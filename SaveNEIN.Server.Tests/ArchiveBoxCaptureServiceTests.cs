using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class ArchiveBoxCaptureServiceTests
{
    [Fact]
    public async Task UrlValidatorRejectsHttpAndUnapprovedHosts()
    {
        var validator = new ArchiveSourceUrlValidator(Options.Create(new ArchiveBoxOptions
        {
            AllowedSourceHosts = ["example.com"]
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(new Uri("http://example.com/"), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(new Uri("https://not-example.com/"), CancellationToken.None));
    }

    [Fact]
    public async Task UrlValidatorRejectsAllowedHostResolvingToLoopback()
    {
        var validator = new ArchiveSourceUrlValidator(Options.Create(new ArchiveBoxOptions
        {
            AllowedSourceHosts = ["localhost"]
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => validator.ValidateAsync(new Uri("https://localhost/"), CancellationToken.None));
    }

    [Fact]
    public async Task CaptureUsesArchiveTimestampAndPersistsVerifiedArtifactManifest()
    {
        var root = CreateArchive("snapshot-1", "Claim text preserved");
        try
        {
            await using var db = CreateDb();
            var capturedAt = new DateTime(2026, 8, 22, 2, 40, 38, DateTimeKind.Utc);
            var service = CreateService(db, root, capturedAt, "Claim text preserved");

            var result = await service.CaptureAsync("test-source", CancellationToken.None);

            Assert.Equal(capturedAt, result.CapturedAtUtc);
            Assert.NotEqual(result.ObservedAtUtc, result.CapturedAtUtc);
            var stored = await db.ArchivedWebSources.SingleAsync();
            Assert.Equal("Verified", stored.VerificationStatus);
            Assert.Equal(200, stored.HttpStatus);
            Assert.Contains("singlefile/singlefile.html", stored.ArtifactManifestJson);
            Assert.Equal(64, stored.NormalizedTextSha256.Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingRequiredTextDoesNotPublishManifest()
    {
        var root = CreateArchive("snapshot-1", "Different captured text");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root, DateTime.UtcNow, "Required claimant wording");

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync("test-source", CancellationToken.None));
            Assert.Empty(await db.ArchivedWebSources.ToListAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedArchiveBoxSnapshotDoesNotPublishManifest()
    {
        var root = CreateArchive("snapshot-1", "Claim text preserved");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root, DateTime.UtcNow, "Claim text preserved", "failed");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync("test-source", CancellationToken.None));
            Assert.Empty(await db.ArchivedWebSources.ToListAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CallerCancellationStopsPollingWithoutPublishingManifest()
    {
        var root = CreateArchive("snapshot-1", "Claim text preserved");
        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root, DateTime.UtcNow, "Claim text preserved", "queued");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureAsync("test-source", cancellation.Token));
            Assert.Empty(await db.ArchivedWebSources.ToListAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static ArchiveBoxCaptureService CreateService(AppDbContext db, string root, DateTime capturedAt, string requiredText, string finalStatus = "sealed")
    {
        var options = Options.Create(new ArchiveBoxOptions
        {
            Enabled = true,
            ApiToken = "test-token",
            DataPath = root,
            ArchiveUserName = "savenein",
            Sources =
            [
                new ArchiveSourceOptions
                {
                    Key = "test-source",
                    Url = "https://example.com/",
                    ObservedAtUtc = new DateTime(2026, 8, 8, 4, 0, 0, DateTimeKind.Utc),
                    ObservationType = "ReportScrape",
                    RequiredTexts = [requiredText]
                }
            ]
        });
        var handler = new SnapshotHandler(capturedAt, finalStatus);
        return new ArchiveBoxCaptureService(new HttpClient(handler) { BaseAddress = new Uri("http://archivebox/") }, db, options, new AllowAllValidator());
    }

    private static string CreateArchive(string snapshotId, string text)
    {
        var root = Path.Combine(Path.GetTempPath(), "savenein-archive-tests", Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(root, "archive", "users", "savenein", "snapshots", "20260822", "example.com", snapshotId);
        WriteArtifact(archive, "htmltotext", "htmltotext.txt", text);
        WriteArtifact(archive, "singlefile", "singlefile.html", "<html><body>preserved</body></html>");
        WriteArtifact(archive, "dom", "output.html", "<html><body>preserved</body></html>");
        WriteArtifact(archive, "screenshot", "screenshot.png", "png");
        WriteArtifact(archive, "headers", "headers.json", "{\"status\":200}");
        WriteArtifact(archive, "wget", "warc", "capture.warc.gz", "warc");
        return root;
    }

    private static void WriteArtifact(string root, params string[] pieces)
    {
        var content = pieces[^1];
        var path = Path.Combine(new[] { root }.Concat(pieces[..^1]).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private sealed class AllowAllValidator : IArchiveSourceUrlValidator
    {
        public Task ValidateAsync(Uri uri, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SnapshotHandler(DateTime capturedAt, string finalStatus) : HttpMessageHandler
    {
        private int _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests++;
            var status = _requests == 1 ? "queued" : finalStatus;
            var downloadedAt = status == "sealed" ? $",\"downloaded_at\":\"{capturedAt:O}\"" : string.Empty;
            var json = $"{{\"id\":\"snapshot-1\",\"status\":\"{status}\",\"archive_path\":\"savenein/20260822/example.com/snapshot-1\"{downloadedAt}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}

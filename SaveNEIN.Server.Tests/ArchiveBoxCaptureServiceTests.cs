using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
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
            Assert.Equal(DateTimeKind.Utc, result.CapturedAtUtc.Kind);
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
    public async Task CaptureSubmitsConfiguredRecursiveDepthAndWaitsForCrawl()
    {
        var root = CreateArchive("snapshot-1", "Claim text preserved");
        var archiveDirectory = GetArchiveDirectory(root, "snapshot-1");
        File.WriteAllText(Path.Combine(archiveDirectory, "singlefile", "singlefile.html"),
            "<html><body><a href=\"/details\">Details</a></body></html>");
        try
        {
            await using var db = CreateDb();
            var capturedAt = new DateTime(2026, 8, 25, 1, 30, 0, DateTimeKind.Utc);
            var handler = new SnapshotHandler(capturedAt, "sealed", archiveDirectory);
            var service = CreateService(db, root, capturedAt, "Claim text preserved", crawlDepth: 7, handler: handler);

            await service.CaptureAsync("test-source", CancellationToken.None);

            Assert.Contains(handler.PostedJson, json => json.Contains("\"depth\":4", StringComparison.Ordinal));
            Assert.Contains(handler.PostedJson, json =>
                json.Contains("https://example.com/details", StringComparison.Ordinal) &&
                json.Contains("\"crawl_id\":\"crawl-1\"", StringComparison.Ordinal));
            Assert.True(handler.CrawlWasPolled);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ArchivedChildPageResolvesOnlyWithinSeedCrawl()
    {
        var root = CreateArchive("snapshot-root", "Claim text preserved");
        var rootDirectory = GetArchiveDirectory(root, "snapshot-root");
        var childDirectory = GetArchiveDirectory(root, "snapshot-child");
        Directory.CreateDirectory(childDirectory);
        WriteArtifact(childDirectory, "singlefile", "singlefile.html", "<html><body>child</body></html>");
        var capturedAt = new DateTime(2026, 8, 25, 1, 30, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(rootDirectory, "index.jsonl"),
            $$"""{"type":"Snapshot","id":"snapshot-root","crawl_id":"crawl-1","url":"https://example.com/","status":"sealed","depth":0,"created_at":"{{capturedAt:O}}"}""");
        File.WriteAllText(Path.Combine(childDirectory, "index.jsonl"),
            $$"""{"type":"Snapshot","id":"snapshot-child","crawl_id":"crawl-1","url":"https://example.com/details","status":"sealed","depth":1,"created_at":"{{capturedAt:O}}"}""");

        try
        {
            await using var db = CreateDb();
            var id = Guid.NewGuid();
            db.ArchivedWebSources.Add(new ArchivedWebSource
            {
                Id = id,
                SourceKey = "test-source",
                OriginalUrl = "https://example.com/",
                ArchiveBoxSnapshotId = "snapshot-root",
                CapturedAtUtc = capturedAt,
                PublicArchivedUrl = $"/api/web-archives/captures/{id}/singlefile",
                ArchiveRelativePath = Path.GetRelativePath(root, rootDirectory).Replace('\\', '/'),
                VerificationStatus = "Verified",
                CreatedAtUtc = capturedAt
            });
            await db.SaveChangesAsync();
            var service = CreateService(db, root, capturedAt, "Claim text preserved");

            var child = await service.GetArchivedPageAsync(id, "https://example.com/details#section", CancellationToken.None);
            var missing = await service.GetArchivedPageAsync(id, "https://example.com/not-captured", CancellationToken.None);

            Assert.NotNull(child);
            Assert.Equal(Path.GetFullPath(Path.Combine(childDirectory, "singlefile", "singlefile.html")), child.Path);
            Assert.Null(missing);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CompletedInterruptedCaptureIsRecoveredFromArchiveStorage()
    {
        var capturedAt = new DateTime(2026, 8, 24, 23, 20, 2, DateTimeKind.Utc);
        var root = CreateArchive("snapshot-recovered", "Claim text preserved");
        var archiveDirectory = Path.Combine(
            root,
            "archive",
            "users",
            "savenein",
            "snapshots",
            "20260822",
            "example.com",
            "snapshot-recovered");
        File.WriteAllText(
            Path.Combine(archiveDirectory, "index.jsonl"),
            $$"""{"type":"Snapshot","id":"snapshot-recovered","url":"https://example.com/","status":"sealed","created_at":"{{capturedAt:O}}"}""");

        try
        {
            await using var db = CreateDb();
            var service = CreateService(db, root, DateTime.UtcNow, "Claim text preserved", "failed");

            var result = await service.CaptureAsync("test-source", CancellationToken.None);

            Assert.Equal("snapshot-recovered", result.ArchiveBoxSnapshotId);
            Assert.Equal(capturedAt, result.CapturedAtUtc);
            Assert.Equal(DateTimeKind.Utc, result.CapturedAtUtc.Kind);
            Assert.Equal("Verified", result.VerificationStatus);
            Assert.Single(await db.ArchivedWebSources.ToListAsync());
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

    private static ArchiveBoxCaptureService CreateService(
        AppDbContext db,
        string root,
        DateTime capturedAt,
        string requiredText,
        string finalStatus = "sealed",
        int crawlDepth = 0,
        SnapshotHandler? handler = null)
    {
        var options = Options.Create(new ArchiveBoxOptions
        {
            Enabled = true,
            ApiToken = "test-token",
            DataPath = root,
            ArchiveUserName = "savenein",
            CrawlDepth = crawlDepth,
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
        handler ??= new SnapshotHandler(capturedAt, finalStatus);
        return new ArchiveBoxCaptureService(new HttpClient(handler) { BaseAddress = new Uri("http://archivebox/") }, db, options, new AllowAllValidator());
    }

    private static string CreateArchive(string snapshotId, string text)
    {
        var root = Path.Combine(Path.GetTempPath(), "savenein-archive-tests", Guid.NewGuid().ToString("N"));
        var archive = GetArchiveDirectory(root, snapshotId);
        WriteArtifact(archive, "htmltotext", "htmltotext.txt", text);
        WriteArtifact(archive, "singlefile", "singlefile.html", "<html><body>preserved</body></html>");
        WriteArtifact(archive, "dom", "output.html", "<html><body>preserved</body></html>");
        WriteArtifact(archive, "screenshot", "screenshot.png", "png");
        WriteArtifact(archive, "headers", "headers.json", "{\"status\":200}");
        WriteArtifact(archive, "wget", "warc", "capture.warc.gz", "warc");
        return root;
    }

    private static string GetArchiveDirectory(string root, string snapshotId) =>
        Path.Combine(root, "archive", "users", "savenein", "snapshots", "20260822", "example.com", snapshotId);

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

    private sealed class SnapshotHandler(DateTime capturedAt, string finalStatus, string? archiveDirectory = null) : HttpMessageHandler
    {
        private int _requests;
        public List<string> PostedJson { get; } = [];
        public bool CrawlWasPolled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests++;
            if (request.Method == HttpMethod.Post)
                PostedJson.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            var isCrawl = request.RequestUri!.AbsolutePath.Contains("/any/", StringComparison.Ordinal);
            CrawlWasPolled |= isCrawl;
            var status = _requests == 1 ? "queued" : finalStatus;
            if (isCrawl) status = finalStatus;
            var downloadedAt = status == "sealed" ? $",\"downloaded_at\":\"{capturedAt:O}\"" : string.Empty;
            var id = isCrawl ? "crawl-1" : "snapshot-1";
            if (!isCrawl && status == "sealed" && archiveDirectory is not null)
            {
                File.WriteAllText(Path.Combine(archiveDirectory, "index.jsonl"),
                    $"{{\"type\":\"Snapshot\",\"id\":\"snapshot-1\",\"crawl_id\":\"crawl-1\",\"url\":\"https://example.com/\",\"status\":\"sealed\",\"depth\":4,\"created_at\":\"{capturedAt:O}\"}}");
            }
            var json = $"{{\"id\":\"{id}\",\"crawl_id\":\"crawl-1\",\"status\":\"{status}\",\"archive_path\":\"savenein/20260822/example.com/snapshot-1\"{downloadedAt}}}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

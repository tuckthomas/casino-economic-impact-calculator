using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services;

public sealed record WebArchiveMetadata(
    Guid Id,
    string SourceKey,
    string OriginalUrl,
    DateTime? ObservedAtUtc,
    string? ObservationType,
    string ArchiveBoxSnapshotId,
    DateTime CapturedAtUtc,
    string ArchivedUrl,
    DateTime? VerifiedAtUtc,
    string VerificationStatus);

public interface IArchiveBoxCaptureService
{
    Task<WebArchiveMetadata> CaptureAsync(string sourceKey, CancellationToken cancellationToken);
    Task<WebArchiveMetadata?> GetLatestAsync(string sourceKey, CancellationToken cancellationToken);
    Task<(string Path, ArchivedWebSource Capture)?> GetSingleFileAsync(Guid id, CancellationToken cancellationToken);
}

public interface IArchiveSourceUrlValidator
{
    Task ValidateAsync(Uri uri, CancellationToken cancellationToken);
}

public sealed class ArchiveSourceUrlValidator(IOptions<ArchiveBoxOptions> options) : IArchiveSourceUrlValidator
{
    private readonly HashSet<string> _allowedHosts = options.Value.AllowedSourceHosts
        .Select(host => host.Trim().TrimEnd('.'))
        .Where(host => host.Length > 0)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task ValidateAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || uri.Port != 443)
            throw new InvalidOperationException("Archive sources must use an ordinary HTTPS URL without embedded credentials.");

        var host = uri.IdnHost.TrimEnd('.');
        if (!_allowedHosts.Contains(host))
            throw new InvalidOperationException($"Archive source host '{host}' is not approved.");

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsNonPublicAddress))
            throw new InvalidOperationException("Archive source resolved to a non-public network address.");
    }

    internal static bool IsNonPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return true;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 0 ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
               (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc);
    }
}

public sealed class ArchiveBoxCaptureService : IArchiveBoxCaptureService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly AppDbContext _db;
    private readonly ArchiveBoxOptions _options;
    private readonly IArchiveSourceUrlValidator _urlValidator;

    public ArchiveBoxCaptureService(HttpClient http, AppDbContext db, IOptions<ArchiveBoxOptions> options, IArchiveSourceUrlValidator urlValidator)
    {
        _http = http;
        _db = db;
        _options = options.Value;
        _urlValidator = urlValidator;
        if (!string.IsNullOrWhiteSpace(_options.ApiToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
    }

    public async Task<WebArchiveMetadata> CaptureAsync(string sourceKey, CancellationToken cancellationToken)
    {
        if (!_options.Enabled) throw new InvalidOperationException("ArchiveBox capture is disabled.");
        if (string.IsNullOrWhiteSpace(_options.ApiToken)) throw new InvalidOperationException("ArchiveBox API token is not configured.");

        var source = _options.Sources.SingleOrDefault(candidate => string.Equals(candidate.Key, sourceKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Unknown archive source key '{sourceKey}'.");
        var uri = new Uri(source.Url, UriKind.Absolute);
        await _urlValidator.ValidateAsync(uri, cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.CaptureTimeoutSeconds, 30, 3600)));

        using var response = await _http.PostAsJsonAsync("api/v1/core/snapshots", new { url = uri.AbsoluteUri, depth = 0 }, JsonOptions, timeout.Token);
        response.EnsureSuccessStatusCode();
        var snapshot = await response.Content.ReadFromJsonAsync<ArchiveBoxSnapshot>(JsonOptions, timeout.Token)
            ?? throw new InvalidOperationException("ArchiveBox returned an empty capture response.");

        while (!string.Equals(snapshot.Status, "sealed", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(snapshot.Status, "failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"ArchiveBox capture {snapshot.Id} failed.");
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
            snapshot = await _http.GetFromJsonAsync<ArchiveBoxSnapshot>($"api/v1/core/snapshot/{snapshot.Id}", JsonOptions, timeout.Token)
                ?? throw new InvalidOperationException("ArchiveBox snapshot disappeared while capture was running.");
        }

        if (snapshot.DownloadedAt is null) throw new InvalidOperationException("Sealed ArchiveBox snapshot has no capture timestamp.");
        var archiveDirectory = ResolveArchiveDirectory(snapshot.ArchivePath);
        var textPath = RequireFile(archiveDirectory, "htmltotext", "htmltotext.txt");
        var singleFilePath = RequireFile(archiveDirectory, "singlefile", "singlefile.html");
        RequireFile(archiveDirectory, "dom", "output.html");
        RequireFile(archiveDirectory, "screenshot", "screenshot.png");
        if (!Directory.EnumerateFiles(Path.Combine(archiveDirectory, "wget"), "*.warc.gz", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("ArchiveBox capture did not produce a WARC artifact.");

        var normalizedText = await File.ReadAllTextAsync(textPath, timeout.Token);
        var headersPath = RequireFile(archiveDirectory, "headers", "headers.json");
        var httpStatus = await ReadHttpStatusAsync(headersPath, timeout.Token);
        var missingTexts = source.RequiredTexts.Where(required => !normalizedText.Contains(required, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (missingTexts.Length > 0)
            throw new InvalidOperationException($"Capture verification failed; {missingTexts.Length} required text fragment(s) were absent.");

        var artifacts = Directory.EnumerateFiles(archiveDirectory, "*", SearchOption.AllDirectories)
            .Where(IsEvidenceArtifact)
            .Select(path => new ArchiveArtifact(
                Path.GetRelativePath(archiveDirectory, path).Replace('\\', '/'),
                new FileInfo(path).Length,
                ComputeSha256(path)))
            .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
            .ToArray();

        var record = new ArchivedWebSource
        {
            Id = Guid.NewGuid(),
            SourceKey = source.Key,
            OriginalUrl = uri.AbsoluteUri,
            ObservedAtUtc = source.ObservedAtUtc,
            ObservationType = source.ObservationType,
            ArchiveBoxSnapshotId = snapshot.Id,
            CapturedAtUtc = snapshot.DownloadedAt.Value,
            PublicArchivedUrl = string.Empty,
            HttpStatus = httpStatus,
            CaptureStatus = snapshot.Status,
            NormalizedText = normalizedText,
            NormalizedTextSha256 = ComputeSha256(Encoding.UTF8.GetBytes(normalizedText)),
            ArtifactManifestJson = JsonSerializer.Serialize(artifacts, JsonOptions),
            ArchiveRelativePath = Path.GetRelativePath(_options.DataPath, archiveDirectory).Replace('\\', '/'),
            VerifiedAtUtc = DateTime.UtcNow,
            VerificationStatus = "Verified",
            VerificationNote = $"Matched {source.RequiredTexts.Length} configured source-text fragment(s).",
            CreatedAtUtc = DateTime.UtcNow
        };
        record.PublicArchivedUrl = $"/api/web-archives/captures/{record.Id}/singlefile";

        _db.ArchivedWebSources.Add(record);
        await _db.SaveChangesAsync(timeout.Token);
        _ = singleFilePath;
        return ToMetadata(record);
    }

    public async Task<WebArchiveMetadata?> GetLatestAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var record = await _db.ArchivedWebSources.AsNoTracking()
            .Where(capture => capture.SourceKey == sourceKey && capture.VerificationStatus == "Verified")
            .OrderByDescending(capture => capture.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : ToMetadata(record);
    }

    public async Task<(string Path, ArchivedWebSource Capture)?> GetSingleFileAsync(Guid id, CancellationToken cancellationToken)
    {
        var capture = await _db.ArchivedWebSources.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.VerificationStatus == "Verified", cancellationToken);
        if (capture is null) return null;
        var directory = ResolveStoredRelativePath(capture.ArchiveRelativePath);
        return (RequireFile(directory, "singlefile", "singlefile.html"), capture);
    }

    private string ResolveArchiveDirectory(string archivePath)
    {
        var pieces = archivePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2 || !string.Equals(pieces[0], _options.ArchiveUserName, StringComparison.Ordinal))
            throw new InvalidOperationException("ArchiveBox returned an unexpected archive path.");
        var relative = Path.Combine(new[] { "archive", "users", _options.ArchiveUserName, "snapshots" }.Concat(pieces.Skip(1)).ToArray());
        return ResolveStoredRelativePath(relative);
    }

    private string ResolveStoredRelativePath(string relativePath)
    {
        var root = Path.GetFullPath(_options.DataPath);
        var resolved = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Archive path escaped the configured data directory.");
        return resolved;
    }

    private static string RequireFile(string root, params string[] pieces)
    {
        var path = Path.Combine(new[] { root }.Concat(pieces).ToArray());
        if (!File.Exists(path)) throw new InvalidOperationException($"Required ArchiveBox artifact is missing: {string.Join('/', pieces)}");
        return path;
    }

    private static bool IsEvidenceArtifact(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith(".warc.gz", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/dom/output.html", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/htmltotext/htmltotext.txt", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/singlefile/singlefile.html", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/screenshot/screenshot.png", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/archivewebpage/archivewebpage.wacz", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith("/headers/headers.json", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int?> ReadHttpStatusAsync(string headersPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(headersPath);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("status", out var status)) return null;
        return status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var numeric)
            ? numeric
            : int.TryParse(status.GetString(), out numeric) ? numeric : null;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static WebArchiveMetadata ToMetadata(ArchivedWebSource capture) => new(
        capture.Id, capture.SourceKey, capture.OriginalUrl, capture.ObservedAtUtc, capture.ObservationType,
        capture.ArchiveBoxSnapshotId, capture.CapturedAtUtc, capture.PublicArchivedUrl,
        capture.VerifiedAtUtc, capture.VerificationStatus);

    private sealed record ArchiveArtifact(string Path, long SizeBytes, string Sha256);
    private sealed class ArchiveBoxSnapshot
    {
        public string Id { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        [JsonPropertyName("downloaded_at")]
        public DateTime? DownloadedAt { get; init; }
        [JsonPropertyName("archive_path")]
        public string ArchivePath { get; init; } = string.Empty;
    }
}

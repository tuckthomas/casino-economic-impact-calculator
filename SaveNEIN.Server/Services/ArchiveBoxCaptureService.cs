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
    Task<ArchivedPage?> GetArchivedPageAsync(Guid id, string? targetUrl, CancellationToken cancellationToken);
}

public sealed record ArchivedPage(string Path, ArchivedWebSource Capture, Uri PageUrl);

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

        var recoveredCapture = await TryRecoverCompletedCaptureAsync(source, uri, cancellationToken);
        if (recoveredCapture is not null)
            return recoveredCapture;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.CaptureTimeoutSeconds, 30, 3600)));

        var crawlDepth = Math.Clamp(_options.CrawlDepth, 0, 4);
        using var response = await _http.PostAsJsonAsync(
            "api/v1/core/snapshots",
            new { url = uri.AbsoluteUri, depth = crawlDepth },
            JsonOptions,
            timeout.Token);
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

        if (crawlDepth > 0 && !string.IsNullOrWhiteSpace(snapshot.CrawlId))
        {
            var crawl = await _http.GetFromJsonAsync<ArchiveBoxCrawl>($"api/v1/core/any/{snapshot.CrawlId}", JsonOptions, timeout.Token)
                ?? throw new InvalidOperationException("ArchiveBox crawl disappeared while capture was running.");
            while (!string.Equals(crawl.Status, "sealed", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(crawl.Status, "failed", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"ArchiveBox crawl {crawl.Id} failed.");
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                crawl = await _http.GetFromJsonAsync<ArchiveBoxCrawl>($"api/v1/core/any/{crawl.Id}", JsonOptions, timeout.Token)
                    ?? throw new InvalidOperationException("ArchiveBox crawl disappeared while capture was running.");
            }
        }

        if (snapshot.DownloadedAt is null) throw new InvalidOperationException("Sealed ArchiveBox snapshot has no capture timestamp.");
        var archiveDirectory = ResolveArchiveDirectory(snapshot.ArchivePath);
        return await PersistVerifiedCaptureAsync(
            source,
            uri,
            snapshot.Id,
            snapshot.DownloadedAt.Value,
            snapshot.Status,
            archiveDirectory,
            timeout.Token);
    }

    private async Task<WebArchiveMetadata?> TryRecoverCompletedCaptureAsync(
        ArchiveSourceOptions source,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var snapshotRoot = Path.Combine(
            _options.DataPath,
            "archive",
            "users",
            _options.ArchiveUserName,
            "snapshots");
        if (!Directory.Exists(snapshotRoot)) return null;

        foreach (var indexPath in Directory
                     .EnumerateFiles(snapshotRoot, "index.jsonl", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoredArchiveBoxSnapshot? storedSnapshot;
            try
            {
                using var reader = new StreamReader(indexPath);
                var firstLine = await reader.ReadLineAsync(cancellationToken);
                storedSnapshot = string.IsNullOrWhiteSpace(firstLine)
                    ? null
                    : JsonSerializer.Deserialize<StoredArchiveBoxSnapshot>(firstLine, JsonOptions);
            }
            catch (IOException)
            {
                continue;
            }
            catch (JsonException)
            {
                continue;
            }

            if (storedSnapshot is null ||
                !string.Equals(storedSnapshot.Type, "Snapshot", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(storedSnapshot.Status, "sealed", StringComparison.OrdinalIgnoreCase) ||
                storedSnapshot.Depth < Math.Clamp(_options.CrawlDepth, 0, 4) ||
                !Uri.TryCreate(storedSnapshot.Url, UriKind.Absolute, out var storedUri) ||
                !Uri.Compare(uri, storedUri, UriComponents.AbsoluteUri, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase).Equals(0))
            {
                continue;
            }

            var alreadyRegistered = await _db.ArchivedWebSources.AsNoTracking()
                .AnyAsync(capture => capture.ArchiveBoxSnapshotId == storedSnapshot.Id, cancellationToken);
            if (alreadyRegistered) continue;

            var archiveDirectory = Path.GetDirectoryName(indexPath)!;
            if (!HasRequiredArtifacts(archiveDirectory)) continue;

            return await PersistVerifiedCaptureAsync(
                source,
                uri,
                storedSnapshot.Id,
                storedSnapshot.CreatedAt.UtcDateTime,
                storedSnapshot.Status,
                archiveDirectory,
                cancellationToken);
        }

        return null;
    }

    private async Task<WebArchiveMetadata> PersistVerifiedCaptureAsync(
        ArchiveSourceOptions source,
        Uri uri,
        string snapshotId,
        DateTime capturedAtUtc,
        string captureStatus,
        string archiveDirectory,
        CancellationToken cancellationToken)
    {
        var textPath = Path.Combine(archiveDirectory, "htmltotext", "htmltotext.txt");
        var publicArtifactPath = FindPublicArtifact(archiveDirectory)
            ?? throw new InvalidOperationException("ArchiveBox capture did not produce a browser-viewable artifact.");
        var wgetDirectory = Path.Combine(archiveDirectory, "wget");
        if (!Directory.Exists(wgetDirectory) ||
            !Directory.EnumerateFiles(wgetDirectory, "*.warc.gz", SearchOption.AllDirectories).Any())
            throw new InvalidOperationException("ArchiveBox capture did not produce a WARC artifact.");

        var normalizedText = File.Exists(textPath)
            ? await File.ReadAllTextAsync(textPath, cancellationToken)
            : string.Empty;
        var headersPath = RequireFile(archiveDirectory, "headers", "headers.json");
        var httpStatus = await ReadHttpStatusAsync(headersPath, cancellationToken);
        if (source.RequiredTexts.Length > 0 && string.IsNullOrEmpty(normalizedText))
            throw new InvalidOperationException("Capture verification requires extracted text, but ArchiveBox did not produce it.");
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
            ArchiveBoxSnapshotId = snapshotId,
            CapturedAtUtc = capturedAtUtc,
            PublicArchivedUrl = string.Empty,
            HttpStatus = httpStatus,
            CaptureStatus = captureStatus,
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
        await _db.SaveChangesAsync(cancellationToken);
        _ = publicArtifactPath;
        return ToMetadata(record);
    }

    private static bool HasRequiredArtifacts(string archiveDirectory) =>
        FindPublicArtifact(archiveDirectory) is not null &&
        File.Exists(Path.Combine(archiveDirectory, "headers", "headers.json")) &&
        Directory.Exists(Path.Combine(archiveDirectory, "wget")) &&
        Directory.EnumerateFiles(Path.Combine(archiveDirectory, "wget"), "*.warc.gz", SearchOption.AllDirectories).Any();

    public async Task<WebArchiveMetadata?> GetLatestAsync(string sourceKey, CancellationToken cancellationToken)
    {
        var record = await _db.ArchivedWebSources.AsNoTracking()
            .Where(capture => capture.SourceKey == sourceKey && capture.VerificationStatus == "Verified")
            .OrderByDescending(capture => capture.CapturedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return record is null ? null : ToMetadata(record);
    }

    public async Task<ArchivedPage?> GetArchivedPageAsync(Guid id, string? targetUrl, CancellationToken cancellationToken)
    {
        var capture = await _db.ArchivedWebSources.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id && item.VerificationStatus == "Verified", cancellationToken);
        if (capture is null) return null;

        var rootDirectory = ResolveStoredRelativePath(capture.ArchiveRelativePath);
        var rootSnapshot = await ReadStoredSnapshotAsync(Path.Combine(rootDirectory, "index.jsonl"), cancellationToken);
        if (rootSnapshot is null || !Uri.TryCreate(rootSnapshot.Url, UriKind.Absolute, out var rootUri)) return null;

        var directory = rootDirectory;
        var pageUri = rootUri;
        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var requestedUri) ||
                (requestedUri.Scheme != Uri.UriSchemeHttp && requestedUri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            requestedUri = WithoutFragment(requestedUri);
            var matchingSnapshot = await FindCrawlSnapshotAsync(rootSnapshot.CrawlId, requestedUri, cancellationToken);
            if (matchingSnapshot is null) return null;
            directory = matchingSnapshot.Value.Directory;
            pageUri = matchingSnapshot.Value.Url;
        }

        var publicArtifactPath = FindPublicArtifact(directory);
        return publicArtifactPath is not null ? new ArchivedPage(publicArtifactPath, capture, pageUri) : null;
    }

    private async Task<(string Directory, Uri Url)?> FindCrawlSnapshotAsync(
        string crawlId,
        Uri requestedUri,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(crawlId)) return null;
        var snapshotRoot = Path.Combine(_options.DataPath, "archive", "users", _options.ArchiveUserName, "snapshots");
        if (!Directory.Exists(snapshotRoot)) return null;

        foreach (var indexPath in Directory.EnumerateFiles(snapshotRoot, "index.jsonl", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadStoredSnapshotAsync(indexPath, cancellationToken);
            if (snapshot is null ||
                !string.Equals(snapshot.CrawlId, crawlId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(snapshot.Status, "sealed", StringComparison.OrdinalIgnoreCase) ||
                !Uri.TryCreate(snapshot.Url, UriKind.Absolute, out var snapshotUri) ||
                Uri.Compare(
                    requestedUri,
                    WithoutFragment(snapshotUri),
                    UriComponents.HttpRequestUrl,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) != 0)
            {
                continue;
            }

            var directory = Path.GetDirectoryName(indexPath)!;
            if (FindPublicArtifact(directory) is not null)
                return (directory, snapshotUri);
        }

        return null;
    }

    private static async Task<StoredArchiveBoxSnapshot?> ReadStoredSnapshotAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(indexPath)) return null;
        try
        {
            using var reader = new StreamReader(indexPath);
            var firstLine = await reader.ReadLineAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(firstLine)
                ? null
                : JsonSerializer.Deserialize<StoredArchiveBoxSnapshot>(firstLine, JsonOptions);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri WithoutFragment(Uri uri)
    {
        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri;
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

    private static string? FindPublicArtifact(string archiveDirectory)
    {
        var singleFile = Path.Combine(archiveDirectory, "singlefile", "singlefile.html");
        if (File.Exists(singleFile)) return singleFile;

        var renderedDom = Path.Combine(archiveDirectory, "dom", "output.html");
        if (File.Exists(renderedDom)) return renderedDom;

        return Directory.Exists(archiveDirectory)
            ? Directory.EnumerateFiles(archiveDirectory, "*.pdf", SearchOption.AllDirectories).FirstOrDefault()
            : null;
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
               normalized.EndsWith("/headers/headers.json", StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
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
        [JsonPropertyName("crawl_id")]
        public string CrawlId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        [JsonPropertyName("downloaded_at")]
        public DateTime? DownloadedAt { get; init; }
        [JsonPropertyName("archive_path")]
        public string ArchivePath { get; init; } = string.Empty;
    }

    private sealed class ArchiveBoxCrawl
    {
        public string Id { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
    }

    private sealed class StoredArchiveBoxSnapshot
    {
        public string Type { get; init; } = string.Empty;
        public string Id { get; init; } = string.Empty;
        [JsonPropertyName("crawl_id")]
        public string CrawlId { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public int Depth { get; init; }
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; init; }
    }
}

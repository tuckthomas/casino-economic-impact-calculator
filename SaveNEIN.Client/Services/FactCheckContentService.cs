using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using SaveNEIN.Client.Models;

namespace SaveNEIN.Client.Services;

public sealed class FactCheckContentService(HttpClient httpClient)
{
    private const string ResourceName = "SaveNEIN.Client.Data.fact-checks.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, Task<WebArchiveMetadata?>> _archiveRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _archiveRequestLock = new();
    private FactCheckDocument? _document;

    public FactCheckDocument GetDocument()
    {
        if (_document is not null)
        {
            return _document;
        }

        using var stream = typeof(FactCheckContentService).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded fact-check document '{ResourceName}' was not found.");

        _document = JsonSerializer.Deserialize<FactCheckDocument>(stream, JsonOptions)
            ?? throw new InvalidOperationException("The embedded fact-check document is empty or invalid.");

        return _document;
    }

    public async Task<IReadOnlyList<FactCheckClaim>> AttachVerifiedArchivesAsync(
        IReadOnlyList<FactCheckClaim> claims)
    {
        var sourceKeys = claims
            .Select(claim => claim.ArchiveSourceKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

        var archiveResults = await Task.WhenAll(sourceKeys.Select(async sourceKey =>
            (SourceKey: sourceKey, Archive: await GetArchiveAsync(sourceKey).ConfigureAwait(false))))
            .ConfigureAwait(false);

        var archives = archiveResults
            .Where(result => result.Archive is not null)
            .ToDictionary(result => result.SourceKey, result => result.Archive!, StringComparer.OrdinalIgnoreCase);

        return claims.Select(claim =>
            claim.ArchiveSourceKey is not null && archives.TryGetValue(claim.ArchiveSourceKey, out var archive)
                ? claim with
                {
                    ClaimSourceArchivedUrl = ResolveArchivedUrl(archive.ArchivedUrl),
                    ClaimSourceCapturedAtUtc = archive.CapturedAtUtc
                }
                : claim).ToArray();
    }

    private string ResolveArchivedUrl(string archivedUrl)
    {
        if (!Uri.TryCreate(archivedUrl, UriKind.Relative, out var relativeUrl) ||
            httpClient.BaseAddress is not { IsLoopback: true })
        {
            return archivedUrl;
        }

        return new Uri(new Uri("https://savenein.com"), relativeUrl).AbsoluteUri;
    }

    private Task<WebArchiveMetadata?> GetArchiveAsync(string sourceKey)
    {
        lock (_archiveRequestLock)
        {
            if (_archiveRequests.TryGetValue(sourceKey, out var existingRequest))
            {
                return existingRequest;
            }

            var request = LoadArchiveAsync(sourceKey);
            _archiveRequests[sourceKey] = request;
            return request;
        }
    }

    private async Task<WebArchiveMetadata?> LoadArchiveAsync(string sourceKey)
    {
        try
        {
            return await httpClient
                .GetFromJsonAsync<WebArchiveMetadata>($"api/web-archives/{Uri.EscapeDataString(sourceKey)}/latest")
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

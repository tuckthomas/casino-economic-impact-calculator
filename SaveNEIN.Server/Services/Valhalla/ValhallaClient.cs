using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace SaveNEIN.Server.Services.Valhalla;

public class ValhallaClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ValhallaClient> _logger;

    public ValhallaClient(HttpClient httpClient, ILogger<ValhallaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ValhallaRoutingGraphIdentity> GetRoutingGraphIdentityAsync(
        CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync("/status", ct);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<ValhallaStatusResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Valhalla returned an empty status payload.");
        if (string.IsNullOrWhiteSpace(status.Version))
        {
            throw new InvalidOperationException("Valhalla status did not include a version.");
        }

        var identity = $"{status.Version}|{status.TilesetLastModified}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return new ValhallaRoutingGraphIdentity(status.Version, status.TilesetLastModified, hash);
    }

    public async Task<ValhallaMatrixResult> GetDriveTimeMatrixAsync(
        IReadOnlyList<ValhallaMatrixLocation> sources,
        IReadOnlyList<ValhallaMatrixLocation> targets,
        string costingProfile = "auto",
        CancellationToken ct = default)
    {
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one matrix source is required.", nameof(sources));
        }
        if (targets.Count == 0)
        {
            throw new ArgumentException("At least one matrix target is required.", nameof(targets));
        }
        if (string.IsNullOrWhiteSpace(costingProfile))
        {
            throw new ArgumentException("A costing profile is required.", nameof(costingProfile));
        }

        ValidateLocations(sources, nameof(sources));
        ValidateLocations(targets, nameof(targets));
        var request = new
        {
            sources = sources.Select(source => new { lat = source.Latitude, lon = source.Longitude }),
            targets = targets.Select(target => new { lat = target.Latitude, lon = target.Longitude }),
            costing = costingProfile,
            units = "kilometers",
            verbose = true
        };

        using var response = await _httpClient.PostAsJsonAsync("/sources_to_targets", request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Valhalla matrix returned {StatusCode}. Body: {Body}",
                (int)response.StatusCode,
                body);
            var responseDetail = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (responseDetail.Length > 500)
            {
                responseDetail = responseDetail[..500];
            }
            throw new HttpRequestException(
                $"Valhalla matrix request failed with HTTP {(int)response.StatusCode}. " +
                $"Valhalla response: {responseDetail}",
                null,
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<ValhallaMatrixResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Valhalla returned an empty matrix payload.");
        if (payload.SourcesToTargets.Count != sources.Count)
        {
            throw new InvalidOperationException(
                $"Valhalla returned {payload.SourcesToTargets.Count} source rows for {sources.Count} sources.");
        }

        var cells = new List<ValhallaMatrixCell>(sources.Count * targets.Count);
        for (var sourceIndex = 0; sourceIndex < payload.SourcesToTargets.Count; sourceIndex++)
        {
            var row = payload.SourcesToTargets[sourceIndex];
            if (row.Count != targets.Count)
            {
                throw new InvalidOperationException(
                    $"Valhalla matrix row {sourceIndex} returned {row.Count} targets for {targets.Count} requested targets.");
            }

            for (var targetIndex = 0; targetIndex < row.Count; targetIndex++)
            {
                var cell = row[targetIndex];
                cells.Add(new ValhallaMatrixCell(
                    sourceIndex,
                    targetIndex,
                    cell.TimeSeconds is not null && cell.DistanceKilometers is not null,
                    cell.TimeSeconds / 60d,
                    cell.DistanceKilometers * 1_000d));
            }
        }

        return new ValhallaMatrixResult(payload.Algorithm ?? "unknown", payload.Units ?? "kilometers", cells);
    }

    public async Task<string?> GetIsochroneJsonAsync(double lat, double lon, int minutes, CancellationToken ct = default)
    {
        return await GetIsochroneJsonAsync(lat, lon, new[] { minutes }, ct);
    }

    public async Task<string?> GetIsochroneJsonAsync(double lat, double lon, IReadOnlyList<int> minutes, CancellationToken ct = default)
    {
        // Valhalla /isochrone endpoint
        // Ref: https://valhalla.github.io/valhalla/api/isochrone/api-reference/
        var contours = minutes
            .Select((m, index) => new { time = m, color = index == 0 ? "ff0000" : "0000ff" })
            .ToArray();

        var request = new
        {
            locations = new[]
            {
                new { lat = lat, lon = lon }
            },
            costing = "auto",
            contours,
            polygons = true,
            denoise = 0.1 // cleanup noisy edges
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/isochrone", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                var requestBody = JsonSerializer.Serialize(request);
                _logger.LogError(
                    "Valhalla returned {StatusCode}. Body: {Body}. Request: {Request}",
                    (int)response.StatusCode,
                    errorBody,
                    requestBody);
                return null;
            }

            // We return the raw string because we'll likely pass it to PostGIS 
            // or parse it into NetTopologySuite objects later.
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Error calling Valhalla isochrone API");
            // In a real scenario, consider retry logic or returning null/throwing based on resilience policy
            throw;
        }
    }

    private static void ValidateLocations(
        IReadOnlyCollection<ValhallaMatrixLocation> locations,
        string parameterName)
    {
        foreach (var location in locations)
        {
            if (!double.IsFinite(location.Latitude) || location.Latitude is < -90 or > 90 ||
                !double.IsFinite(location.Longitude) || location.Longitude is < -180 or > 180)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Matrix coordinates must be valid WGS84 coordinates.");
            }
        }
    }
}

public sealed record ValhallaMatrixLocation(double Latitude, double Longitude);

public sealed record ValhallaRoutingGraphIdentity(
    string ValhallaVersion,
    long? TilesetLastModified,
    string GraphHash);

public sealed record ValhallaMatrixCell(
    int SourceIndex,
    int TargetIndex,
    bool RouteFound,
    double? TravelTimeMinutes,
    double? RoutedDistanceMeters);

public sealed record ValhallaMatrixResult(
    string Algorithm,
    string Units,
    IReadOnlyList<ValhallaMatrixCell> Cells);

internal sealed class ValhallaStatusResponse
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("tileset_last_modified")]
    public long? TilesetLastModified { get; set; }
}

internal sealed class ValhallaMatrixResponse
{
    [JsonPropertyName("algorithm")]
    public string? Algorithm { get; set; }

    [JsonPropertyName("units")]
    public string? Units { get; set; }

    [JsonPropertyName("sources_to_targets")]
    public List<List<ValhallaMatrixResponseCell>> SourcesToTargets { get; set; } = [];
}

internal sealed class ValhallaMatrixResponseCell
{
    [JsonPropertyName("time")]
    public double? TimeSeconds { get; set; }

    [JsonPropertyName("distance")]
    public double? DistanceKilometers { get; set; }
}

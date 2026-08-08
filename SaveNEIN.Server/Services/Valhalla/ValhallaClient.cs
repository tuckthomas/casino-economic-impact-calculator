using System.Text.Json;
using System.Text.Json.Serialization;

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
}

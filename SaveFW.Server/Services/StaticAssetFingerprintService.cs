using System.Text.Json;

namespace SaveFW.Server.Services;

public sealed class StaticAssetFingerprintService
{
    private readonly IReadOnlyDictionary<string, string> _routes;

    public StaticAssetFingerprintService(IWebHostEnvironment environment)
    {
        _routes = LoadRoutes(environment);
    }

    public string GetPath(string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(logicalPath))
        {
            return logicalPath;
        }

        var normalized = logicalPath.TrimStart('/');
        if (_routes.TryGetValue(normalized, out var route))
        {
            return "/" + route.TrimStart('/');
        }

        return "/" + normalized;
    }

    private static IReadOnlyDictionary<string, string> LoadRoutes(IWebHostEnvironment environment)
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "SaveFW.Server.staticwebassets.endpoints.json");
        if (!File.Exists(manifestPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(manifestPath);
        using var document = JsonDocument.Parse(stream);

        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!document.RootElement.TryGetProperty("Endpoints", out var endpoints) || endpoints.ValueKind != JsonValueKind.Array)
        {
            return routes;
        }

        foreach (var endpoint in endpoints.EnumerateArray())
        {
            if (!endpoint.TryGetProperty("Route", out var routeElement) ||
                !endpoint.TryGetProperty("EndpointProperties", out var propertiesElement))
            {
                continue;
            }

            var route = routeElement.GetString();
            if (string.IsNullOrWhiteSpace(route) || route.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) || route.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? label = null;
            bool hasFingerprint = false;

            foreach (var property in propertiesElement.EnumerateArray())
            {
                if (!property.TryGetProperty("Name", out var nameElement) ||
                    !property.TryGetProperty("Value", out var valueElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                var value = valueElement.GetString();

                if (string.Equals(name, "label", StringComparison.OrdinalIgnoreCase))
                {
                    label = value;
                }
                else if (string.Equals(name, "fingerprint", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    hasFingerprint = true;
                }
            }

            if (!hasFingerprint || string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            routes[label.TrimStart('/')] = route;
        }

        return routes;
    }
}

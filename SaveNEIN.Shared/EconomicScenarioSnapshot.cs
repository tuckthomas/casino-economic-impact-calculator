using System.Text.Json;

namespace SaveNEIN.Shared;

/// <summary>The versioned location and visualization context passed between workflow stages.</summary>
public sealed record EconomicScenarioSnapshot(
    string Version,
    string? StateFips,
    string? CountyFips,
    double? MarkerLatitude,
    double? MarkerLongitude,
    JsonElement ZoneConfiguration,
    JsonElement PreviewOutputs)
{
    public const string CurrentVersion = "economic-impact-client-v1";
    public const int MaximumPayloadBytes = 131_072;

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException("Unsupported or missing economic-impact scenario version.");
        if (!IsDigits(StateFips, 2))
            throw new InvalidDataException("A two-digit state FIPS code is required.");
        if (CountyFips is not null && (!IsDigits(CountyFips, 5) || !CountyFips.StartsWith(StateFips!, StringComparison.Ordinal)))
            throw new InvalidDataException("County FIPS must contain five digits and belong to the selected state.");
        if (MarkerLatitude is not double latitude || !double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            MarkerLongitude is not double longitude || !double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new InvalidDataException("A valid candidate latitude and longitude are required.");
        if (ZoneConfiguration.ValueKind != JsonValueKind.Object || PreviewOutputs.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Zone configuration and visualization outputs must be JSON objects.");
    }

    private static bool IsDigits(string? value, int length) =>
        value?.Length == length && value.All(character => character is >= '0' and <= '9');
}

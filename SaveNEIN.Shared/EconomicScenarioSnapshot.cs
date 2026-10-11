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
    JsonElement PreviewOutputs,
    JsonElement? VisualizationContext = null)
{
    public const string CurrentVersion = "economic-impact-client-v1";
    public const int MaximumPayloadBytes = 131_072;

    /// <summary>Reads the selected projection without treating it as the fiscal effective date.</summary>
    public Modeling.PopulationProjectionRequest ReadPopulationProjection()
    {
        try
        {
            if (VisualizationContext is not { } context ||
                context.GetProperty("version").GetString() != "economic-impact-visualization-v3")
                throw new InvalidDataException("The saved population projection is unavailable. Return to location selection and save it again.");
            var projection = context.GetProperty("impactBreakdown").GetProperty("populationProjection");
            var request = new Modeling.PopulationProjectionRequest(1,
                projection.GetProperty("baseYear").GetInt32(), projection.GetProperty("targetYear").GetInt32(),
                projection.GetProperty("annualGrowthRate").GetDouble() / 100);
            var result = Modeling.PopulationProjectionArithmetic.Calculate(request);
            var multiplier = projection.GetProperty("multiplier").GetDouble();
            if (result.ProjectionYears < 0 || projection.GetProperty("yearsFromBase").GetInt32() != result.ProjectionYears ||
                !double.IsFinite(multiplier) || multiplier <= 0 ||
                Math.Abs(multiplier - result.ProjectedPopulation) > Math.Max(1e-6, result.ProjectedPopulation * 1e-9) ||
                projection.GetProperty("usesProjectedAdults").GetBoolean() != (result.ProjectionYears > 0 && request.AnnualGrowthRate != 0))
                throw new InvalidDataException("The saved population projection assumptions do not reconcile.");
            return request;
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new InvalidDataException("The saved population projection is incomplete or invalid. Return to location selection and save it again.", exception);
        }
    }

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
        if (VisualizationContext is { } context && context.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Visualization context must be a JSON object.");
    }

    private static bool IsDigits(string? value, int length) =>
        value?.Length == length && value.All(character => character is >= '0' and <= '9');
}

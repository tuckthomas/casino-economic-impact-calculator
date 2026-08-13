using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Tests;

public sealed class ValidationGeographicResidualSchemaTests
{
    [Fact]
    public void Model_MapsUniqueImmutableGeographicResidualPatterns()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Database=metadata_only;Username=metadata_only;Password=metadata_only",
                provider => provider.UseNetTopologySuite())
            .Options;
        using var db = new AppDbContext(options);
        var entity = db.Model.FindEntityType(typeof(ValidationGeographicResidualPattern))!;

        Assert.Equal("validation_geographic_residual_patterns", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(ValidationGeographicResidualPattern.ValidationEvaluationId),
                nameof(ValidationGeographicResidualPattern.PredictionKind),
                nameof(ValidationGeographicResidualPattern.DatasetPartition),
                nameof(ValidationGeographicResidualPattern.GeographyKind),
                nameof(ValidationGeographicResidualPattern.GeographyCode)
            ]));
        Assert.Equal(20, entity.FindProperty(nameof(ValidationGeographicResidualPattern.Residual))!.GetPrecision());
    }

    [Fact]
    public void Assembly_ContainsGeographicResidualPatternMigration()
    {
        var resourceName = Assert.Single(
            typeof(ModelFoundationInitializer).Assembly.GetManifestResourceNames(),
            name => name.EndsWith("025_validation_geographic_residual_patterns.sql", StringComparison.Ordinal));
        using var stream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        Assert.Contains("validation_geographic_residual_patterns", sql, StringComparison.Ordinal);
        Assert.Contains("geography_kind IN ('market', 'jurisdiction', 'holdout-group')", sql, StringComparison.Ordinal);
        Assert.Contains("prevent_immutable_validation_evaluation_mutation", sql, StringComparison.Ordinal);
        Assert.Contains("overprediction_count + underprediction_count + exact_prediction_count = observation_count", sql, StringComparison.Ordinal);
    }
}

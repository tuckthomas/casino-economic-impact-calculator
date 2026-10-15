using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public sealed class DemographicCaptureScopeTests
{
    [Fact]
    public void SupportsExplicitInputCaptureAndExistingEffectiveJurisdictionScopes()
    {
        new DemographicCaptureScope([new("market", 41, -85)], 100, EligibleAge: 21).Validate();
        new DemographicCaptureScope([new("market", 41, -85)], 75, "US-IN", "commercial-casino", new(2026, 10, 13)).Validate();
    }

    [Fact]
    public void RequiresAnAgeBasisAndUniqueFiniteLocations()
    {
        Assert.Throws<InvalidDataException>(() => new DemographicCaptureScope([new("market", 41, -85)], 100).Validate());
        Assert.Throws<InvalidDataException>(() => new DemographicCaptureScope([new("market", 41, -85), new("market", 42, -86)], 100, EligibleAge: 21).Validate());
        Assert.Throws<InvalidDataException>(() => new DemographicCaptureScope([new("market", double.NaN, -85)], 100, EligibleAge: 21).Validate());
        Assert.Throws<InvalidDataException>(() => new DemographicCaptureScope([new("market", 41, -85)], 100, EligibleAge: 17).Validate());
    }

    [Fact]
    public void MultipleProviderEnvelopesRespectTheSourceLimitAndContainEveryStudyCircle()
    {
        var scope = new DemographicCaptureScope([new("west", 39.1, -94.6), new("middle", 41.1, -85.1), new("east", 40, -75.1)], 100, EligibleAge: 21);
        var envelopes = scope.ProviderEnvelopes();
        Assert.True(envelopes.Count > 1);
        Assert.All(envelopes, envelope => Assert.InRange(envelope.RadiusMiles, 1, 500));
        Assert.All(scope.Scenarios, location => Assert.Contains(envelopes, envelope =>
            SaveNEIN.Server.Services.Gravity.CompetitiveUniverseService.IsWithinBroadPrefilter(location.Latitude, location.Longitude,
                envelope.Latitude, envelope.Longitude, envelope.RadiusMiles - scope.OriginRadiusMiles)));
    }
}

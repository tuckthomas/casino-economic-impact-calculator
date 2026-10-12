using System.Text.Json;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Validation;

namespace SaveNEIN.Server.Tests;

public class MultimarketAttractionProtocolTests
{
    private static MultimarketCalibrationProtocol Read(string json) =>
        JsonSerializer.Deserialize<MultimarketCalibrationProtocol>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void ExistingProtocolsRetainHybridAttraction()
    {
        var protocol = Read("{}");
        Assert.Equal(FacilityAttractionSpecifications.HybridObservedGgr, protocol.AttractionSpecification);
        MultimarketRevenueCalibration.ValidateAttractionProtocol(protocol);
    }

    [Fact]
    public void StructuralModeAcceptsPositiveDeclaredOffsets()
    {
        MultimarketRevenueCalibration.ValidateAttractionProtocol(Read(
            "{\"attractionSpecification\":\"structural\",\"gamingPositionsOffset\":[100,500,2000]}"));
    }

    [Theory]
    [InlineData("{\"gamingPositionsOffset\":[0]}")]
    [InlineData("{\"gamingPositionsOffset\":[-1]}")]
    [InlineData("{\"gamingPositionsOffset\":[]}")]
    [InlineData("{\"attractionSpecification\":\"observed-ggr\"}")]
    [InlineData("{\"demandSpecification\":\"eligible-adult-per-capita\"}")]
    [InlineData("{\"demandSpecification\":\"unknown\"}")]
    [InlineData("{\"minimumDemandScale\":0}")]
    [InlineData("{\"minimumDemandScale\":1,\"maximumDemandScale\":0.1}")]
    [InlineData("{\"fitDemandScaleByJurisdiction\":true}")]
    [InlineData("{\"selectionPolicyVersion\":\"gravity-development-selection-v1\",\"training\":[\"t1\",\"t2\"],\"selection\":[\"s1\"]}")]
    public void RejectsInvalidSpecificationBeforeAnyDatabaseOrRoutingWork(string json)
    {
        Assert.Throws<InvalidDataException>(() => MultimarketRevenueCalibration.ValidateAttractionProtocol(Read(json)));
    }
}

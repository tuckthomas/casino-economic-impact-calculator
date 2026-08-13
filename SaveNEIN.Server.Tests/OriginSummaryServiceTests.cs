// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Tests;

public sealed class OriginSummaryServiceTests
{
    [Fact]
    public void Summarize_GroupsEverythingBeyondConfiguredTopNIntoExactlyReconciledResidual()
    {
        var origins = new[]
        {
            Row(1, "46701", "IN", "001", 60),
            Row(2, "46702", "IN", "003", 25),
            Row(3, "46703", "IN", "003", 10),
            Row(4, "46704", "OH", "039", 5)
        };

        var result = new OriginSummaryService().Summarize(
            origins,
            Context(new HashSet<long> { 1, 2, 3 }),
            new OriginSummaryOptions(OriginSummaryDimensions.Zcta, TopN: 2, MinimumShare: 0));

        Assert.True(result.ReconcilesToUnderlyingOrigins);
        Assert.Equal(4, result.FullGroupCount);
        Assert.Equal(3, result.DisplayedGroupCount);
        Assert.Equal(100m, result.TotalProposedResidentGgr);
        Assert.Equal("46701", result.Rows[0].Label);
        Assert.Equal("46702", result.Rows[1].Label);
        var residual = Assert.Single(result.Rows, row => row.IsResidual);
        Assert.Equal("Other origins", residual.Label);
        Assert.Equal(2, residual.OriginCount);
        Assert.Equal(15m, residual.TotalProposedResidentGgr);
        Assert.Equal(0.15m, residual.ShareOfProposedResidentGgr);
        Assert.Equal(origins.Sum(origin => origin.ResidentDemand), result.Rows.Sum(row => row.ResidentDemand));
        Assert.Equal(origins.Sum(origin => origin.OutsideOptionCapture), result.Rows.Sum(row => row.OutsideOptionCapture));
    }

    [Fact]
    public void Summarize_HostRegionSeparatesCountyMsaCsaStateOutOfStateAndInternational()
    {
        var origins = new[]
        {
            Row(1, "host", "IN", "001", 60, msa: "23060", csa: "258"),
            Row(2, "msa", "IN", "003", 15, msa: "23060", csa: "258"),
            Row(3, "csa", "IN", "005", 10, msa: "99999", csa: "258"),
            Row(4, "state", "IN", "007", 7),
            Row(5, "domestic", "OH", "039", 5),
            Row(6, "international", null, null, 3, country: "CAN")
        };
        var context = new OriginSummaryContext("USA", "IN", "001", "23060", "258", new HashSet<long> { 1, 2, 3, 4 });

        var result = new OriginSummaryService().Summarize(
            origins,
            context,
            new OriginSummaryOptions(OriginSummaryDimensions.HostRegion, TopN: 100, MinimumShare: 0));

        Assert.Equal(6, result.Rows.Count);
        Assert.Equal(60m, result.Rows.Single(row => row.Key == "host-region:host-county").TotalProposedResidentGgr);
        Assert.Equal(15m, result.Rows.Single(row => row.Key == "host-region:rest-host-msa").TotalProposedResidentGgr);
        Assert.Equal(10m, result.Rows.Single(row => row.Key == "host-region:rest-host-csa").TotalProposedResidentGgr);
        Assert.Equal(7m, result.Rows.Single(row => row.Key == "host-region:rest-host-state").TotalProposedResidentGgr);
        Assert.Equal(5m, result.Rows.Single(row => row.Key == "host-region:out-of-state").TotalProposedResidentGgr);
        Assert.Equal(3m, result.Rows.Single(row => row.Key == "host-region:international").TotalProposedResidentGgr);
    }

    [Fact]
    public void Summarize_UsesPersistedScenarioOriginSetForJurisdictionRelationship()
    {
        var origins = new[]
        {
            Row(1, "one", "IN", "001", 70),
            Row(2, "two", "IN", "003", 20),
            Row(3, "three", "OH", "039", 10)
        };

        var result = new OriginSummaryService().Summarize(
            origins,
            Context(new HashSet<long> { 1, 3 }),
            new OriginSummaryOptions(OriginSummaryDimensions.Jurisdiction, TopN: 100, MinimumShare: 0));

        Assert.Equal(80m, result.Rows.Single(row => row.Key == "jurisdiction:in").TotalProposedResidentGgr);
        Assert.Equal(20m, result.Rows.Single(row => row.Key == "jurisdiction:out").TotalProposedResidentGgr);
        Assert.True(result.ReconcilesToUnderlyingOrigins);
    }

    [Fact]
    public void Summarize_AppliesMinimumShareBeforeTopNAndStillPreservesAllDetailInResidual()
    {
        var origins = new[]
        {
            Row(1, "one", "IN", "001", 90),
            Row(2, "two", "IN", "003", 9),
            Row(3, "three", "OH", "039", 1)
        };

        var result = new OriginSummaryService().Summarize(
            origins,
            Context(new HashSet<long> { 1, 2 }),
            new OriginSummaryOptions(OriginSummaryDimensions.Origin, TopN: 100, MinimumShare: 0.05m));

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(1m, result.Rows.Single(row => row.IsResidual).TotalProposedResidentGgr);
        Assert.Equal(100m, result.Rows.Sum(row => row.TotalProposedResidentGgr));
    }

    private static OriginSummaryContext Context(IReadOnlySet<long> inJurisdiction) =>
        new("USA", "IN", "001", "23060", "258", inJurisdiction);

    private static OriginSummarySourceRow Row(
        long id,
        string geographyCode,
        string? state,
        string? county,
        decimal ggr,
        string? msa = null,
        string? csa = null,
        string country = "USA") =>
        new(
            id,
            $"origin-{geographyCode}",
            "zcta",
            geographyCode,
            country,
            state,
            county,
            msa,
            csa,
            ResidentDemand: ggr * 2,
            InducedResidentDemand: ggr / 10,
            ProposedResidentGgr: ggr * 0.9m,
            ProposedInducedResidentGgr: ggr * 0.1m,
            TotalProposedResidentGgr: ggr,
            HostJurisdictionCapture: ggr * 0.3m,
            ExternalJurisdictionCapture: ggr * 0.2m,
            TribalOrOtherJurisdictionCapture: ggr * 0.1m,
            OutsideOptionCapture: ggr * 0.4m);
}

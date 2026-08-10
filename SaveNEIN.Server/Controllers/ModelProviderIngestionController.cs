// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using Microsoft.AspNetCore.Mvc;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Controllers;

public sealed record LinkedOriginProviderIngestionRequest(
    ProviderFetchRequest Fetch,
    Guid OriginGeographySnapshotId);

public sealed record LinkedGamingProviderIngestionRequest(
    ProviderFetchRequest Fetch,
    Guid CompetitorSnapshotId);

[ApiController]
[Route("api/model-data/providers")]
public sealed class ModelProviderIngestionController(
    IProviderSnapshotIngestionService ingestion,
    CensusZctaOriginProvider censusZctaOrigins,
    CensusAcsAgePopulationProvider censusAge,
    CensusAcsMedianIncomeProvider censusIncome,
    IrsSoiExactCodeZctaIncomeProvider irsSoiIncome,
    IndianaGamingCommissionMonthlyRevenueProvider indianaGamingRevenue,
    IndianaGamingCommissionFacilityInventoryProvider indianaGamingFacilities,
    IllinoisGamingBoardRevenueProvider illinoisGamingRevenue,
    IllinoisGamingBoardFacilityInventoryProvider illinoisGamingFacilities,
    MichiganGamingFacilityInventoryProvider michiganGamingFacilities,
    CompositeGamingRegulatorPerformanceProvider compositeGamingRevenue,
    CompositeGamingFacilityInventoryProvider compositeGamingFacilities,
    IndianaDepartmentOfTransportationAadtProvider indianaTraffic,
    IndianaDestinationDevelopmentPersonTripsProvider indianaTourism) : ControllerBase
{
    [HttpPost("census/zcta-origins")]
    public async Task<IActionResult> IngestCensusZctaOrigins(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestOriginsAsync(
            censusZctaOrigins,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("census-acs/zcta-age-population")]
    public async Task<IActionResult> IngestCensusAgePopulation(
        [FromBody] LinkedOriginProviderIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestAgePopulationAsync(
            censusAge,
            request.Fetch,
            request.OriginGeographySnapshotId,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("census-acs/zcta-median-household-income")]
    public async Task<IActionResult> IngestCensusMedianHouseholdIncome(
        [FromBody] LinkedOriginProviderIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestIncomeAsync(
            censusIncome,
            request.Fetch,
            request.OriginGeographySnapshotId,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("irs-soi/zcta-income-exact-code")]
    public async Task<IActionResult> IngestIrsSoiExactCodeZctaIncome(
        [FromBody] LinkedOriginProviderIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestIncomeAsync(
            irsSoiIncome,
            request.Fetch,
            request.OriginGeographySnapshotId,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("igc/indiana-monthly-performance")]
    public async Task<IActionResult> IngestIndianaMonthlyGamingPerformance(
        [FromBody] LinkedGamingProviderIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingPerformanceAsync(
            indianaGamingRevenue,
            request.Fetch,
            request.CompetitorSnapshotId,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("igc/indiana-facilities")]
    public async Task<IActionResult> IngestIndianaGamingFacilities(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingFacilitiesAsync(
            indianaGamingFacilities,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("igb/illinois-performance")]
    public async Task<IActionResult> IngestIllinoisGamingPerformance(
        [FromBody] LinkedGamingProviderIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingPerformanceAsync(
            illinoisGamingRevenue,
            request.Fetch,
            request.CompetitorSnapshotId,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("igb/illinois-facilities")]
    public async Task<IActionResult> IngestIllinoisGamingFacilities(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingFacilitiesAsync(
            illinoisGamingFacilities,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("mgcb/michigan-facilities")]
    public async Task<IActionResult> IngestMichiganGamingFacilities(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingFacilitiesAsync(
            michiganGamingFacilities,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("gaming/composite-performance")]
    public async Task<IActionResult> IngestCompositeGamingPerformance(
        [FromBody] LinkedGamingProviderIngestionRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingPerformanceAsync(
            compositeGamingRevenue,
            request.Fetch,
            request.CompetitorSnapshotId,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("gaming/composite-facilities")]
    public async Task<IActionResult> IngestCompositeGamingFacilities(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestGamingFacilitiesAsync(
            compositeGamingFacilities,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("indot/indiana-aadt")]
    public async Task<IActionResult> IngestIndianaAadt(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestTrafficAsync(
            indianaTraffic,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }

    [HttpPost("iddc/indiana-statewide-person-trips")]
    public async Task<IActionResult> IngestIndianaTourismPersonTrips(
        [FromBody] ProviderFetchRequest request,
        CancellationToken cancellationToken)
    {
        var snapshotId = await ingestion.IngestTourismAsync(
            indianaTourism,
            request,
            cancellationToken);
        return Created($"/api/model-data/snapshots/{snapshotId:D}", new { SnapshotId = snapshotId });
    }
}

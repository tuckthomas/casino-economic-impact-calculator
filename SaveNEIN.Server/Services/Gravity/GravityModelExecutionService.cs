// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Gravity;

public static class GravityDemandSpecifications
{
    public const string AgiShare = "agi-share";
    public const string EligibleAdultPerCapita = "eligible-adult-per-capita";
}

public static class FacilityAttractionSpecifications
{
    public const string Structural = "structural";
    public const string ObservedGgr = "observed-ggr";
    public const string HybridObservedGgr = "hybrid-observed-ggr";
}

public static class MissingRoutePolicies
{
    public const string RejectOrigin = "reject-origin";
    public const string ExcludeFacility = "exclude-facility";
}

public sealed record TrafficCorridorRunSelection(
    string StableObservationId,
    double RelevantDirectionShare,
    double InterchangeAccessibilityModifier);

public sealed record ImpactGeographyDefinition(
    string ScopeKind,
    string ScopeCode,
    IReadOnlyCollection<string>? LocalOriginIds = null);

public sealed record GravityModelRunRequest(
    string ScenarioName,
    string JurisdictionCode,
    Guid DevelopmentProgramId,
    double CandidateLatitude,
    double CandidateLongitude,
    Guid OriginGeographySnapshotId,
    Guid AgePopulationSnapshotId,
    Guid IncomeSnapshotId,
    Guid CompetitorSnapshotId,
    Guid ObservedPerformanceSnapshotId,
    IReadOnlyCollection<string> StableOriginIds,
    IReadOnlyCollection<int> CompetitorIds,
    int PopulationObservationYear,
    int IncomeTaxYear,
    DateOnly EffectiveOn,
    string FacilityRegime,
    string DemandSpecification,
    string AttractionSpecification,
    string FrictionForm,
    string ObservedMetricKey,
    DateOnly ObservedPeriodStart,
    DateOnly ObservedPeriodEnd,
    long? NationalParameterSetId,
    long? JurisdictionParameterSetId,
    long? ScenarioParameterSetId,
    IReadOnlyCollection<ParameterOverride>? UserOverrides,
    string CostingProfile = "auto",
    double CompetitorPrefilterMiles = 300,
    Guid? TourismSnapshotId = null,
    IReadOnlyCollection<string>? TourismObservationIds = null,
    Guid? TrafficSnapshotId = null,
    IReadOnlyCollection<TrafficCorridorRunSelection>? TrafficCorridors = null,
    ImpactGeographyDefinition? ImpactGeography = null,
    IReadOnlyCollection<int>? ExcludedCompetitorIds = null,
    Guid? LocalEconomicInventorySnapshotId = null,
    string MissingRoutePolicy = MissingRoutePolicies.RejectOrigin);

public sealed record GravityModelRunResult(
    Guid ModelRunId,
    string Status,
    string ComputationalOriginType,
    int OriginCount,
    int IncumbentCount,
    string RoutingGraphHash,
    string CostingProfile,
    decimal TotalResidentDemand,
    decimal InducedResidentDemand,
    decimal ProposedRedistributedResidentGgr,
    decimal ProposedInducedResidentGgr,
    decimal ProposedResidentGgr,
    decimal TourismGgr,
    decimal TrafficGgr,
    decimal StabilizedTotalGgr,
    decimal LocalDiscretionaryDisplacement,
    decimal GrossGamingTax,
    decimal GrossSocialCost,
    double NetPermanentJobs,
    decimal NetHostLocalImpact,
    decimal NetHostStateImpact,
    IReadOnlyDictionary<string, decimal> ProposedCaptureBySource,
    IReadOnlyList<string> Warnings);

public interface IGravityModelExecutionService
{
    Task<GravityModelRunResult> ExecuteAsync(
        GravityModelRunRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GravityModelExecutionService(
    AppDbContext db,
    IModelParameterService parameterService,
    IGamingAgeResolver gamingAgeResolver,
    ICompetitiveUniverseService competitiveUniverseService,
    IOriginDemandService originDemandService,
    IFacilityAttractivenessService attractivenessService,
    ITravelMatrixService travelMatrixService,
    IMarketEquilibriumService equilibriumService,
    IAccessibilityExpansionService accessibilityExpansionService,
    ITourismDemandService tourismDemandService,
    ITrafficInterceptService trafficInterceptService,
    ICapacityDiagnosticService capacityDiagnosticService,
    ICapacityProductivityBenchmarkService capacityProductivityBenchmarkService,
    IRampScheduleService rampScheduleService,
    IGamingTaxCalculator gamingTaxCalculator,
    IGamingFiscalAllocationCalculator gamingFiscalAllocationCalculator,
    IGeneralFiscalRuleResolver generalFiscalRuleResolver,
    IProblemGamblingPrevalenceResolver problemGamblingPrevalenceResolver,
    ICannibalizationAccountingService cannibalizationAccountingService,
    ILocalEconomicInventoryWeightService localEconomicInventoryWeightService,
    IDisplacementModelService displacementModelService,
    IEmploymentImpactService employmentImpactService,
    IEmploymentProductivityBenchmarkService employmentProductivityBenchmarkService,
    IFiscalImpactService fiscalImpactService,
    ISocialCostService socialCostService,
    INetImpactService netImpactService) : IGravityModelExecutionService
{
    private const string ModelVersion = "gravity-v1";

    public async Task<GravityModelRunResult> ExecuteAsync(
        GravityModelRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var stopwatch = Stopwatch.StartNew();
        var context = await LoadContextAsync(request, cancellationToken);
        var modelRun = await CreateDraftRunAsync(request, context, cancellationToken);

        try
        {
            var proposedFacilityKey = $"scenario:{modelRun.Id:D}";
            var travel = await ResolveTravelAsync(
                request,
                context,
                modelRun.Id,
                proposedFacilityKey,
                cancellationToken);
            var result = await ComputePersistAndFinalizeAsync(
                request,
                context,
                modelRun.Id,
                proposedFacilityKey,
                travel,
                stopwatch,
                cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(modelRun.Id, exception, stopwatch.Elapsed, cancellationToken);
            throw;
        }
    }

    private async Task<ExecutionContext> LoadContextAsync(
        GravityModelRunRequest request,
        CancellationToken cancellationToken)
    {
        var jurisdiction = await db.Jurisdictions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Code == request.JurisdictionCode, cancellationToken)
            ?? throw new KeyNotFoundException($"Jurisdiction '{request.JurisdictionCode}' was not found.");
        var developmentProgram = await db.DevelopmentPrograms
            .SingleOrDefaultAsync(item => item.Id == request.DevelopmentProgramId, cancellationToken)
            ?? throw new KeyNotFoundException($"Development program '{request.DevelopmentProgramId}' was not found.");

        var requiredSnapshots = new Dictionary<Guid, (string Label, string DatasetKind)>
        {
            [request.OriginGeographySnapshotId] = ("origin geography", DatasetSnapshotKinds.OriginGeography),
            [request.AgePopulationSnapshotId] = ("age population", DatasetSnapshotKinds.AgePopulation),
            [request.IncomeSnapshotId] = ("income/AGI", DatasetSnapshotKinds.Income),
            [request.CompetitorSnapshotId] = ("competitor", DatasetSnapshotKinds.Competitors),
            [request.ObservedPerformanceSnapshotId] = ("observed performance", DatasetSnapshotKinds.ObservedPerformance)
        };
        if (request.TourismSnapshotId is { } tourismSnapshotId)
        {
            requiredSnapshots.Add(tourismSnapshotId, ("tourism", DatasetSnapshotKinds.Tourism));
        }
        if (request.TrafficSnapshotId is { } trafficSnapshotId)
        {
            requiredSnapshots.Add(trafficSnapshotId, ("traffic", DatasetSnapshotKinds.Traffic));
        }
        if (request.LocalEconomicInventorySnapshotId is { } localEconomicSnapshotId)
        {
            requiredSnapshots.Add(
                localEconomicSnapshotId,
                ("local economic inventory", DatasetSnapshotKinds.LocalEconomicInventory));
        }
        var snapshots = await db.DatasetSnapshots
            .AsNoTracking()
            .Where(snapshot => requiredSnapshots.Keys.Contains(snapshot.Id))
            .ToListAsync(cancellationToken);
        var missingSnapshots = requiredSnapshots.Keys.Except(snapshots.Select(snapshot => snapshot.Id)).ToArray();
        if (missingSnapshots.Length > 0)
        {
            throw new KeyNotFoundException($"Dataset snapshot(s) not found: {string.Join(", ", missingSnapshots)}.");
        }
        var invalidSnapshots = snapshots
            .Where(snapshot => !snapshot.IsSealed ||
                               snapshot.ValidationState is DatasetValidationStates.Pending or DatasetValidationStates.Rejected ||
                               snapshot.DatasetKey != requiredSnapshots[snapshot.Id].DatasetKind)
            .Select(snapshot =>
                $"{requiredSnapshots[snapshot.Id].Label}={snapshot.Id} " +
                $"(kind={snapshot.DatasetKey}, expected={requiredSnapshots[snapshot.Id].DatasetKind}, " +
                $"state={snapshot.ValidationState}, sealed={snapshot.IsSealed})")
            .ToArray();
        if (invalidSnapshots.Length > 0)
        {
            throw new InvalidOperationException(
                $"Gravity execution requires validated or warning-state snapshots: {string.Join(", ", invalidSnapshots)}.");
        }

        var stableOriginIds = request.StableOriginIds.Distinct(StringComparer.Ordinal).ToArray();
        var origins = await db.OriginZones
            .AsNoTracking()
            .Where(origin => origin.DatasetSnapshotId == request.OriginGeographySnapshotId &&
                             stableOriginIds.Contains(origin.StableOriginId))
            .OrderBy(origin => origin.StableOriginId)
            .ToListAsync(cancellationToken);
        if (origins.Count != stableOriginIds.Length)
        {
            var found = origins.Select(origin => origin.StableOriginId).ToHashSet(StringComparer.Ordinal);
            throw new KeyNotFoundException(
                $"Origin snapshot is missing requested origin(s): {string.Join(", ", stableOriginIds.Where(id => !found.Contains(id)))}.");
        }
        var originTypes = origins.Select(origin => origin.OriginType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (originTypes.Length != 1)
        {
            throw new InvalidOperationException(
                "One model run must use one explicit computational origin resolution; split mixed origin types into separate runs.");
        }

        var selectedCompetitors = await competitiveUniverseService.SelectAsync(
            request.CompetitorSnapshotId,
            origins,
            request.EffectiveOn,
            request.CompetitorPrefilterMiles,
            request.CompetitorIds,
            cancellationToken);
        var excludedCompetitorIds = (request.ExcludedCompetitorIds ?? []).Distinct().ToHashSet();
        var competitors = selectedCompetitors
            .Where(competitor => !excludedCompetitorIds.Contains(competitor.Id))
            .ToArray();
        if (competitors.Length == 0)
        {
            throw new InvalidOperationException("The held-out exclusions removed every facility from the competitive universe.");
        }

        var tourismObservationIds = (request.TourismObservationIds ?? [])
            .Select(id => id.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (request.TourismSnapshotId is null && tourismObservationIds.Length > 0 ||
            request.TourismSnapshotId is not null && tourismObservationIds.Length == 0)
        {
            throw new ArgumentException(
                "Tourism snapshot and at least one explicit tourism observation ID must be provided together.",
                nameof(request));
        }
        var tourismObservations = request.TourismSnapshotId is not { } selectedTourismSnapshotId
            ? []
            : await db.TourismMarketObservations
                .AsNoTracking()
                .Where(observation => observation.DatasetSnapshotId == selectedTourismSnapshotId &&
                                      tourismObservationIds.Contains(observation.StableObservationId))
                .OrderBy(observation => observation.StableObservationId)
                .ToListAsync(cancellationToken);
        if (tourismObservations.Count != tourismObservationIds.Length)
        {
            var found = tourismObservations.Select(item => item.StableObservationId).ToHashSet(StringComparer.Ordinal);
            throw new KeyNotFoundException(
                $"Tourism snapshot is missing requested observation(s): {string.Join(", ", tourismObservationIds.Where(id => !found.Contains(id)))}.");
        }

        var trafficSelections = (request.TrafficCorridors ?? []).ToArray();
        var duplicateTrafficSelection = trafficSelections
            .GroupBy(selection => selection.StableObservationId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateTrafficSelection is not null)
        {
            throw new ArgumentException(
                $"Traffic observation '{duplicateTrafficSelection.Key}' was selected more than once.",
                nameof(request));
        }
        if (request.TrafficSnapshotId is null && trafficSelections.Length > 0 ||
            request.TrafficSnapshotId is not null && trafficSelections.Length == 0)
        {
            throw new ArgumentException(
                "Traffic snapshot and at least one explicit corridor selection must be provided together.",
                nameof(request));
        }
        var trafficIds = trafficSelections.Select(selection => selection.StableObservationId).ToArray();
        var trafficObservations = request.TrafficSnapshotId is not { } selectedTrafficSnapshotId
            ? []
            : await db.TrafficCorridorObservations
                .AsNoTracking()
                .Where(observation => observation.DatasetSnapshotId == selectedTrafficSnapshotId &&
                                      trafficIds.Contains(observation.StableObservationId))
                .OrderBy(observation => observation.StableObservationId)
                .ToListAsync(cancellationToken);
        if (trafficObservations.Count != trafficIds.Length)
        {
            var found = trafficObservations.Select(item => item.StableObservationId).ToHashSet(StringComparer.Ordinal);
            throw new KeyNotFoundException(
                $"Traffic snapshot is missing requested observation(s): {string.Join(", ", trafficIds.Where(id => !found.Contains(id)))}.");
        }

        var localEconomicObservations = request.LocalEconomicInventorySnapshotId is not { } selectedLocalEconomicSnapshotId
            ? []
            : await db.LocalEconomicSectorObservations
                .AsNoTracking()
                .Where(observation => observation.DatasetSnapshotId == selectedLocalEconomicSnapshotId)
                .OrderBy(observation => observation.GeographyType)
                .ThenBy(observation => observation.GeographyCode)
                .ThenBy(observation => observation.SectorKey)
                .ThenBy(observation => observation.StableObservationId)
                .ToListAsync(cancellationToken);
        if (request.LocalEconomicInventorySnapshotId is not null && localEconomicObservations.Count == 0)
        {
            throw new InvalidOperationException("The selected local-economic inventory snapshot contains no observations.");
        }

        return new ExecutionContext(
            jurisdiction,
            developmentProgram,
            origins,
            competitors,
            tourismObservations,
            trafficObservations,
            localEconomicObservations,
            trafficSelections.ToDictionary(selection => selection.StableObservationId, StringComparer.Ordinal),
            originTypes.Single());
    }

    private async Task<ModelRun> CreateDraftRunAsync(
        GravityModelRunRequest request,
        ExecutionContext context,
        CancellationToken cancellationToken)
    {
        var resolvedInput = JsonSerializer.Serialize(new
        {
            request.ScenarioName,
            computationalOriginType = context.ComputationalOriginType,
            request.StableOriginIds,
            request.CompetitorIds,
            request.PopulationObservationYear,
            request.IncomeTaxYear,
            request.EffectiveOn,
            request.FacilityRegime,
            request.DemandSpecification,
            request.AttractionSpecification,
            request.FrictionForm,
            request.ObservedMetricKey,
            request.ObservedPeriodStart,
            request.ObservedPeriodEnd,
            request.CostingProfile,
            request.CompetitorPrefilterMiles,
            request.TourismSnapshotId,
            request.TourismObservationIds,
            request.TrafficSnapshotId,
            request.TrafficCorridors,
            request.LocalEconomicInventorySnapshotId,
            selectedCompetitorIds = context.Competitors.Select(competitor => competitor.Id)
        });
        var run = new ModelRun
        {
            ModelVersion = ModelVersion,
            Status = ModelRunStatuses.Draft,
            JurisdictionId = context.Jurisdiction.Id,
            DevelopmentProgramId = context.DevelopmentProgram.Id,
            CandidateLatitude = request.CandidateLatitude,
            CandidateLongitude = request.CandidateLongitude,
            ResolvedInputJson = resolvedInput,
            DataSnapshotReferencesJson = "{}"
        };
        db.ModelRuns.Add(run);
        var datasetReferences = new List<ModelRunDatasetSnapshotReference>
        {
            DatasetReference(run.Id, request.OriginGeographySnapshotId, DatasetSnapshotRoles.OriginDemographics, "geography"),
            DatasetReference(run.Id, request.AgePopulationSnapshotId, DatasetSnapshotRoles.OriginDemographics, "age-population"),
            DatasetReference(run.Id, request.IncomeSnapshotId, DatasetSnapshotRoles.IncomeAgi, "income"),
            DatasetReference(run.Id, request.CompetitorSnapshotId, DatasetSnapshotRoles.Competitors, "competitive-universe"),
            DatasetReference(run.Id, request.ObservedPerformanceSnapshotId, DatasetSnapshotRoles.ObservedPerformance, "gaming-revenue")
        };
        if (request.TourismSnapshotId is { } tourismSnapshotId)
        {
            datasetReferences.Add(DatasetReference(
                run.Id,
                tourismSnapshotId,
                DatasetSnapshotRoles.Tourism,
                "visitor-person-trips"));
        }
        if (request.TrafficSnapshotId is { } trafficSnapshotId)
        {
            datasetReferences.Add(DatasetReference(
                run.Id,
                trafficSnapshotId,
                DatasetSnapshotRoles.Traffic,
                "corridor-counts"));
        }
        if (request.LocalEconomicInventorySnapshotId is { } localEconomicSnapshotId)
        {
            datasetReferences.Add(DatasetReference(
                run.Id,
                localEconomicSnapshotId,
                DatasetSnapshotRoles.LocalEconomicInventory,
                "displacement-sector-inventory"));
        }
        db.ModelRunDatasetSnapshotReferences.AddRange(datasetReferences);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    private async Task<TravelMatrixResolution> ResolveTravelAsync(
        GravityModelRunRequest request,
        ExecutionContext context,
        Guid modelRunId,
        string proposedFacilityKey,
        CancellationToken cancellationToken)
    {
        var origins = context.Origins.Select(origin => new TravelMatrixOrigin(
            origin.Id,
            origin.StableOriginId,
            origin.RepresentativePoint.Y,
            origin.RepresentativePoint.X)).ToArray();
        var facilities = context.Competitors.Select(competitor => new TravelMatrixFacility(
                competitor.StableVenueId,
                FacilityKinds.Incumbent,
                competitor.Id,
                null,
                competitor.Latitude,
                competitor.Longitude))
            .Append(new TravelMatrixFacility(
                proposedFacilityKey,
                FacilityKinds.Scenario,
                null,
                modelRunId,
                request.CandidateLatitude,
                request.CandidateLongitude))
            .ToArray();
        return await travelMatrixService.ResolveAsync(
            origins,
            facilities,
            request.CostingProfile,
            cancellationToken);
    }

    private async Task<GravityModelRunResult> ComputePersistAndFinalizeAsync(
        GravityModelRunRequest request,
        ExecutionContext context,
        Guid modelRunId,
        string proposedFacilityKey,
        TravelMatrixResolution travel,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var run = await db.ModelRuns.SingleAsync(item => item.Id == modelRunId, cancellationToken);
        if (run.Status != ModelRunStatuses.Draft)
        {
            throw new InvalidOperationException($"Model run '{modelRunId}' is not draft.");
        }

        var parameterRequest = new ParameterResolutionRequest(
            ModelVersion,
            request.NationalParameterSetId,
            request.JurisdictionParameterSetId,
            request.ScenarioParameterSetId,
            request.UserOverrides);
        var selectedSetLayers = SelectedParameterSets(parameterRequest);
        var selectedSetIds = selectedSetLayers.Select(item => item.Id).Distinct().ToArray();
        var selectedSets = selectedSetIds.Length == 0
            ? []
            : await db.ModelParameterSets
                .Where(set => selectedSetIds.Contains(set.Id))
                .ToListAsync(cancellationToken);
        if (selectedSets.Count != selectedSetIds.Length)
        {
            throw new InvalidOperationException("One or more selected parameter sets were not found.");
        }
        foreach (var set in selectedSets)
        {
            set.IsImmutable = true;
        }

        var resolved = await parameterService.ResolveAsync(parameterRequest, cancellationToken);
        var parameters = resolved.ToDictionary(item => item.Definition.Key, item => item.FinalValue, StringComparer.Ordinal);
        var origins = await BuildOriginDemandAsync(request, context, parameters, cancellationToken);
        var attractionResolution = await BuildAttractionsAsync(
            request,
            context,
            proposedFacilityKey,
            parameters,
            cancellationToken);
        var attractions = attractionResolution.Attractions;
        var routeByKey = travel.Routes.ToDictionary(
            route => (route.OriginZoneId, route.FacilityKey),
            new RouteKeyComparer());
        var competitorByKey = context.Competitors.ToDictionary(
            competitor => competitor.StableVenueId,
            StringComparer.OrdinalIgnoreCase);
        var equilibriumOrigins = origins.Select(origin =>
        {
            var incumbents = context.Competitors.Select(competitor =>
                Alternative(
                    competitor.StableVenueId,
                    attractions[competitor.StableVenueId],
                    routeByKey[(origin.Origin.Id, competitor.StableVenueId)],
                    CaptureCategory(competitor, context.Jurisdiction.Id),
                    false)).ToArray();
            return new EquilibriumOriginInput(
                origin.Origin.StableOriginId,
                origin.Demand.Demand,
                RequireParameter(parameters, "gravity.outside_option_weight"),
                incumbents,
                Alternative(
                    proposedFacilityKey,
                    attractions[proposedFacilityKey],
                    routeByKey[(origin.Origin.Id, proposedFacilityKey)],
                    CaptureSourceCategories.ExternalCommercialIncumbent,
                    true));
        }).ToArray();
        var gravityParameters = new GravityParameters(
            RequireParameter(parameters, "gravity.alpha"),
            ParseFrictionForm(request.FrictionForm),
            ParseFrictionForm(request.FrictionForm) == TravelFrictionForm.InversePower
                ? RequireParameter(parameters, "gravity.beta")
                : RequireParameter(parameters, "gravity.exponential_lambda"),
            RequireParameter(parameters, "gravity.regularization_minutes"),
            ParseMissingRouteBehavior(request.MissingRoutePolicy));
        var equilibrium = equilibriumService.Calculate(new MarketEquilibriumRequest(
            equilibriumOrigins,
            gravityParameters));
        var expansions = BuildAccessibilityExpansions(equilibrium, parameters);
        var nonresidentDemand = BuildAndPersistNonresidentDemand(run.Id, request, context, parameters);

        PersistResults(
            run,
            request,
            context,
            origins,
            attractions,
            routeByKey,
            competitorByKey,
            equilibrium,
            expansions,
            nonresidentDemand,
            proposedFacilityKey);
        var capacityAndRamp = await PersistCapacityAndRampAsync(
            run.Id,
            request,
            context,
            context.DevelopmentProgram,
            proposedFacilityKey,
            equilibrium.ProposedFacilityDemand +
            expansions.Values.Sum(item => item.ProposedInducedResidentGgr) +
            nonresidentDemand.TourismGgr +
            nonresidentDemand.TrafficGgr,
            parameters,
            cancellationToken);
        var impact = await BuildAndPersistImpactAsync(
            run.Id,
            request,
            context,
            origins,
            equilibrium,
            expansions,
            nonresidentDemand,
            parameters,
            resolved,
            cancellationToken);
        PersistParameterSnapshot(run.Id, resolved, selectedSetLayers);
        context.DevelopmentProgram.IsImmutable = true;
        await db.SaveChangesAsync(cancellationToken);

        stopwatch.Stop();
        run.Status = ModelRunStatuses.Finalized;
        run.FinalizedAtUtc = DateTime.UtcNow;
        run.ExecutionDuration = stopwatch.Elapsed;
        run.BaseParameterSetId = request.JurisdictionParameterSetId ?? request.NationalParameterSetId;
        run.ResolvedInputJson = AddExecutionManifest(
            run.ResolvedInputJson,
            travel,
            attractionResolution,
            parameters,
            resolved,
            equilibrium,
            expansions,
            nonresidentDemand,
            capacityAndRamp,
            impact);
        run.DataSnapshotReferencesJson = JsonSerializer.Serialize(new
        {
            originGeography = request.OriginGeographySnapshotId,
            agePopulation = request.AgePopulationSnapshotId,
            income = request.IncomeSnapshotId,
            competitors = request.CompetitorSnapshotId,
            observedPerformance = request.ObservedPerformanceSnapshotId,
            tourism = request.TourismSnapshotId,
            traffic = request.TrafficSnapshotId,
            localEconomicInventory = request.LocalEconomicInventorySnapshotId
        });
        var warnings = resolved
            .Where(item => item.WarningText is not null)
            .Select(item => item.WarningText!)
            .Concat(attractionResolution.Warnings)
            .Concat(expansions.Values.SelectMany(item => item.Expansion.Warnings))
            .Concat(nonresidentDemand.Warnings)
            .Concat(capacityAndRamp.Warnings)
            .Concat(impact.Warnings)
            .Concat(travel.Routes.Any(route => !route.RouteFound)
                ? [$"Travel matrix excluded {travel.Routes.Count(route => !route.RouteFound)} unreachable " +
                   $"origin-facility pair(s) under policy '{request.MissingRoutePolicy}'; " +
                   "no straight-line travel value was substituted."]
                : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        run.WarningSummary = string.Join(" ", warnings);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var inducedResidentDemand = expansions.Values.Sum(item => item.Expansion.InducedResidentDemand);
        var proposedInducedResidentGgr = expansions.Values.Sum(item => item.ProposedInducedResidentGgr);
        var totalProposedResidentGgr = equilibrium.ProposedFacilityDemand + proposedInducedResidentGgr;
        var proposedResidentGgrResult = ToMoney(totalProposedResidentGgr);
        var tourismGgrResult = ToMoney(nonresidentDemand.TourismGgr);
        var trafficGgrResult = ToMoney(nonresidentDemand.TrafficGgr);
        var stabilizedTotalGgrResult = proposedResidentGgrResult + tourismGgrResult + trafficGgrResult;
        var proposedCaptureBySource = equilibrium.ProposedCaptureBySource.ToDictionary(
            pair => pair.Key,
            pair => ToMoney(pair.Value),
            StringComparer.OrdinalIgnoreCase);
        proposedCaptureBySource[CaptureSourceCategories.InducedResident] = ToMoney(proposedInducedResidentGgr);
        proposedCaptureBySource[CaptureSourceCategories.Tourism] = ToMoney(nonresidentDemand.TourismGgr);
        proposedCaptureBySource[CaptureSourceCategories.TrafficIntercept] = ToMoney(nonresidentDemand.TrafficGgr);
        return new GravityModelRunResult(
            run.Id,
            run.Status,
            context.ComputationalOriginType,
            context.Origins.Count,
            context.Competitors.Count,
            travel.RoutingGraphHash,
            travel.CostingProfile,
            ToMoney(equilibrium.TotalDemand),
            ToMoney(inducedResidentDemand),
            ToMoney(equilibrium.ProposedFacilityDemand),
            ToMoney(proposedInducedResidentGgr),
            proposedResidentGgrResult,
            tourismGgrResult,
            trafficGgrResult,
            stabilizedTotalGgrResult,
            ToMoney(impact.Displacement.TotalDisplacedSales),
            ToMoney(impact.Fiscal.GrossGamingTax),
            ToMoney(impact.SocialCost.AnnualCost),
            impact.Employment.NetPermanentJobs,
            ToMoney(impact.NetImpact.NetHostLocalImpact),
            ToMoney(impact.NetImpact.NetHostStateImpact),
            proposedCaptureBySource,
            warnings);
    }

    private async Task<IReadOnlyList<OriginDemandContext>> BuildOriginDemandAsync(
        GravityModelRunRequest request,
        ExecutionContext context,
        IReadOnlyDictionary<string, double> parameters,
        CancellationToken cancellationToken)
    {
        var originIds = context.Origins.Select(origin => origin.Id).ToArray();
        var incomes = await db.OriginZoneIncomePeriods
            .AsNoTracking()
            .Where(period => originIds.Contains(period.OriginZoneId) &&
                             period.DatasetSnapshotId == request.IncomeSnapshotId &&
                             period.TaxYear == request.IncomeTaxYear)
            .ToListAsync(cancellationToken);
        if (incomes.Count != originIds.Length)
        {
            var found = incomes.Select(period => period.OriginZoneId).ToHashSet();
            throw new InvalidOperationException(
                $"Income snapshot is missing the requested tax year for origin IDs: {string.Join(", ", originIds.Where(id => !found.Contains(id)))}.");
        }
        var incomeByOrigin = incomes.ToDictionary(period => period.OriginZoneId);
        var legalGamingAge = await gamingAgeResolver.ResolveMinimumAgeAsync(
            request.JurisdictionCode,
            request.FacilityRegime,
            request.EffectiveOn,
            cancellationToken);
        var ageBins = await db.OriginZoneAgeBins
            .AsNoTracking()
            .Where(bin => originIds.Contains(bin.OriginZoneId) &&
                          bin.DatasetSnapshotId == request.AgePopulationSnapshotId &&
                          bin.ObservationYear == request.PopulationObservationYear)
            .OrderBy(bin => bin.MinimumAge)
            .ToListAsync(cancellationToken);
        var binsByOrigin = ageBins.GroupBy(bin => bin.OriginZoneId).ToDictionary(group => group.Key, group => group.ToArray());
        var eligiblePopulationByOrigin = context.Origins.ToDictionary(
            origin => origin.Id,
            origin =>
            {
                if (!binsByOrigin.TryGetValue(origin.Id, out var bins))
                {
                    throw new InvalidOperationException(
                        $"Age-population snapshot is missing origin '{origin.StableOriginId}'.");
                }
                var observedEligiblePopulation = EligiblePopulationCalculator.Calculate(
                    bins.Select(bin => new AgeBinValue(bin.MinimumAge, bin.MaximumAge, bin.Population)).ToArray(),
                    legalGamingAge).Population;
                return PopulationProjectionCalculator.Calculate(new PopulationProjectionInput(
                    observedEligiblePopulation,
                    request.PopulationObservationYear,
                    request.EffectiveOn.Year,
                    RequireParameter(parameters, "demographics.population_annual_growth_rate"))).ProjectedPopulation;
            });

        if (request.DemandSpecification == GravityDemandSpecifications.AgiShare)
        {
            return context.Origins.Select(origin =>
            {
                var income = incomeByOrigin[origin.Id];
                var realIncomeMass = income.InflationAdjustedAdjustedGrossIncome ?? income.AdjustedGrossIncome
                    ?? throw new InvalidOperationException(
                        $"Origin '{origin.StableOriginId}' has no AGI value for AGI-share demand.");
                var demand = originDemandService.CalculateAgiShare(new AgiShareDemandInput(
                    origin.StableOriginId,
                    Convert.ToDouble(realIncomeMass),
                    RequireParameter(parameters, "demand.gaming_income_share"),
                    RequireParameter(parameters, "demand.regional_intensity_multiplier")));
                return new OriginDemandContext(origin, demand, eligiblePopulationByOrigin[origin.Id]);
            }).ToArray();
        }

        var incomeMetricByOrigin = context.Origins.ToDictionary(
            origin => origin.Id,
            origin => ResolveIncomeMetric(incomeByOrigin[origin.Id], origin.StableOriginId));
        var referenceIncome = Median(incomeMetricByOrigin.Values);
        return context.Origins.Select(origin =>
        {
            var eligiblePopulation = eligiblePopulationByOrigin[origin.Id];
            var demand = originDemandService.CalculatePerCapita(new PerCapitaDemandInput(
                origin.StableOriginId,
                eligiblePopulation,
                RequireParameter(parameters, "demand.base_ggr_per_eligible_adult") *
                RequireParameter(parameters, "demand.regional_intensity_multiplier"),
                incomeMetricByOrigin[origin.Id],
                referenceIncome,
                RequireParameter(parameters, "demand.income_elasticity"),
                RequireParameter(parameters, "demand.income_adjustment_minimum"),
                RequireParameter(parameters, "demand.income_adjustment_maximum")));
            return new OriginDemandContext(origin, demand, eligiblePopulation);
        }).ToArray();
    }

    private async Task<AttractionResolutionContext> BuildAttractionsAsync(
        GravityModelRunRequest request,
        ExecutionContext context,
        string proposedFacilityKey,
        IReadOnlyDictionary<string, double> parameters,
        CancellationToken cancellationToken)
    {
        var structural = context.Competitors.ToDictionary(
            competitor => competitor.StableVenueId,
            competitor => StructuralAttraction(competitor.StableVenueId, FacilityFeatures(competitor, parameters)),
            StringComparer.OrdinalIgnoreCase);
        var proposedStructural = StructuralAttraction(
            proposedFacilityKey,
            FacilityFeatures(context.DevelopmentProgram, parameters)) *
            RequireParameter(parameters, "facility.proposed_scale_multiplier");

        if (request.AttractionSpecification == FacilityAttractionSpecifications.Structural)
        {
            structural.Add(proposedFacilityKey, proposedStructural);
            return new AttractionResolutionContext(
                structural,
                [],
                context.Competitors.Select(competitor => competitor.StableVenueId).Order(StringComparer.Ordinal).ToArray(),
                []);
        }

        var competitorIds = context.Competitors.Select(competitor => competitor.Id).ToArray();
        var periods = await db.CasinoGamingRevenuePeriods
            .AsNoTracking()
            .Where(period => competitorIds.Contains(period.CasinoCompetitorId) &&
                             period.DatasetSnapshotId == request.ObservedPerformanceSnapshotId &&
                             period.ReportedMetricKey == request.ObservedMetricKey &&
                             period.PeriodStart >= request.ObservedPeriodStart &&
                             period.PeriodEnd <= request.ObservedPeriodEnd)
            .ToListAsync(cancellationToken);
        var observedCompetitorIds = periods.Select(period => period.CasinoCompetitorId).ToHashSet();
        var observedCompetitors = context.Competitors
            .Where(competitor => observedCompetitorIds.Contains(competitor.Id))
            .ToArray();
        if (request.AttractionSpecification == FacilityAttractionSpecifications.ObservedGgr)
        {
            ValidateObservedPeriods(periods, context.Competitors, request);
        }
        else
        {
            if (observedCompetitors.Length == 0)
            {
                throw new InvalidOperationException(
                    "Hybrid observed-GGR attraction requires at least one incumbent with the selected observed metric and period.");
            }
            ValidateObservedPeriods(periods, observedCompetitors, request);
        }
        var observedByCompetitor = periods
            .GroupBy(period => period.CasinoCompetitorId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(period => Convert.ToDouble(period.InflationAdjustedAmount ?? period.ReportedAmount)));
        var referenceObservedGgr = Median(observedByCompetitor.Values);
        var attractions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var observedFacilityIds = new List<string>();
        var structuralFallbackFacilityIds = new List<string>();
        foreach (var competitor in context.Competitors)
        {
            if (observedByCompetitor.TryGetValue(competitor.Id, out var observedGgr))
            {
                attractions.Add(
                    competitor.StableVenueId,
                    attractivenessService.CalculateObservedGgr(new ObservedGgrAttractivenessInput(
                        competitor.StableVenueId,
                        observedGgr,
                        referenceObservedGgr,
                        false)).NormalizedAttraction);
                observedFacilityIds.Add(competitor.StableVenueId);
            }
            else
            {
                attractions.Add(competitor.StableVenueId, structural[competitor.StableVenueId]);
                structuralFallbackFacilityIds.Add(competitor.StableVenueId);
            }
        }
        attractions.Add(
            proposedFacilityKey,
            proposedStructural * RequireParameter(parameters, "facility.comparable_scale_multiplier"));
        var warnings = structuralFallbackFacilityIds.Count == 0
            ? Array.Empty<string>()
            : [$"Hybrid attraction used structural mass for {structuralFallbackFacilityIds.Count} incumbent facility/facilities without the selected observed metric: {string.Join(", ", structuralFallbackFacilityIds.Order(StringComparer.Ordinal))}."];
        return new AttractionResolutionContext(
            attractions,
            observedFacilityIds.Order(StringComparer.Ordinal).ToArray(),
            structuralFallbackFacilityIds.Order(StringComparer.Ordinal).ToArray(),
            warnings);
    }

    private double StructuralAttraction(string facilityKey, IReadOnlyCollection<FacilityFeatureTerm> features) =>
        attractivenessService.CalculateStructural(new StructuralAttractivenessInput(
            facilityKey,
            features,
            MissingFacilityAttributeBehavior.UseReferenceValue)).NormalizedAttraction;

    private static IReadOnlyCollection<FacilityFeatureTerm> FacilityFeatures(
        CasinoCompetitor competitor,
        IReadOnlyDictionary<string, double> parameters) =>
    [
        Feature("gaming-positions", competitor.GamingPositions ?? competitor.SlotOrVltPositions, "facility.reference_gaming_positions", "facility.gaming_positions_coefficient", parameters),
        Feature("table-games", competitor.TableGameCount, "facility.reference_table_games", "facility.table_games_coefficient", parameters),
        Feature("hotel-rooms", competitor.HotelRoomCount, "facility.reference_hotel_rooms", "facility.hotel_rooms_coefficient", parameters),
        Feature("gaming-floor-square-feet", competitor.GamingFloorSquareFeet, "facility.reference_gaming_floor_square_feet", "facility.gaming_floor_coefficient", parameters),
        Feature("food-beverage-venues", competitor.FoodBeverageVenueCount, "facility.reference_food_beverage_venues", "facility.food_beverage_coefficient", parameters),
        Feature("entertainment-capacity", competitor.EventCapacity, "facility.reference_entertainment_capacity", "facility.entertainment_capacity_coefficient", parameters),
        Feature("capital-cost", competitor.DevelopmentCost is null ? null : Convert.ToDouble(competitor.DevelopmentCost), "facility.reference_capital_cost", "facility.capital_scale_coefficient", parameters),
        Feature("highway-access", competitor.HasInterchangeAccess is null ? null : competitor.HasInterchangeAccess.Value ? 1 : 0, "facility.reference_highway_access", "facility.highway_access_coefficient", parameters)
    ];

    private static IReadOnlyCollection<FacilityFeatureTerm> FacilityFeatures(
        DevelopmentProgram program,
        IReadOnlyDictionary<string, double> parameters) =>
    [
        Feature("gaming-positions", program.SlotOrVltPositions + program.TableGameCount, "facility.reference_gaming_positions", "facility.gaming_positions_coefficient", parameters),
        Feature("table-games", program.TableGameCount, "facility.reference_table_games", "facility.table_games_coefficient", parameters),
        Feature("hotel-rooms", program.HotelRoomCount, "facility.reference_hotel_rooms", "facility.hotel_rooms_coefficient", parameters),
        Feature("gaming-floor-square-feet", program.GamingFloorSquareFeet, "facility.reference_gaming_floor_square_feet", "facility.gaming_floor_coefficient", parameters),
        Feature("food-beverage-venues", program.FoodBeverageVenueCount, "facility.reference_food_beverage_venues", "facility.food_beverage_coefficient", parameters),
        Feature("entertainment-capacity", program.EventCapacity, "facility.reference_entertainment_capacity", "facility.entertainment_capacity_coefficient", parameters),
        Feature("capital-cost", program.CapitalCost is null ? null : Convert.ToDouble(program.CapitalCost), "facility.reference_capital_cost", "facility.capital_scale_coefficient", parameters),
        Feature("highway-access", null, "facility.reference_highway_access", "facility.highway_access_coefficient", parameters)
    ];

    private static FacilityFeatureTerm Feature(
        string key,
        double? value,
        string referenceKey,
        string coefficientKey,
        IReadOnlyDictionary<string, double> parameters) =>
        new(key, value, RequireParameter(parameters, referenceKey), RequireParameter(parameters, coefficientKey));

    private static GravityAlternativeInput Alternative(
        string facilityKey,
        double attraction,
        OriginFacilityTravel route,
        string captureSourceCategory,
        bool isProposed) =>
        new(
            facilityKey,
            attraction,
            route.TravelTimeMinutes,
            route.RouteFound,
            1,
            captureSourceCategory,
            isProposed);

    private IReadOnlyDictionary<string, OriginExpansionContext> BuildAccessibilityExpansions(
        MarketEquilibriumResult equilibrium,
        IReadOnlyDictionary<string, double> parameters)
    {
        var elasticity = RequireParameter(parameters, "market_expansion.accessibility_elasticity");
        var maximumShare = RequireParameter(parameters, "market_expansion.maximum_induced_demand_share");
        return equilibrium.Origins.ToDictionary(
            origin => origin.OriginKey,
            origin =>
            {
                var baselineLogAccessibility = LogInclusiveAccessibility(origin.Baseline);
                var withProjectLogAccessibility = LogInclusiveAccessibility(origin.WithProject);
                var expansion = accessibilityExpansionService.Calculate(new AccessibilityExpansionInput(
                    origin.OriginKey,
                    origin.Baseline.Demand,
                    baselineLogAccessibility,
                    withProjectLogAccessibility,
                    elasticity,
                    maximumShare));
                var proposedShare = origin.WithProject.FacilityAllocations
                    .Single(allocation => allocation.IsProposedFacility)
                    .Share;
                return new OriginExpansionContext(
                    baselineLogAccessibility,
                    withProjectLogAccessibility,
                    expansion,
                    expansion.InducedResidentDemand * proposedShare);
            },
            StringComparer.Ordinal);
    }

    internal static double LogInclusiveAccessibility(GravityOriginResult result)
    {
        var logWeights = result.FacilityAllocations
            .Where(allocation => allocation.RouteIncluded && allocation.LogWeight.HasValue)
            .Select(allocation => allocation.LogWeight!.Value)
            .Concat(result.OutsideOptionLogWeight is { } outsideOptionLogWeight
                ? [outsideOptionLogWeight]
                : [])
            .Where(double.IsFinite)
            .ToArray();
        if (logWeights.Length == 0)
        {
            throw new InvalidOperationException(
                $"Origin '{result.OriginKey}' has neither a routed facility weight nor a positive outside-option weight for accessibility expansion.");
        }
        var maximum = logWeights.Max();
        return maximum + Math.Log(logWeights.Sum(value => Math.Exp(value - maximum)));
    }

    private NonresidentDemandContext BuildAndPersistNonresidentDemand(
        Guid modelRunId,
        GravityModelRunRequest request,
        ExecutionContext context,
        IReadOnlyDictionary<string, double> parameters)
    {
        var warnings = new List<string>();
        double tourismGgr = 0;
        foreach (var observation in context.TourismObservations)
        {
            var result = tourismDemandService.Calculate(new TourismDemandInput(
                observation.StableObservationId,
                Convert.ToDouble(observation.NormalizedVisitorPersonTrips),
                RequireParameter(parameters, "tourism.resident_origin_overlap_share"),
                RequireParameter(parameters, "tourism.eligible_visitor_share"),
                RequireParameter(parameters, "tourism.participation_rate"),
                RequireParameter(parameters, "tourism.capture_rate"),
                RequireParameter(parameters, "tourism.ggr_per_captured_participant")));
            tourismGgr += result.TourismGgr;
            db.ModelRunDemandComponents.Add(new ModelRunDemandComponent
            {
                ModelRunId = modelRunId,
                DatasetSnapshotId = request.TourismSnapshotId,
                ComponentType = ModelDemandComponentTypes.Tourism,
                SourceRecordKey = observation.StableObservationId,
                MethodKey = "visitor-person-trips-v1",
                InputQuantity = ToQuantity(result.VisitorPersonTrips),
                DeduplicatedQuantity = ToQuantity(result.DeduplicatedVisitorPersonTrips),
                EligibleQuantity = ToQuantity(result.EligibleVisitorTrips),
                ParticipatingQuantity = ToQuantity(result.GamingParticipantTrips),
                CapturedQuantity = ToQuantity(result.CapturedParticipantTrips),
                Ggr = ToMoney(result.TourismGgr),
                DetailsJson = JsonSerializer.Serialize(new
                {
                    observation.SourceMetricKind,
                    observation.SourceQuantity,
                    observation.NormalizedVisitorPersonTrips,
                    observation.NormalizationMethod,
                    observation.MarketKey,
                    observation.GeographyType,
                    observation.GeographyCode,
                    observation.PeriodStart,
                    observation.PeriodEnd
                })
            });
        }
        if (context.TourismObservations.Count == 0)
        {
            warnings.Add("Tourism GGR is zero because this run references no tourism observation snapshot.");
        }
        else if (tourismGgr == 0)
        {
            warnings.Add("Tourism observations were selected, but zero-safe participation/capture/spend parameters produced zero tourism GGR.");
        }

        double trafficGgr = 0;
        foreach (var observation in context.TrafficObservations)
        {
            var selection = context.TrafficSelections[observation.StableObservationId];
            var result = trafficInterceptService.Calculate(new TrafficInterceptInput(
                observation.StableObservationId,
                observation.AnnualAverageDailyTraffic,
                observation.ObservationDays,
                RequireParameter(parameters, "traffic.eligible_passengers_per_vehicle"),
                selection.RelevantDirectionShare,
                selection.InterchangeAccessibilityModifier,
                RequireParameter(parameters, "traffic.intercept_rate"),
                RequireParameter(parameters, "traffic.resident_origin_overlap_share"),
                RequireParameter(parameters, "traffic.overlap_deduplication_share"),
                RequireParameter(parameters, "traffic.ggr_per_intercepted_traveler")));
            trafficGgr += result.TrafficGgr;
            db.ModelRunDemandComponents.Add(new ModelRunDemandComponent
            {
                ModelRunId = modelRunId,
                DatasetSnapshotId = request.TrafficSnapshotId,
                ComponentType = ModelDemandComponentTypes.Traffic,
                SourceRecordKey = observation.StableObservationId,
                MethodKey = "aadt-intercept-v1",
                InputQuantity = ToQuantity(result.AnnualVehicleTrips),
                DeduplicatedQuantity = ToQuantity(result.DeduplicatedInterceptedTravelerTrips),
                EligibleQuantity = ToQuantity(result.DirectionallyRelevantEligibleTravelerTrips),
                ParticipatingQuantity = ToQuantity(result.AccessibleTravelerTrips),
                CapturedQuantity = ToQuantity(result.DeduplicatedInterceptedTravelerTrips),
                Ggr = ToMoney(result.TrafficGgr),
                DetailsJson = JsonSerializer.Serialize(new
                {
                    observation.RouteDesignation,
                    observation.JurisdictionCode,
                    observation.PeriodStart,
                    observation.PeriodEnd,
                    observation.AnnualAverageDailyTraffic,
                    observation.ObservationDays,
                    selection.RelevantDirectionShare,
                    selection.InterchangeAccessibilityModifier,
                    result.InterceptedTravelerTripsBeforeDeduplication
                })
            });
        }
        if (context.TrafficObservations.Count == 0)
        {
            warnings.Add("Traffic-intercept GGR is zero because this run references no traffic observation snapshot.");
        }
        else if (trafficGgr == 0)
        {
            warnings.Add("Traffic observations were selected, but zero-safe intercept/spend parameters produced zero traffic GGR.");
        }
        return new NonresidentDemandContext(tourismGgr, trafficGgr, warnings);
    }

    private async Task<CapacityAndRampContext> PersistCapacityAndRampAsync(
        Guid modelRunId,
        GravityModelRunRequest request,
        ExecutionContext context,
        DevelopmentProgram program,
        string proposedFacilityKey,
        double stabilizedTotalGgr,
        IReadOnlyDictionary<string, double> parameters,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var competitorIds = context.Competitors.Select(competitor => competitor.Id).ToArray();
        var componentPeriods = await db.CasinoGamingRevenuePeriods
            .AsNoTracking()
            .Where(period => competitorIds.Contains(period.CasinoCompetitorId) &&
                             period.DatasetSnapshotId == request.ObservedPerformanceSnapshotId &&
                             (period.ReportedMetricKey == GamingRevenueMetricKeys.SlotOrVltGamingRevenue ||
                              period.ReportedMetricKey == GamingRevenueMetricKeys.TableGameGamingRevenue) &&
                             period.PeriodStart >= request.ObservedPeriodStart &&
                             period.PeriodEnd <= request.ObservedPeriodEnd)
            .ToListAsync(cancellationToken);
        var benchmarkResolution = capacityProductivityBenchmarkService.Resolve(
            new CapacityProductivityBenchmarkInput(
                request.ObservedPerformanceSnapshotId,
                request.ObservedPeriodStart,
                request.ObservedPeriodEnd,
                context.Competitors,
                componentPeriods));
        var benchmark = benchmarkResolution.Benchmark;
        var capacityEnabled = benchmark is not null ||
                              RequireParameter(parameters, "capacity.diagnostic_enabled") >= 0.5;
        if (!capacityEnabled)
        {
            const string warning = "Capacity diagnostic was not evaluated because no validated productivity benchmark set is active.";
            db.ModelRunCapacityDiagnostics.Add(new ModelRunCapacityDiagnostic
            {
                ModelRunId = modelRunId,
                FacilityKey = proposedFacilityKey,
                Status = CapacityDiagnosticStatuses.NotEvaluated,
                StabilizedGgr = ToMoney(stabilizedTotalGgr),
                WarningText = warning
            });
            warnings.Add(warning);
        }
        else
        {
            var slotMinimum = benchmark?.SlotWinPerUnitDayMinimum ??
                              RequireParameter(parameters, "capacity.slot_win_per_unit_day_minimum");
            var slotMaximum = benchmark?.SlotWinPerUnitDayMaximum ??
                              RequireParameter(parameters, "capacity.slot_win_per_unit_day_maximum");
            var tableMinimum = benchmark?.TableWinPerTableDayMinimum ??
                               RequireParameter(parameters, "capacity.table_win_per_table_day_minimum");
            var tableMaximum = benchmark?.TableWinPerTableDayMaximum ??
                               RequireParameter(parameters, "capacity.table_win_per_table_day_maximum");
            var capacity = capacityDiagnosticService.Evaluate(new CapacityDiagnosticInput(
                stabilizedTotalGgr,
                program.SlotOrVltPositions,
                program.TableGameCount,
                IntegerParameter(parameters, "capacity.operating_days_per_year", 1, 366),
                slotMinimum,
                slotMaximum,
                tableMinimum,
                tableMaximum,
                program.HotelRoomCount,
                program.EventCapacity));
            var status = capacity.IsAboveValidatedRange
                ? CapacityDiagnosticStatuses.AboveRange
                : capacity.IsBelowValidatedRange
                    ? CapacityDiagnosticStatuses.BelowRange
                    : CapacityDiagnosticStatuses.WithinRange;
            db.ModelRunCapacityDiagnostics.Add(new ModelRunCapacityDiagnostic
            {
                ModelRunId = modelRunId,
                FacilityKey = proposedFacilityKey,
                Status = status,
                StabilizedGgr = ToMoney(capacity.StabilizedGgr),
                PlausibleCapacityMinimum = ToMoney(capacity.PlausibleCapacityMinimum),
                PlausibleCapacityMaximum = ToMoney(capacity.PlausibleCapacityMaximum),
                ImpliedResidualSlotWinPerUnitDay = capacity.ImpliedResidualSlotWinPerUnitDay,
                BenchmarkDatasetSnapshotId = benchmark?.ObservedPerformanceSnapshotId,
                BenchmarkMethod = benchmark?.Method ?? "versioned-parameter-range",
                BenchmarkSampleSize = benchmark?.Facilities.Count,
                SlotWinPerUnitDayMinimum = slotMinimum,
                SlotWinPerUnitDayMaximum = slotMaximum,
                TableWinPerTableDayMinimum = tableMinimum,
                TableWinPerTableDayMaximum = tableMaximum,
                BenchmarkProvenanceJson = benchmark is null
                    ? JsonSerializer.Serialize(new
                    {
                        source = "resolved-model-parameters",
                        keys = new[]
                        {
                            "capacity.slot_win_per_unit_day_minimum",
                            "capacity.slot_win_per_unit_day_maximum",
                            "capacity.table_win_per_table_day_minimum",
                            "capacity.table_win_per_table_day_maximum"
                        }
                    })
                    : JsonSerializer.Serialize(new
                    {
                        source = "observed-performance-and-competitor-snapshots",
                        benchmark.ObservedPerformanceSnapshotId,
                        benchmark.Method,
                        benchmark.PeriodStart,
                        benchmark.PeriodEnd,
                        Facilities = benchmark.Facilities
                    }),
                IsBelowValidatedRange = capacity.IsBelowValidatedRange,
                IsAboveValidatedRange = capacity.IsAboveValidatedRange,
                WarningText = string.Join(" ", benchmarkResolution.Warnings.Concat(capacity.Warnings))
            });
            warnings.AddRange(benchmarkResolution.Warnings);
            warnings.AddRange(capacity.Warnings);
        }

        var rampYears = 0;
        if (program.PlannedOpeningDate is not { } openingDate)
        {
            warnings.Add("Ramp schedule was not generated because the development program has no planned opening date.");
        }
        else
        {
            var schedule = rampScheduleService.Build(new RampScheduleInput(
                stabilizedTotalGgr,
                openingDate.Year,
                openingDate.Month,
                RequireParameter(parameters, "ramp.first_year_share"),
                RequireParameter(parameters, "ramp.second_year_share"),
                program.StabilizedYearNumber,
                RequireParameter(parameters, "ramp.stabilized_annual_growth_rate"),
                IntegerParameter(parameters, "ramp.projection_years", program.StabilizedYearNumber, 50)));
            db.ModelRunRampResults.AddRange(schedule.Select(year => new ModelRunRampResult
            {
                ModelRunId = modelRunId,
                FacilityKey = proposedFacilityKey,
                CalendarYear = year.CalendarYear,
                OperatingYearNumber = year.OperatingYearNumber,
                PeriodKind = year.PeriodKind,
                OperatingYearFraction = year.OperatingYearFraction,
                StabilizationShare = year.StabilizationShare,
                ProjectedGgr = ToMoney(year.ProjectedGgr)
            }));
            rampYears = schedule.Count;
        }
        return new CapacityAndRampContext(capacityEnabled, rampYears, warnings);
    }

    private async Task<ComprehensiveImpactContext> BuildAndPersistImpactAsync(
        Guid modelRunId,
        GravityModelRunRequest request,
        ExecutionContext context,
        IReadOnlyCollection<OriginDemandContext> originDemand,
        MarketEquilibriumResult equilibrium,
        IReadOnlyDictionary<string, OriginExpansionContext> expansions,
        NonresidentDemandContext nonresidentDemand,
        IReadOnlyDictionary<string, double> parameters,
        IReadOnlyCollection<ResolvedModelParameter> resolvedParameters,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var geography = ResolveImpactGeography(request, context.Origins);
        var localOriginIds = geography.LocalOriginIds.ToHashSet(StringComparer.Ordinal);
        var localEquilibrium = equilibrium.Origins
            .Where(origin => localOriginIds.Contains(origin.OriginKey))
            .ToArray();
        if (localEquilibrium.Length == 0)
        {
            throw new InvalidOperationException(
                $"Impact geography '{geography.ScopeKind}:{geography.ScopeCode}' does not include any selected demand origin.");
        }

        var accounting = cannibalizationAccountingService.Calculate(new CannibalizationAccountingInput(
            equilibrium.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.HostJurisdictionIncumbent),
            equilibrium.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.ExternalCommercialIncumbent),
            equilibrium.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.TribalOrOtherJurisdictionIncumbent),
            equilibrium.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.OutsideOption),
            expansions.Values.Sum(item => item.ProposedInducedResidentGgr),
            nonresidentDemand.TourismGgr,
            nonresidentDemand.TrafficGgr));

        var localResidentGamingBase = localEquilibrium.Sum(origin =>
            origin.ProposedFacilityDemand + expansions[origin.OriginKey].ProposedInducedResidentGgr);
        var localCasinoCannibalization = localEquilibrium.Sum(origin =>
            origin.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.HostJurisdictionIncumbent));
        var localRepatriatedOrLeaked = localEquilibrium.Sum(origin =>
            origin.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.ExternalCommercialIncumbent) +
            origin.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.TribalOrOtherJurisdictionIncumbent) +
            origin.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.OutsideOption));

        GeneralFiscalRuleResult? generalFiscalRule = null;
        try
        {
            generalFiscalRule = await generalFiscalRuleResolver.ResolveAsync(
                request.JurisdictionCode,
                request.FacilityRegime,
                request.EffectiveOn,
                cancellationToken);
        }
        catch (UnsupportedJurisdictionException exception)
        {
            warnings.Add(exception.Message + " Non-gaming fiscal effects are reported as zero rather than applying another jurisdiction's defaults.");
        }
        var salesTaxRate = Convert.ToDouble(generalFiscalRule?.SalesTaxRate ?? 0);
        var businessIncomeTaxRate = Convert.ToDouble(generalFiscalRule?.BusinessIncomeTaxRate ?? 0);
        var taxableShare = RequireParameter(parameters, "displacement.taxable_share");
        var businessMargin = RequireParameter(parameters, "displacement.business_margin");
        var displacementPriors = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DisplacementSectorKeys.RestaurantHospitality] =
                RequireParameter(parameters, "displacement.restaurant_hospitality_weight"),
            [DisplacementSectorKeys.Retail] = RequireParameter(parameters, "displacement.retail_weight"),
            [DisplacementSectorKeys.ArtsEntertainmentRecreation] =
                RequireParameter(parameters, "displacement.arts_entertainment_recreation_weight")
        };
        var configuredInventoryModifiers = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DisplacementSectorKeys.RestaurantHospitality] =
                RequireParameter(parameters, "displacement.restaurant_hospitality_inventory_modifier"),
            [DisplacementSectorKeys.Retail] =
                RequireParameter(parameters, "displacement.retail_inventory_modifier"),
            [DisplacementSectorKeys.ArtsEntertainmentRecreation] =
                RequireParameter(parameters, "displacement.arts_entertainment_recreation_inventory_modifier")
        };
        var inventoryResolution = localEconomicInventoryWeightService.Resolve(
            context.LocalEconomicObservations,
            geography.ScopeKind,
            geography.ScopeCode,
            displacementPriors,
            configuredInventoryModifiers,
            request.LocalEconomicInventorySnapshotId is not null);
        var laborAssumptions = localEconomicInventoryWeightService.ResolveLaborAssumptions(
            context.LocalEconomicObservations,
            geography.ScopeKind,
            geography.ScopeCode,
            RequireParameter(parameters, "employment.direct_average_annual_wage"),
            RequireParameter(parameters, "employment.indirect_average_annual_wage"),
            RequireParameter(parameters, "employment.incumbent_average_annual_wage"),
            request.LocalEconomicInventorySnapshotId is not null);
        warnings.AddRange(laborAssumptions.Warnings);
        var displacement = displacementModelService.Calculate(new DisplacementInput(
            localResidentGamingBase,
            localCasinoCannibalization,
            localRepatriatedOrLeaked,
            RequireParameter(parameters, "displacement.eligible_base_share"),
            RequireParameter(parameters, "displacement.coefficient"),
            [
                new DisplacementSectorInput(
                    DisplacementSectorKeys.RestaurantHospitality,
                    displacementPriors[DisplacementSectorKeys.RestaurantHospitality],
                    inventoryResolution.Modifiers[DisplacementSectorKeys.RestaurantHospitality],
                    taxableShare,
                    businessMargin,
                    salesTaxRate,
                    businessIncomeTaxRate,
                    RequireParameter(parameters, "displacement.restaurant_hospitality_annual_sales_per_job")),
                new DisplacementSectorInput(
                    DisplacementSectorKeys.Retail,
                    displacementPriors[DisplacementSectorKeys.Retail],
                    inventoryResolution.Modifiers[DisplacementSectorKeys.Retail],
                    taxableShare,
                    businessMargin,
                    salesTaxRate,
                    businessIncomeTaxRate,
                    RequireParameter(parameters, "displacement.retail_annual_sales_per_job")),
                new DisplacementSectorInput(
                    DisplacementSectorKeys.ArtsEntertainmentRecreation,
                    displacementPriors[DisplacementSectorKeys.ArtsEntertainmentRecreation],
                    inventoryResolution.Modifiers[DisplacementSectorKeys.ArtsEntertainmentRecreation],
                    taxableShare,
                    businessMargin,
                    salesTaxRate,
                    businessIncomeTaxRate,
                    RequireParameter(parameters, "displacement.arts_entertainment_recreation_annual_sales_per_job"))
            ]));
        if (displacement.DisplacementEligibleBase == 0)
        {
            warnings.Add("Local discretionary displacement is zero because no economically eligible local-resident base was active after exclusions and parameter resolution.");
        }

        var employmentBenchmarkCompetitors = await db.CasinoCompetitors
            .AsNoTracking()
            .Where(competitor => competitor.DatasetSnapshotId == request.CompetitorSnapshotId &&
                                 competitor.ReportedEmployment > 0)
            .OrderBy(competitor => competitor.StableVenueId)
            .ToListAsync(cancellationToken);
        var employmentBenchmarkCompetitorIds = employmentBenchmarkCompetitors
            .Select(competitor => competitor.Id)
            .ToArray();
        var employmentBenchmarkPeriods = await db.CasinoGamingRevenuePeriods
            .AsNoTracking()
            .Where(period => employmentBenchmarkCompetitorIds.Contains(period.CasinoCompetitorId) &&
                             period.DatasetSnapshotId == request.ObservedPerformanceSnapshotId &&
                             period.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue &&
                             period.PeriodStart >= request.ObservedPeriodStart &&
                             period.PeriodEnd <= request.ObservedPeriodEnd)
            .OrderBy(period => period.CasinoCompetitorId)
            .ThenBy(period => period.PeriodStart)
            .ToListAsync(cancellationToken);
        var employmentBenchmarkResolution = employmentProductivityBenchmarkService.Resolve(
            new EmploymentProductivityBenchmarkInput(
                request.ObservedPerformanceSnapshotId,
                request.ObservedPeriodStart,
                request.ObservedPeriodEnd,
                employmentBenchmarkCompetitors,
                employmentBenchmarkPeriods));
        warnings.AddRange(employmentBenchmarkResolution.Warnings);
        var configuredDirectJobsPerMillion = RequireParameter(parameters, "employment.direct_jobs_per_million_ggr");
        var configuredIncumbentJobsPerMillion = RequireParameter(parameters, "employment.incumbent_jobs_per_million_lost_ggr");
        var directJobsPerMillion = configuredDirectJobsPerMillion > 0
            ? configuredDirectJobsPerMillion
            : employmentBenchmarkResolution.Benchmark?.WeightedJobsPerMillionGgr ?? 0;
        var incumbentJobsPerMillion = configuredIncumbentJobsPerMillion > 0
            ? configuredIncumbentJobsPerMillion
            : employmentBenchmarkResolution.Benchmark?.WeightedJobsPerMillionGgr ?? 0;
        if (employmentBenchmarkResolution.Benchmark is not null &&
            (configuredDirectJobsPerMillion > 0 || configuredIncumbentJobsPerMillion > 0))
        {
            warnings.Add(
                "One or more explicit versioned employment job-density parameters superseded the available regulator-observed weighted benchmark; both the applied values and benchmark remain disclosed.");
        }

        var employment = employmentImpactService.Calculate(new EmploymentImpactInput(
            accounting.StabilizedGgr,
            Convert.ToDouble(context.DevelopmentProgram.CapitalCost ?? 0),
            accounting.HostJurisdictionCannibalization,
            directJobsPerMillion,
            RequireParameter(parameters, "employment.construction_job_years_per_million_capital_cost"),
            RequireParameter(parameters, "employment.indirect_induced_jobs_per_direct_job"),
            incumbentJobsPerMillion,
            laborAssumptions.DirectAverageAnnualWage,
            laborAssumptions.IndirectAverageAnnualWage,
            laborAssumptions.IncumbentAverageAnnualWage,
            displacement.Sectors));
        if (employment.DirectCasinoJobs == 0)
        {
            warnings.Add("Employment outputs are zero-safe because no validated direct-job assumption was active.");
        }

        var gamingTax = await gamingTaxCalculator.CalculateAsync(new GamingTaxRequest(
            request.JurisdictionCode,
            request.FacilityRegime,
            request.EffectiveOn,
            0,
            ToMoney(accounting.StabilizedGgr),
            PriorFiscalYearTaxableGamingRevenue: 0), cancellationToken);
        var gamingFiscalAllocation = await gamingFiscalAllocationCalculator.CalculateAsync(
            new GamingFiscalAllocationRequest(
            request.JurisdictionCode,
            request.FacilityRegime,
            request.EffectiveOn,
            ToMoney(accounting.StabilizedGgr),
            gamingTax.GamingTax,
            request.CandidateLatitude,
            request.CandidateLongitude),
            cancellationToken);
        var incumbentTaxLosses = await CalculateIncumbentGamingTaxLossesAsync(
            request,
            context,
            equilibrium,
            cancellationToken);
        warnings.AddRange(incumbentTaxLosses.Warnings);

        var nonGamingTaxableRevenue = accounting.StabilizedGgr *
                                      Convert.ToDouble(generalFiscalRule?.NonGamingTaxableRevenueShareOfGgr ?? 0);
        var directAndIndirectLaborIncome = employment.DirectLaborIncome + employment.IndirectLaborIncome;
        var fiscal = fiscalImpactService.Calculate(new FiscalImpactInput(
            Convert.ToDouble(gamingFiscalAllocation.BaseGamingTax),
            Convert.ToDouble(gamingFiscalAllocation.SupplementalGamingTax),
            Convert.ToDouble(gamingFiscalAllocation.HostMunicipalityShare),
            Convert.ToDouble(gamingFiscalAllocation.HostCountyShare),
            Convert.ToDouble(gamingFiscalAllocation.HostRegionalShare),
            Convert.ToDouble(gamingFiscalAllocation.HostStateShare),
            nonGamingTaxableRevenue * salesTaxRate,
            directAndIndirectLaborIncome * Convert.ToDouble(generalFiscalRule?.PayrollIncomeTaxRate ?? 0),
            nonGamingTaxableRevenue * RequireParameter(parameters, "fiscal.non_gaming_business_margin") * businessIncomeTaxRate,
            Convert.ToDouble(generalFiscalRule?.AnnualPropertyTax ?? 0),
            displacement.Sectors.Sum(sector => sector.SalesTaxLoss),
            displacement.Sectors.Sum(sector => sector.BusinessIncomeTaxLoss),
            incumbentTaxLosses.HostJurisdictionTaxLoss,
            incumbentTaxLosses.OtherJurisdictionTaxLoss));

        var localEligiblePopulation = originDemand
            .Where(origin => localOriginIds.Contains(origin.Origin.StableOriginId))
            .Sum(origin => origin.EligiblePopulation);
        var prevalenceParameter = resolvedParameters.Single(parameter =>
            parameter.Definition.Key == "social_cost.prevalence");
        var jurisdictionPrevalence = await problemGamblingPrevalenceResolver.ResolveAsync(
            request.JurisdictionCode,
            request.EffectiveOn,
            cancellationToken);
        var prevalenceSelection = SocialCostPrevalenceSelector.Select(
            prevalenceParameter,
            jurisdictionPrevalence);
        if (prevalenceSelection.JurisdictionAssumption is not null)
        {
            warnings.Add(
                $"Problem-gambling prevalence uses the validated effective jurisdiction rule ({prevalenceSelection.AppliedPrevalence:P1}); this observed prevalence does not replace the separately resolved causal exposure-response parameter.");
        }
        var socialScale = RequireParameter(parameters, "social_cost.crime_public_safety_productivity_scale");
        var socialCost = socialCostService.Calculate(new SocialCostInput(
            localEligiblePopulation,
            prevalenceSelection.AppliedPrevalence,
            RequireParameter(parameters, "social_cost.exposure_response"),
            RequireParameter(parameters, "social_cost.low_case_multiplier"),
            RequireParameter(parameters, "social_cost.high_case_multiplier"),
            [
                new SocialCostDomainInput("treatment-health", RequireParameter(parameters, "social_cost.treatment_health_per_case")),
                new SocialCostDomainInput("bankruptcy-debt-stress", RequireParameter(parameters, "social_cost.bankruptcy_debt_per_case")),
                new SocialCostDomainInput("crime-public-safety", RequireParameter(parameters, "social_cost.crime_public_safety_per_case"), socialScale),
                new SocialCostDomainInput("productivity-employment", RequireParameter(parameters, "social_cost.productivity_employment_per_case"), socialScale),
                new SocialCostDomainInput("family-household", RequireParameter(parameters, "social_cost.family_household_per_case")),
                new SocialCostDomainInput("public-assistance-administration", RequireParameter(parameters, "social_cost.public_assistance_administration_per_case"))
            ]));
        if (socialCost.AnnualCost == 0)
        {
            warnings.Add("Social-cost outputs are zero-safe because prevalence, exposure-response, or nonoverlapping per-case domain costs were not active.");
        }

        var netImpact = netImpactService.Calculate(new NetImpactInput(
            accounting.StabilizedGgr,
            accounting.TransferEffectGgr,
            accounting.CrossJurisdictionCapture,
            accounting.OutsideOrUnmodeledLeakageCapture,
            accounting.InducedResidentGgr,
            accounting.TourismGgr + accounting.TrafficGgr,
            displacement.TotalDisplacedSales,
            directAndIndirectLaborIncome,
            fiscal.NetHostLocalFiscalImpact,
            fiscal.NetHostStateFiscalImpact,
            socialCost.AnnualCost));

        db.ModelRunGeographicAccounting.Add(new ModelRunGeographicAccounting
        {
            ModelRunId = modelRunId,
            ScopeKind = geography.ScopeKind,
            ScopeCode = geography.ScopeCode,
            LocalOriginCount = localEquilibrium.Length,
            HostJurisdictionCannibalization = ToMoney(accounting.HostJurisdictionCannibalization),
            CrossJurisdictionCapture = ToMoney(accounting.CrossJurisdictionCapture),
            OutsideOrUnmodeledLeakageCapture = ToMoney(accounting.OutsideOrUnmodeledLeakageCapture),
            InducedResidentGgr = ToMoney(accounting.InducedResidentGgr),
            TourismGgr = ToMoney(accounting.TourismGgr),
            TrafficGgr = ToMoney(accounting.TrafficGgr),
            TransferEffectGgr = ToMoney(accounting.TransferEffectGgr),
            MarketExpansionAndImportGgr = ToMoney(accounting.MarketExpansionAndImportGgr),
            StabilizedGgr = ToMoney(accounting.StabilizedGgr),
            LocalResidentGamingBase = ToMoney(displacement.LocalResidentGamingBase),
            ExcludedLocalCasinoCannibalization = ToMoney(displacement.ExcludedLocalCasinoCannibalization),
            ExcludedRepatriatedOrLeakedResidentGgr = ToMoney(displacement.ExcludedRepatriatedOrLeakedResidentGgr),
            RemainingLocalResidentGamingBase = ToMoney(displacement.RemainingLocalResidentGamingBase),
            LocalOriginIdsJson = JsonSerializer.Serialize(geography.LocalOriginIds.Order(StringComparer.Ordinal))
        });
        db.ModelRunSectorDisplacement.AddRange(displacement.Sectors.Select(sector => new ModelRunSectorDisplacement
        {
            ModelRunId = modelRunId,
            ScopeKind = geography.ScopeKind,
            ScopeCode = geography.ScopeCode,
            SectorKey = sector.SectorKey,
            NormalizedWeight = sector.NormalizedWeight,
            DisplacementEligibleBase = ToMoney(displacement.DisplacementEligibleBase),
            DisplacementCoefficient = displacement.DisplacementCoefficient,
            DisplacedSales = ToMoney(sector.DisplacedSales),
            DisplacedTaxableSales = ToMoney(sector.DisplacedTaxableSales),
            DisplacedBusinessIncome = ToMoney(sector.DisplacedBusinessIncome),
            SalesTaxLoss = ToMoney(sector.SalesTaxLoss),
            BusinessIncomeTaxLoss = ToMoney(sector.BusinessIncomeTaxLoss),
            DisplacedJobs = sector.DisplacedJobs
        }));
        db.ModelRunEmploymentImpacts.Add(new ModelRunEmploymentImpact
        {
            ModelRunId = modelRunId,
            ScopeKind = geography.ScopeKind,
            ScopeCode = geography.ScopeCode,
            DirectCasinoJobs = employment.DirectCasinoJobs,
            ConstructionJobYears = employment.ConstructionJobYears,
            IndirectAndInducedJobs = employment.IndirectAndInducedJobs,
            DisplacedSectorJobs = employment.DisplacedSectorJobs,
            IncumbentCasinoJobsLost = employment.IncumbentCasinoJobsLost,
            NetPermanentJobs = employment.NetPermanentJobs,
            DirectLaborIncome = ToMoney(employment.DirectLaborIncome),
            IndirectLaborIncome = ToMoney(employment.IndirectLaborIncome),
            IncumbentLaborIncomeLost = ToMoney(employment.IncumbentLaborIncomeLost),
            DirectAverageAnnualWage = ToMoney(laborAssumptions.DirectAverageAnnualWage),
            IndirectAverageAnnualWage = ToMoney(laborAssumptions.IndirectAverageAnnualWage),
            IncumbentAverageAnnualWage = ToMoney(laborAssumptions.IncumbentAverageAnnualWage),
            AssumptionProvenanceJson = JsonSerializer.Serialize(new
            {
                laborAssumptions.AssumptionBasis,
                inventoryResolution.WeightBasis,
                localEconomicInventorySnapshotId = request.LocalEconomicInventorySnapshotId,
                directJobsPerMillionGgr = directJobsPerMillion,
                incumbentJobsPerMillionLostGgr = incumbentJobsPerMillion,
                directJobsDensitySource = configuredDirectJobsPerMillion > 0 ? "versioned-parameter" :
                    employmentBenchmarkResolution.Benchmark?.Method ?? "zero-safe-fallback",
                incumbentJobsDensitySource = configuredIncumbentJobsPerMillion > 0 ? "versioned-parameter" :
                    employmentBenchmarkResolution.Benchmark?.Method ?? "zero-safe-fallback",
                employmentBenchmark = employmentBenchmarkResolution.Benchmark
            })
        });
        db.ModelRunFiscalImpacts.Add(new ModelRunFiscalImpact
        {
            ModelRunId = modelRunId,
            ScopeKind = geography.ScopeKind,
            ScopeCode = geography.ScopeCode,
            BaseGamingTax = ToMoney(fiscal.BaseGamingTax),
            SupplementalGamingTax = ToMoney(fiscal.SupplementalGamingTax),
            GrossGamingTax = ToMoney(fiscal.GrossGamingTax),
            HostMunicipalityGamingTaxShare = ToMoney(fiscal.HostMunicipalityGamingTaxShare),
            HostCountyGamingTaxShare = ToMoney(fiscal.HostCountyGamingTaxShare),
            HostRegionalGamingTaxShare = ToMoney(fiscal.HostRegionalGamingTaxShare),
            HostStateGamingTaxShare = ToMoney(fiscal.HostStateGamingTaxShare),
            HostLocalGrossPublicRevenue = ToMoney(fiscal.HostLocalGrossPublicRevenue),
            HostStateGrossPublicRevenue = ToMoney(fiscal.HostStateGrossPublicRevenue),
            DisplacedLocalFiscalLoss = ToMoney(fiscal.DisplacedLocalFiscalLoss),
            HostIncumbentGamingTaxLoss = ToMoney(fiscal.HostIncumbentGamingTaxLoss),
            OtherJurisdictionGamingTaxLoss = ToMoney(fiscal.OtherJurisdictionGamingTaxLoss),
            NetHostLocalFiscalImpact = ToMoney(fiscal.NetHostLocalFiscalImpact),
            NetHostStateFiscalImpact = ToMoney(fiscal.NetHostStateFiscalImpact),
            OtherJurisdictionFiscalImpact = ToMoney(fiscal.OtherJurisdictionFiscalImpact),
            RuleProvenanceJson = JsonSerializer.Serialize(new
            {
                gamingTax.RevenueDefinition,
                gamingTax.SourceUrl,
                gamingFiscalAllocation.SourceUrls,
                gamingFiscalAllocation.Location.StateFips,
                gamingFiscalAllocation.Location.CountyFips,
                gamingFiscalAllocation.Location.CountyName,
                gamingFiscalAllocation.Location.MunicipalityGeoid,
                gamingFiscalAllocation.Location.MunicipalityName,
                generalFiscalRuleSource = generalFiscalRule?.SourceUrl,
                effectiveOn = request.EffectiveOn
            })
        });
        db.ModelRunSocialCosts.AddRange(socialCost.Domains.Select(domain => new ModelRunSocialCost
        {
            ModelRunId = modelRunId,
            ScopeKind = geography.ScopeKind,
            ScopeCode = geography.ScopeCode,
            DomainKey = domain.DomainKey,
            ExposedEligiblePopulation = socialCost.ExposedEligiblePopulation,
            IncrementalCases = domain.IncrementalCases,
            PerCaseCost = ToMoney(domain.PerCaseCost),
            AnnualCost = ToMoney(domain.AnnualCost),
            LowAnnualCost = ToMoney(domain.LowAnnualCost),
            HighAnnualCost = ToMoney(domain.HighAnnualCost),
            Included = true,
            ProvenanceNotes = JsonSerializer.Serialize(new
            {
                prevalenceSelection.AppliedPrevalence,
                prevalenceSelection.SourceKey,
                prevalenceSelection.JurisdictionAssumption,
                exposureResponse = RequireParameter(parameters, "social_cost.exposure_response"),
                populationObservationYear = request.PopulationObservationYear,
                scenarioYear = request.EffectiveOn.Year,
                note = "Per-case domain values and scaling remain in the immutable model-run parameter snapshot; zero values mean the domain was not activated."
            })
        }));
        db.ModelRunNetImpacts.Add(new ModelRunNetImpact
        {
            ModelRunId = modelRunId,
            ScopeKind = geography.ScopeKind,
            ScopeCode = geography.ScopeCode,
            GrossPropertyGgr = ToMoney(netImpact.GrossPropertyGgr),
            TransferEffectGgr = ToMoney(netImpact.TransferEffectGgr),
            CrossJurisdictionImportedGgr = ToMoney(netImpact.CrossJurisdictionImportedGgr),
            OutsideOrUnmodeledLeakageCapture = ToMoney(netImpact.OutsideOrUnmodeledLeakageCapture),
            InducedResidentGgr = ToMoney(netImpact.InducedResidentGgr),
            TourismAndTrafficImportGgr = ToMoney(netImpact.TourismAndTrafficImportGgr),
            LocalDiscretionaryDisplacement = ToMoney(netImpact.LocalDiscretionaryDisplacement),
            DirectAndIndirectLaborIncome = ToMoney(netImpact.DirectAndIndirectLaborIncome),
            NetHostLocalFiscalImpact = ToMoney(netImpact.NetHostLocalFiscalImpact),
            NetHostStateFiscalImpact = ToMoney(netImpact.NetHostStateFiscalImpact),
            GrossSocialCost = ToMoney(netImpact.GrossSocialCost),
            NetNewLocalGamingActivity = ToMoney(netImpact.NetNewLocalGamingActivity),
            NetHostLocalImpact = ToMoney(netImpact.NetHostLocalImpact),
            NetHostStateImpact = ToMoney(netImpact.NetHostStateImpact)
        });

        return new ComprehensiveImpactContext(
            geography.ScopeKind,
            geography.ScopeCode,
            localEquilibrium.Length,
            accounting,
            displacement,
            employment,
            fiscal,
            socialCost,
            netImpact,
            inventoryResolution.WeightBasis,
            warnings);
    }

    private async Task<IncumbentGamingTaxLossContext> CalculateIncumbentGamingTaxLossesAsync(
        GravityModelRunRequest request,
        ExecutionContext context,
        MarketEquilibriumResult equilibrium,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        double hostLoss = 0;
        double otherLoss = 0;
        var jurisdictionIds = context.Competitors
            .Where(competitor => competitor.JurisdictionId.HasValue)
            .Select(competitor => competitor.JurisdictionId!.Value)
            .Distinct()
            .ToArray();
        var jurisdictionCodes = await db.Jurisdictions
            .AsNoTracking()
            .Where(jurisdiction => jurisdictionIds.Contains(jurisdiction.Id))
            .ToDictionaryAsync(jurisdiction => jurisdiction.Id, jurisdiction => jurisdiction.Code, cancellationToken);
        var competitors = context.Competitors.ToDictionary(competitor => competitor.StableVenueId, StringComparer.OrdinalIgnoreCase);
        foreach (var facility in equilibrium.Facilities.Where(facility => !facility.IsProposedFacility && facility.ChangeInAllocatedDemand < 0))
        {
            if (!competitors.TryGetValue(facility.FacilityKey, out var competitor) ||
                competitor.JurisdictionId is not { } jurisdictionId ||
                !jurisdictionCodes.TryGetValue(jurisdictionId, out var jurisdictionCode))
            {
                warnings.Add($"Incumbent fiscal loss was not evaluated for '{facility.FacilityKey}' because its jurisdiction profile is incomplete.");
                continue;
            }
            try
            {
                var baselineRevenue = ToMoney(facility.BaselineAllocatedDemand);
                var withProjectRevenue = ToMoney(facility.WithProjectAllocatedDemand);
                var before = await gamingTaxCalculator.CalculateAsync(new GamingTaxRequest(
                    jurisdictionCode,
                    competitor.FacilityRegime ?? request.FacilityRegime,
                    request.EffectiveOn,
                    0,
                    baselineRevenue,
                    PriorFiscalYearTaxableGamingRevenue: baselineRevenue), cancellationToken);
                var after = await gamingTaxCalculator.CalculateAsync(new GamingTaxRequest(
                    jurisdictionCode,
                    competitor.FacilityRegime ?? request.FacilityRegime,
                    request.EffectiveOn,
                    0,
                    withProjectRevenue,
                    PriorFiscalYearTaxableGamingRevenue: baselineRevenue), cancellationToken);
                decimal beforeSupplementalTax = 0;
                decimal afterSupplementalTax = 0;
                try
                {
                    var supplementalBefore = await gamingFiscalAllocationCalculator.CalculateSupplementalTaxAsync(
                        new SupplementalGamingTaxRequest(
                            jurisdictionCode,
                            competitor.FacilityRegime ?? request.FacilityRegime,
                            request.EffectiveOn,
                            baselineRevenue,
                            competitor.Latitude,
                            competitor.Longitude,
                            competitor.StableVenueId),
                        cancellationToken);
                    var supplementalAfter = await gamingFiscalAllocationCalculator.CalculateSupplementalTaxAsync(
                        new SupplementalGamingTaxRequest(
                            jurisdictionCode,
                            competitor.FacilityRegime ?? request.FacilityRegime,
                            request.EffectiveOn,
                            withProjectRevenue,
                            competitor.Latitude,
                            competitor.Longitude,
                            competitor.StableVenueId),
                        cancellationToken);
                    beforeSupplementalTax = supplementalBefore.SupplementalGamingTax;
                    afterSupplementalTax = supplementalAfter.SupplementalGamingTax;
                }
                catch (UnsupportedJurisdictionException exception)
                {
                    warnings.Add(
                        exception.Message +
                        $" Supplemental gaming-tax loss for '{facility.FacilityKey}' is not estimated; its validated base gaming-tax loss is retained.");
                }
                var marginalTaxLoss = before.GamingTax + beforeSupplementalTax -
                                      after.GamingTax - afterSupplementalTax;
                if (jurisdictionId == context.Jurisdiction.Id)
                {
                    hostLoss += Convert.ToDouble(marginalTaxLoss);
                }
                else
                {
                    otherLoss += Convert.ToDouble(marginalTaxLoss);
                }
            }
            catch (UnsupportedJurisdictionException exception)
            {
                warnings.Add(exception.Message + $" Incumbent fiscal loss for '{facility.FacilityKey}' is not estimated.");
            }
        }
        return new IncumbentGamingTaxLossContext(hostLoss, otherLoss, warnings);
    }

    private static ResolvedImpactGeography ResolveImpactGeography(
        GravityModelRunRequest request,
        IReadOnlyCollection<OriginZone> origins)
    {
        var definition = request.ImpactGeography ?? new ImpactGeographyDefinition(
            ImpactScopeKinds.HostState,
            request.JurisdictionCode);
        if (string.IsNullOrWhiteSpace(definition.ScopeKind) || string.IsNullOrWhiteSpace(definition.ScopeCode))
        {
            throw new ArgumentException("Impact geography requires non-empty scope kind and code.", nameof(request));
        }
        var requestedIds = definition.LocalOriginIds?.ToArray() ?? [];
        if (requestedIds.Any(string.IsNullOrWhiteSpace) ||
            requestedIds.Distinct(StringComparer.Ordinal).Count() != requestedIds.Length)
        {
            throw new ArgumentException("Impact-geography origin IDs must be non-empty and unique.", nameof(request));
        }
        IReadOnlyCollection<string> selectedIds;
        if (requestedIds.Length > 0)
        {
            var available = origins.Select(origin => origin.StableOriginId).ToHashSet(StringComparer.Ordinal);
            var missing = requestedIds.Where(id => !available.Contains(id)).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Impact geography references origins outside the run study area: {string.Join(", ", missing)}.");
            }
            selectedIds = requestedIds;
        }
        else
        {
            selectedIds = definition.ScopeKind switch
            {
                ImpactScopeKinds.HostState => origins
                    .Where(origin => StateMatches(origin.StateOrTerritoryCode, definition.ScopeCode))
                    .Select(origin => origin.StableOriginId)
                    .ToArray(),
                ImpactScopeKinds.HostCounty => origins
                    .Where(origin => string.Equals(origin.CountyEquivalentCode, definition.ScopeCode, StringComparison.OrdinalIgnoreCase))
                    .Select(origin => origin.StableOriginId)
                    .ToArray(),
                ImpactScopeKinds.MetropolitanArea => origins
                    .Where(origin => string.Equals(origin.MetropolitanStatisticalAreaCode, definition.ScopeCode, StringComparison.OrdinalIgnoreCase))
                    .Select(origin => origin.StableOriginId)
                    .ToArray(),
                _ => throw new ArgumentException(
                    $"Impact scope '{definition.ScopeKind}' requires explicit local origin IDs.",
                    nameof(request))
            };
        }
        return new ResolvedImpactGeography(definition.ScopeKind, definition.ScopeCode, selectedIds);
    }

    private static bool StateMatches(string? originStateCode, string scopeCode)
    {
        if (string.IsNullOrWhiteSpace(originStateCode))
        {
            return false;
        }
        var normalizedScope = scopeCode.Split('-', StringSplitOptions.RemoveEmptyEntries)[^1];
        return string.Equals(originStateCode, normalizedScope, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(originStateCode, scopeCode, StringComparison.OrdinalIgnoreCase);
    }

    private void PersistResults(
        ModelRun run,
        GravityModelRunRequest request,
        ExecutionContext context,
        IReadOnlyCollection<OriginDemandContext> originDemand,
        IReadOnlyDictionary<string, double> attractions,
        IReadOnlyDictionary<(long OriginZoneId, string FacilityKey), OriginFacilityTravel> routeByKey,
        IReadOnlyDictionary<string, CasinoCompetitor> competitorByKey,
        MarketEquilibriumResult equilibrium,
        IReadOnlyDictionary<string, OriginExpansionContext> expansions,
        NonresidentDemandContext nonresidentDemand,
        string proposedFacilityKey)
    {
        var demandByKey = originDemand.ToDictionary(item => item.Origin.StableOriginId, StringComparer.Ordinal);
        foreach (var originResult in equilibrium.Origins)
        {
            var demand = demandByKey[originResult.OriginKey];
            var expansion = expansions[originResult.OriginKey];
            var inducedResidentDemand = ToMoney(expansion.Expansion.InducedResidentDemand);
            var inducedFacilityAllocations = originResult.WithProject.FacilityAllocations.ToDictionary(
                allocation => allocation.FacilityKey,
                allocation => ToMoney(allocation.Share * expansion.Expansion.InducedResidentDemand),
                StringComparer.OrdinalIgnoreCase);
            var inducedOutsideOption = inducedResidentDemand - inducedFacilityAllocations.Values.Sum();
            var proposedResidentGgr = ToMoney(originResult.ProposedFacilityDemand);
            var proposedInducedResidentGgr = inducedFacilityAllocations[proposedFacilityKey];
            db.ModelRunOriginResults.Add(new ModelRunOriginResult
            {
                ModelRunId = run.Id,
                OriginZoneId = demand.Origin.Id,
                DemandSpecification = request.DemandSpecification,
                ResidentDemand = ToMoney(demand.Demand.Demand),
                BaselineLogAccessibility = expansion.BaselineLogAccessibility,
                WithProjectLogAccessibility = expansion.WithProjectLogAccessibility,
                InducedResidentDemand = inducedResidentDemand,
                InducedOutsideOptionGgr = inducedOutsideOption,
                BaselineOutsideShare = originResult.Baseline.OutsideOptionShare,
                WithProjectOutsideShare = originResult.WithProject.OutsideOptionShare,
                ProposedResidentGgr = proposedResidentGgr,
                ProposedInducedResidentGgr = proposedInducedResidentGgr,
                TotalProposedResidentGgr = proposedResidentGgr + proposedInducedResidentGgr,
                HostJurisdictionCapture = ToMoney(originResult.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.HostJurisdictionIncumbent)),
                ExternalJurisdictionCapture = ToMoney(originResult.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.ExternalCommercialIncumbent)),
                TribalOrOtherJurisdictionCapture = ToMoney(originResult.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.TribalOrOtherJurisdictionIncumbent)),
                OutsideOptionCapture = ToMoney(originResult.ProposedCaptureBySource.GetValueOrDefault(CaptureSourceCategories.OutsideOption))
            });
            AddAllocations(run.Id, demand.Origin.Id, MarketStates.Baseline, originResult.Baseline, null);
            AddAllocations(
                run.Id,
                demand.Origin.Id,
                MarketStates.WithProject,
                originResult.WithProject,
                inducedFacilityAllocations);
        }

        var inducedByFacility = equilibrium.Origins
            .SelectMany(origin => origin.WithProject.FacilityAllocations.Select(allocation => new
            {
                allocation.FacilityKey,
                Amount = ToMoney(allocation.Share * expansions[origin.OriginKey].Expansion.InducedResidentDemand)
            }))
            .GroupBy(item => item.FacilityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount), StringComparer.OrdinalIgnoreCase);
        foreach (var facility in equilibrium.Facilities)
        {
            competitorByKey.TryGetValue(facility.FacilityKey, out var competitor);
            var inducedResidentGgr = inducedByFacility.GetValueOrDefault(facility.FacilityKey);
            var withProjectResidentGgr = ToMoney(facility.WithProjectAllocatedDemand);
            var totalWithProjectResidentGgr = withProjectResidentGgr + inducedResidentGgr;
            var tourismGgr = facility.IsProposedFacility ? ToMoney(nonresidentDemand.TourismGgr) : 0;
            var trafficGgr = facility.IsProposedFacility ? ToMoney(nonresidentDemand.TrafficGgr) : 0;
            db.ModelRunFacilityResults.Add(new ModelRunFacilityResult
            {
                ModelRunId = run.Id,
                CasinoCompetitorId = competitor?.Id,
                FacilityKey = facility.FacilityKey,
                FacilityKind = facility.IsProposedFacility ? FacilityKinds.Scenario : FacilityKinds.Incumbent,
                IsProposedFacility = facility.IsProposedFacility,
                NormalizedAttraction = attractions[facility.FacilityKey],
                BaselineResidentGgr = ToMoney(facility.BaselineAllocatedDemand),
                WithProjectResidentGgr = withProjectResidentGgr,
                ChangeInResidentGgr = ToMoney(facility.ChangeInAllocatedDemand),
                InducedResidentGgr = inducedResidentGgr,
                TotalWithProjectResidentGgr = totalWithProjectResidentGgr,
                TourismGgr = tourismGgr,
                TrafficGgr = trafficGgr,
                StabilizedTotalGgr = totalWithProjectResidentGgr + tourismGgr + trafficGgr
            });
        }

        void AddAllocations(
            Guid modelRunId,
            long originZoneId,
            string marketState,
            GravityOriginResult result,
            IReadOnlyDictionary<string, decimal>? inducedAllocations)
        {
            foreach (var allocation in result.FacilityAllocations)
            {
                var route = routeByKey[(originZoneId, allocation.FacilityKey)];
                competitorByKey.TryGetValue(allocation.FacilityKey, out var competitor);
                db.ModelRunOriginFacilityAllocations.Add(new ModelRunOriginFacilityAllocation
                {
                    ModelRunId = modelRunId,
                    OriginZoneId = originZoneId,
                    OriginFacilityTravelId = route.Id,
                    CasinoCompetitorId = competitor?.Id,
                    FacilityKey = allocation.FacilityKey,
                    MarketState = marketState,
                    CaptureSourceCategory = allocation.CaptureSourceCategory,
                    IsProposedFacility = allocation.IsProposedFacility,
                    NetworkTravelTimeMinutes = route.TravelTimeMinutes,
                    RoutedDistanceMeters = route.RoutedDistanceMeters,
                    NormalizedAttraction = attractions[allocation.FacilityKey],
                    OriginFacilityModifier = 1,
                    LogWeight = allocation.LogWeight,
                    Share = allocation.Share,
                    AllocatedResidentGgr = ToMoney(allocation.AllocatedDemand),
                    AllocatedInducedResidentGgr = inducedAllocations?.GetValueOrDefault(allocation.FacilityKey) ?? 0
                });
            }
        }
    }

    private void PersistParameterSnapshot(
        Guid modelRunId,
        IReadOnlyCollection<ResolvedModelParameter> resolved,
        IReadOnlyCollection<(long Id, string Layer)> selectedSets)
    {
        db.ModelRunParameterValues.AddRange(resolved.Select(parameter => new ModelRunParameterValue
        {
            ModelRunId = modelRunId,
            ParameterDefinitionId = parameter.Definition.Id,
            SystemFallbackValue = parameter.SystemFallbackValue,
            DefaultValue = parameter.DefaultValue,
            ScenarioValue = parameter.ScenarioValue,
            UserOverrideValue = parameter.UserOverrideValue,
            FinalValue = parameter.FinalValue,
            SourceLayer = parameter.SourceLayer,
            IsOutsideRecommendedRange = parameter.IsOutsideRecommendedRange,
            WarningText = parameter.WarningText
        }));
        db.ModelRunParameterSetReferences.AddRange(selectedSets.Select(item => new ModelRunParameterSetReference
        {
            ModelRunId = modelRunId,
            ParameterSetId = item.Id,
            SourceLayer = item.Layer
        }));
    }

    private static string AddExecutionManifest(
        string existingJson,
        TravelMatrixResolution travel,
        AttractionResolutionContext attractionResolution,
        IReadOnlyDictionary<string, double> parameters,
        IReadOnlyCollection<ResolvedModelParameter> resolved,
        MarketEquilibriumResult equilibrium,
        IReadOnlyDictionary<string, OriginExpansionContext> expansions,
        NonresidentDemandContext nonresidentDemand,
        CapacityAndRampContext capacityAndRamp,
        ComprehensiveImpactContext impact)
    {
        using var document = JsonDocument.Parse(existingJson);
        var existing = document.RootElement.Deserialize<Dictionary<string, object?>>() ?? [];
        existing["routingGraph"] = new
        {
            hash = travel.RoutingGraphHash,
            valhallaVersion = travel.ValhallaVersion,
            tilesetLastModified = travel.TilesetLastModified,
            travel.CostingProfile
        };
        existing["attractionResolution"] = new
        {
            observedRevenueFacilityIds = attractionResolution.ObservedRevenueFacilityIds,
            structuralFallbackFacilityIds = attractionResolution.StructuralFallbackFacilityIds
        };
        existing["resolvedParameters"] = parameters;
        existing["parameterSourceLayers"] = resolved.ToDictionary(
            item => item.Definition.Key,
            item => item.SourceLayer,
            StringComparer.Ordinal);
        existing["residentEquilibrium"] = new
        {
            equilibrium.TotalDemand,
            equilibrium.ProposedFacilityDemand,
            equilibrium.ProposedCaptureBySource,
            equilibrium.ConservationResidual
        };
        existing["accessibilityExpansion"] = new
        {
            baselineResidentDemand = equilibrium.TotalDemand,
            inducedResidentDemand = expansions.Values.Sum(item => item.Expansion.InducedResidentDemand),
            proposedInducedResidentGgr = expansions.Values.Sum(item => item.ProposedInducedResidentGgr),
            originCount = expansions.Count
        };
        existing["nonresidentDemand"] = new
        {
            nonresidentDemand.TourismGgr,
            nonresidentDemand.TrafficGgr
        };
        existing["capacityAndRamp"] = new
        {
            capacityAndRamp.CapacityEvaluated,
            capacityAndRamp.RampYearCount
        };
        existing["impactAccounting"] = new
        {
            impact.ScopeKind,
            impact.ScopeCode,
            impact.LocalOriginCount,
            localInventoryWeightBasis = impact.LocalInventoryWeightBasis,
            impact.Accounting.StabilizedGgr,
            impact.Accounting.TransferEffectGgr,
            impact.Accounting.CrossJurisdictionCapture,
            impact.Displacement.DisplacementEligibleBase,
            impact.Displacement.TotalDisplacedSales,
            impact.Employment.NetPermanentJobs,
            impact.Fiscal.GrossGamingTax,
            impact.Fiscal.NetHostLocalFiscalImpact,
            impact.Fiscal.NetHostStateFiscalImpact,
            grossSocialCost = impact.SocialCost.AnnualCost,
            impact.NetImpact.NetHostLocalImpact,
            impact.NetImpact.NetHostStateImpact
        };
        return JsonSerializer.Serialize(existing);
    }

    private async Task MarkFailedAsync(
        Guid modelRunId,
        Exception exception,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var run = await db.ModelRuns.SingleOrDefaultAsync(item => item.Id == modelRunId, cancellationToken);
        if (run is null || run.Status != ModelRunStatuses.Draft)
        {
            return;
        }
        run.Status = ModelRunStatuses.Failed;
        run.ExecutionDuration = duration;
        run.ErrorSummary = exception.Message;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static ModelRunDatasetSnapshotReference DatasetReference(
        Guid runId,
        Guid snapshotId,
        string role,
        string key) => new()
        {
            ModelRunId = runId,
            DatasetSnapshotId = snapshotId,
            Role = role,
            ReferenceKey = key
        };

    private static IReadOnlyCollection<(long Id, string Layer)> SelectedParameterSets(
        ParameterResolutionRequest request) =>
    [
        .. new[]
        {
            (request.NationalParameterSetId, "national-calibrated-set"),
            (request.JurisdictionParameterSetId, "jurisdiction-market-set"),
            (request.ScenarioParameterSetId, "scenario-preset")
        }.Where(item => item.Item1.HasValue)
         .Select(item => (item.Item1!.Value, item.Item2))
         .Distinct()
    ];

    private static string CaptureCategory(CasinoCompetitor competitor, int hostJurisdictionId)
    {
        if (!string.IsNullOrWhiteSpace(competitor.TribalNationName) ||
            competitor.FacilityRegime?.Contains("tribal", StringComparison.OrdinalIgnoreCase) == true)
        {
            return CaptureSourceCategories.TribalOrOtherJurisdictionIncumbent;
        }
        return competitor.JurisdictionId == hostJurisdictionId
            ? CaptureSourceCategories.HostJurisdictionIncumbent
            : CaptureSourceCategories.ExternalCommercialIncumbent;
    }

    private static void ValidateObservedPeriods(
        IReadOnlyCollection<CasinoGamingRevenuePeriod> periods,
        IReadOnlyCollection<CasinoCompetitor> competitors,
        GravityModelRunRequest request)
    {
        foreach (var competitor in competitors)
        {
            var facilityPeriods = periods
                .Where(period => period.CasinoCompetitorId == competitor.Id)
                .OrderBy(period => period.PeriodStart)
                .ToArray();
            if (facilityPeriods.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Observed-performance snapshot has no '{request.ObservedMetricKey}' periods for '{competitor.StableVenueId}'.");
            }
            if (facilityPeriods.Select(period => period.PeriodGranularity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            {
                throw new InvalidOperationException(
                    $"Observed periods for '{competitor.StableVenueId}' mix granularities and cannot be summed.");
            }
            for (var index = 1; index < facilityPeriods.Length; index++)
            {
                if (facilityPeriods[index].PeriodStart <= facilityPeriods[index - 1].PeriodEnd)
                {
                    throw new InvalidOperationException(
                        $"Observed periods for '{competitor.StableVenueId}' overlap and would double count GGR.");
                }
            }
        }
    }

    private static double ResolveIncomeMetric(OriginZoneIncomePeriod income, string originKey)
    {
        if (income.MedianHouseholdIncome is > 0)
        {
            return Convert.ToDouble(income.MedianHouseholdIncome.Value);
        }
        var agi = income.InflationAdjustedAdjustedGrossIncome ?? income.AdjustedGrossIncome;
        if (agi is > 0 && income.ReturnCount is > 0)
        {
            return Convert.ToDouble(agi.Value) / income.ReturnCount.Value;
        }
        throw new InvalidOperationException(
            $"Origin '{originKey}' has no positive median income or AGI-per-return metric.");
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0 || ordered.Any(value => !double.IsFinite(value) || value <= 0))
        {
            throw new InvalidOperationException("A positive finite sample is required to calculate a reference median.");
        }
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2
            : ordered[middle];
    }

    private static TravelFrictionForm ParseFrictionForm(string value) => value switch
    {
        "inverse-power" => TravelFrictionForm.InversePower,
        "exponential" => TravelFrictionForm.Exponential,
        _ => throw new ArgumentException($"Unsupported friction form '{value}'.")
    };

    private static MissingRouteBehavior ParseMissingRouteBehavior(string value) => value switch
    {
        MissingRoutePolicies.RejectOrigin => MissingRouteBehavior.RejectOrigin,
        MissingRoutePolicies.ExcludeFacility => MissingRouteBehavior.ExcludeFacility,
        _ => throw new ArgumentException($"Unsupported missing-route policy '{value}'.")
    };

    private static double RequireParameter(IReadOnlyDictionary<string, double> parameters, string key) =>
        parameters.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Required resolved parameter '{key}' is missing.");

    private static decimal ToMoney(double value)
    {
        if (!double.IsFinite(value) || value > (double)decimal.MaxValue || value < (double)decimal.MinValue)
        {
            throw new InvalidOperationException("A model amount cannot be represented as persisted decimal currency.");
        }
        return Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
    }

    private static decimal ToQuantity(double value)
    {
        if (!double.IsFinite(value) || value > (double)decimal.MaxValue || value < 0)
        {
            throw new InvalidOperationException("A model quantity cannot be represented as a persisted nonnegative decimal.");
        }
        return Math.Round((decimal)value, 4, MidpointRounding.AwayFromZero);
    }

    private static int IntegerParameter(
        IReadOnlyDictionary<string, double> parameters,
        string key,
        int minimum,
        int maximum)
    {
        var value = RequireParameter(parameters, key);
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        if (Math.Abs(value - rounded) > 1e-9 || rounded < minimum || rounded > maximum)
        {
            throw new InvalidOperationException(
                $"Resolved parameter '{key}' must be an integer between {minimum} and {maximum}.");
        }
        return checked((int)rounded);
    }

    private static void ValidateRequest(GravityModelRunRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ScenarioName) ||
            string.IsNullOrWhiteSpace(request.JurisdictionCode) ||
            string.IsNullOrWhiteSpace(request.FacilityRegime) ||
            string.IsNullOrWhiteSpace(request.CostingProfile))
        {
            throw new ArgumentException("Scenario, jurisdiction, facility regime, and costing profile are required.");
        }
        if (request.StableOriginIds.Count == 0)
        {
            throw new ArgumentException(
                "A versioned study-area definition must provide at least one persisted origin ID.",
                nameof(request));
        }
        if (!double.IsFinite(request.CompetitorPrefilterMiles) || request.CompetitorPrefilterMiles is <= 0 or > 2_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Competitor prefilter miles must be between 0 and 2,000.");
        }
        if (request.ObservedPeriodEnd < request.ObservedPeriodStart)
        {
            throw new ArgumentException("Observed performance period end cannot precede its start.", nameof(request));
        }
        if (request.DemandSpecification is not (GravityDemandSpecifications.AgiShare or GravityDemandSpecifications.EligibleAdultPerCapita))
        {
            throw new ArgumentException($"Unsupported demand specification '{request.DemandSpecification}'.", nameof(request));
        }
        if (request.AttractionSpecification is not (
                FacilityAttractionSpecifications.Structural or
                FacilityAttractionSpecifications.ObservedGgr or
                FacilityAttractionSpecifications.HybridObservedGgr))
        {
            throw new ArgumentException($"Unsupported attraction specification '{request.AttractionSpecification}'.", nameof(request));
        }
        ParseFrictionForm(request.FrictionForm);
        _ = ParseMissingRouteBehavior(request.MissingRoutePolicy);
        var excludedCompetitorIds = request.ExcludedCompetitorIds ?? [];
        if (excludedCompetitorIds.Any(id => id <= 0) ||
            excludedCompetitorIds.Distinct().Count() != excludedCompetitorIds.Count ||
            excludedCompetitorIds.Intersect(request.CompetitorIds).Any())
        {
            throw new ArgumentException(
                "Held-out competitor IDs must be positive, unique, and absent from explicit inclusions.",
                nameof(request));
        }
        if (!double.IsFinite(request.CandidateLatitude) || request.CandidateLatitude is < -90 or > 90 ||
            !double.IsFinite(request.CandidateLongitude) || request.CandidateLongitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Candidate coordinates must be valid WGS84 coordinates.");
        }
        var tourismIds = request.TourismObservationIds ?? [];
        if (tourismIds.Any(string.IsNullOrWhiteSpace) ||
            tourismIds.Distinct(StringComparer.Ordinal).Count() != tourismIds.Count)
        {
            throw new ArgumentException("Tourism observation IDs must be non-empty and unique.", nameof(request));
        }
        foreach (var selection in request.TrafficCorridors ?? [])
        {
            if (string.IsNullOrWhiteSpace(selection.StableObservationId) ||
                !double.IsFinite(selection.RelevantDirectionShare) || selection.RelevantDirectionShare is < 0 or > 1 ||
                !double.IsFinite(selection.InterchangeAccessibilityModifier) || selection.InterchangeAccessibilityModifier is < 0 or > 1)
            {
                throw new ArgumentException(
                    "Traffic selections require a stable observation ID and direction/accessibility shares between zero and one.",
                    nameof(request));
            }
        }
    }

    private sealed record ExecutionContext(
        Jurisdiction Jurisdiction,
        DevelopmentProgram DevelopmentProgram,
        IReadOnlyList<OriginZone> Origins,
        IReadOnlyList<CasinoCompetitor> Competitors,
        IReadOnlyList<TourismMarketObservation> TourismObservations,
        IReadOnlyList<TrafficCorridorObservation> TrafficObservations,
        IReadOnlyList<LocalEconomicSectorObservation> LocalEconomicObservations,
        IReadOnlyDictionary<string, TrafficCorridorRunSelection> TrafficSelections,
        string ComputationalOriginType);

    private sealed record OriginDemandContext(OriginZone Origin, OriginDemandResult Demand, double EligiblePopulation);

    private sealed record AttractionResolutionContext(
        IReadOnlyDictionary<string, double> Attractions,
        IReadOnlyList<string> ObservedRevenueFacilityIds,
        IReadOnlyList<string> StructuralFallbackFacilityIds,
        IReadOnlyList<string> Warnings);

    private sealed record OriginExpansionContext(
        double BaselineLogAccessibility,
        double WithProjectLogAccessibility,
        AccessibilityExpansionResult Expansion,
        double ProposedInducedResidentGgr);

    private sealed record NonresidentDemandContext(
        double TourismGgr,
        double TrafficGgr,
        IReadOnlyList<string> Warnings);

    private sealed record CapacityAndRampContext(
        bool CapacityEvaluated,
        int RampYearCount,
        IReadOnlyList<string> Warnings);

    private sealed record ComprehensiveImpactContext(
        string ScopeKind,
        string ScopeCode,
        int LocalOriginCount,
        CannibalizationAccountingResult Accounting,
        DisplacementResult Displacement,
        EmploymentImpactResult Employment,
        FiscalImpactResult Fiscal,
        SocialCostResult SocialCost,
        NetImpactResult NetImpact,
        string LocalInventoryWeightBasis,
        IReadOnlyList<string> Warnings);


    private sealed record IncumbentGamingTaxLossContext(
        double HostJurisdictionTaxLoss,
        double OtherJurisdictionTaxLoss,
        IReadOnlyList<string> Warnings);

    private sealed record ResolvedImpactGeography(
        string ScopeKind,
        string ScopeCode,
        IReadOnlyCollection<string> LocalOriginIds);

    private sealed class RouteKeyComparer : IEqualityComparer<(long OriginZoneId, string FacilityKey)>
    {
        public bool Equals(
            (long OriginZoneId, string FacilityKey) x,
            (long OriginZoneId, string FacilityKey) y) =>
            x.OriginZoneId == y.OriginZoneId &&
            string.Equals(x.FacilityKey, y.FacilityKey, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((long OriginZoneId, string FacilityKey) obj) =>
            HashCode.Combine(obj.OriginZoneId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FacilityKey));
    }
}

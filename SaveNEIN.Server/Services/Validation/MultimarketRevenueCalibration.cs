// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Diagnostics;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Valhalla;

namespace SaveNEIN.Server.Services.Validation;

public sealed record MultimarketCalibrationProtocol(
    string ProtocolVersion, string FrozenOn, string SourceBundleSha256, int ObservedYear,
    Guid OriginSnapshotId, Guid IncomeSnapshotId, Guid FacilitySnapshotId, Guid PerformanceSnapshotId,
    double OriginRadiusMiles, double CompetitorRadiusMiles,
    double[] Beta, double[] Alpha, double[] PositionsCoefficient, double TablesCoefficient,
    double[] ComparableScale, double[] OutsideWeight,
    string[] Training, string[] Selection, Dictionary<string, string[]> Holdout,
    double[]? GamingPositionsOffset = null,
    string AttractionSpecification = FacilityAttractionSpecifications.HybridObservedGgr,
    string SelectionPolicyVersion = "legacy-selection-wape-v1",
    Dictionary<string, string[]>? DevelopmentMarkets = null,
    double[]? GamingFloorCoefficient = null,
    bool FitDemandScaleByJurisdiction = false,
    string DemandSpecification = GravityDemandSpecifications.AgiShare,
    Guid? AgePopulationSnapshotId = null,
    int EligibleAge = 21,
    double MinimumDemandScale = 0.001,
    double MaximumDemandScale = 0.03,
    double[]? TableGameCoefficient = null,
    bool ConstrainTrainingScale = false,
    double[]? TravelTimeRegularizationMinutes = null,
    double[]? IncomeElasticity = null,
    double IncomeAdjustmentMinimum = 0.5,
    double IncomeAdjustmentMaximum = 1.5);

/// <summary>
/// Revenue-component backtests use the same demand, facility feature and gravity
/// calculations as server execution. They do not fabricate downstream fiscal
/// rules, finalize complete economic runs or publish parameter defaults.
/// </summary>
public sealed class MultimarketRevenueCalibration(AppDbContext db, ValhallaClient routing)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task RunAsync(string protocolPath, string outputPath, CancellationToken cancellationToken = default,
        bool evaluateFinalHoldout = true, bool diagnosticReplay = false)
    {
        if (File.Exists(outputPath)) throw new ArgumentException("Evaluation output already exists.", nameof(outputPath));
        var protocolBytes = await File.ReadAllBytesAsync(protocolPath, cancellationToken);
        var protocolHash = Convert.ToHexString(SHA256.HashData(protocolBytes)).ToLowerInvariant();
        var protocol = JsonSerializer.Deserialize<MultimarketCalibrationProtocol>(protocolBytes, JsonOptions)
            ?? throw new InvalidDataException("Calibration protocol is missing.");
        ValidateAttractionProtocol(protocol);
        var useObservedPeers = protocol.AttractionSpecification == FacilityAttractionSpecifications.HybridObservedGgr;
        var guardedSelection = protocol.SelectionPolicyVersion == CalibrationDevelopmentSelection.PolicyVersion;
        if (!guardedSelection && !diagnosticReplay)
            throw new InvalidDataException("New calibration searches require guarded development selection. Legacy selection is restricted to explicit inspected diagnostic replay.");
        var allTargetKeys = protocol.Training.Concat(protocol.Selection).Concat(protocol.Holdout.Values.SelectMany(value => value)).ToArray();
        if (allTargetKeys.Distinct(StringComparer.Ordinal).Count() != allTargetKeys.Length || protocol.Training.Length < 2 || protocol.Selection.Length == 0)
            throw new InvalidDataException("Protocol partitions overlap or lack training/selection properties.");
        var snapshotIds = new List<Guid> { protocol.OriginSnapshotId, protocol.IncomeSnapshotId, protocol.FacilitySnapshotId, protocol.PerformanceSnapshotId };
        if (protocol.DemandSpecification == GravityDemandSpecifications.EligibleAdultPerCapita) snapshotIds.Add(protocol.AgePopulationSnapshotId!.Value);
        var snapshots = await db.DatasetSnapshots.AsNoTracking().Where(row => snapshotIds.Contains(row.Id))
            .ToListAsync(cancellationToken);
        if (snapshots.Count != snapshotIds.Count || snapshots.Any(row => !row.IsSealed || row.ValidationState == DatasetValidationStates.Rejected))
            throw new InvalidDataException("All declared inputs must be sealed and usable.");
        var facilities = await db.CasinoCompetitors.AsNoTracking().Where(row => row.DatasetSnapshotId == protocol.FacilitySnapshotId)
            .OrderBy(row => row.StableVenueId).ToArrayAsync(cancellationToken);
        var facilityByKey = facilities.ToDictionary(row => row.StableVenueId, StringComparer.Ordinal);
        if (allTargetKeys.Any(key => !facilityByKey.ContainsKey(key))) throw new InvalidDataException("A target is absent from the sealed inventory.");
        if (protocol.FitDemandScaleByJurisdiction)
        {
            var trainingCounts = protocol.Training.GroupBy(key => facilityByKey[key].State, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            if (allTargetKeys.Any(key => !trainingCounts.TryGetValue(facilityByKey[key].State, out var count) || count < 3))
                throw new InvalidDataException("Every evaluated jurisdiction requires at least three training properties before fitting regional shares.");
        }
        if (evaluateFinalHoldout && !diagnosticReplay)
        {
            var inspectedIds = await ModelReleaseAcceptance.PreviouslyEvaluatedPropertyIdsAsync(db, cancellationToken);
            if (protocol.Holdout.Values.SelectMany(value => value).Any(key => inspectedIds.Contains(facilityByKey[key].Id)))
                throw new InvalidOperationException("Final holdout contains previously inspected properties. Use development-only mode; predeclare a new untouched sample for release validation.");
        }
        if (allTargetKeys.Any(key => facilityByKey[key].SlotOrVltPositions is null)) throw new InvalidDataException("Targets require observed physical slot/VLT counts.");
        var origins = await db.OriginZones.AsNoTracking().Where(row => row.DatasetSnapshotId == protocol.OriginSnapshotId)
            .OrderBy(row => row.StableOriginId).ToArrayAsync(cancellationToken);
        var incomeRows = await db.OriginZoneIncomePeriods.AsNoTracking().Where(row => row.DatasetSnapshotId == protocol.IncomeSnapshotId)
            .ToArrayAsync(cancellationToken);
        var incomeById = incomeRows.ToDictionary(row => row.OriginZoneId, row => Convert.ToDouble(row.InflationAdjustedAdjustedGrossIncome ?? row.AdjustedGrossIncome));
        if (origins.Any(row => !incomeById.ContainsKey(row.Id))) throw new InvalidDataException("Origins lack sealed income.");
        var demandUnitsById = incomeById;
        Dictionary<long, double>? incomeMetricsById = null;
        if (protocol.DemandSpecification == GravityDemandSpecifications.EligibleAdultPerCapita)
        {
            var incomePeriodsById = incomeRows.ToDictionary(row => row.OriginZoneId);
            incomeMetricsById = origins.ToDictionary(origin => origin.Id,
                origin => GravityModelExecutionService.ResolveIncomeMetric(incomePeriodsById[origin.Id], origin.StableOriginId));
            var ageSnapshot = snapshots.Single(row => row.Id == protocol.AgePopulationSnapshotId);
            if (ageSnapshot.DatasetKey != DatasetSnapshotKinds.AgePopulation) throw new InvalidDataException("The declared age snapshot has a different data kind.");
            var ageRows = await db.OriginZoneAgeBins.AsNoTracking().Where(row => row.DatasetSnapshotId == ageSnapshot.Id).ToArrayAsync(cancellationToken);
            var bins = ageRows.GroupBy(row => row.OriginZoneId).ToDictionary(group => group.Key, group => group.ToArray());
            if (origins.Any(origin => !bins.ContainsKey(origin.Id))) throw new InvalidDataException("Per-capita demand requires age bins for every selected origin.");
            if (ageRows.Select(row => row.ObservationYear).Distinct().Count() != 1) throw new InvalidDataException("Age observations must use one declared vintage.");
            var populationYear = ageRows[0].ObservationYear;
            demandUnitsById = origins.ToDictionary(origin => origin.Id, origin =>
            {
                var eligible = EligiblePopulationCalculator.Calculate(bins[origin.Id].Select(bin => new AgeBinValue(bin.MinimumAge, bin.MaximumAge, bin.Population)).ToArray(), protocol.EligibleAge).Population;
                var projected = PopulationProjectionCalculator.Calculate(new PopulationProjectionInput(eligible, populationYear, protocol.ObservedYear, 0)).ProjectedPopulation;
                // Population growth remains zero in this component diagnostic.
                // Apply the case-relative server income adjustment during prediction.
                return projected;
            });
        }
        var start = new DateOnly(protocol.ObservedYear, 1, 1);
        var end = new DateOnly(protocol.ObservedYear, 12, 31);
        var periods = await db.CasinoGamingRevenuePeriods.AsNoTracking().Where(row => row.DatasetSnapshotId == protocol.PerformanceSnapshotId &&
            row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue && row.PeriodStart >= start && row.PeriodEnd <= end)
            .OrderBy(row => row.PeriodStart).ToArrayAsync(cancellationToken);
        var observedById = new Dictionary<int, double>();
        foreach (var group in periods.GroupBy(row => row.CasinoCompetitorId))
        {
            var cursor = start;
            var complete = true;
            foreach (var row in group)
            {
                if (row.PeriodStart != cursor || row.PeriodEnd < row.PeriodStart) { complete = false; break; }
                cursor = row.PeriodEnd.AddDays(1);
            }
            if (complete && cursor == end.AddDays(1) && group.Select(row => row.ReportedMetricDefinition).Distinct().Count() == 1)
                observedById[group.Key] = group.Sum(row => Convert.ToDouble(row.InflationAdjustedAmount ?? row.ReportedAmount));
        }
        if (allTargetKeys.Any(key => !observedById.TryGetValue(facilityByKey[key].Id, out var value) || value <= 0))
            throw new InvalidDataException("Targets require complete positive comparable annual revenue.");
        var cases = allTargetKeys.Select(key =>
        {
            var facility = facilityByKey[key];
            var caseOrigins = origins.Where(origin => CompetitiveUniverseService.IsWithinBroadPrefilter(origin.RepresentativePoint.Y,
                origin.RepresentativePoint.X, facility.Latitude, facility.Longitude, protocol.OriginRadiusMiles)).ToArray();
            var casePeers = facilities.Where(peer => peer.Id != facility.Id && CompetitiveUniverseService.IsWithinBroadPrefilter(
                peer.Latitude, peer.Longitude, facility.Latitude, facility.Longitude, protocol.CompetitorRadiusMiles)).ToArray();
            if (caseOrigins.Length == 0 || casePeers.Length == 0) throw new InvalidDataException($"Empty market for {key}.");
            var partition = protocol.Training.Contains(key) ? "training" : protocol.Selection.Contains(key) ? "selection" : "holdout";
            var market = protocol.Holdout.FirstOrDefault(group => group.Value.Contains(key)).Key
                ?? protocol.DevelopmentMarkets?.FirstOrDefault(group => group.Value.Contains(key)).Key ?? "development";
            return new PreparedCase(facility, partition, market, observedById[facility.Id], caseOrigins, casePeers);
        }).ToArray();
        var referenceIncomeByCase = incomeMetricsById is null ? null : cases.ToDictionary(item => item.Facility.Id,
            item => Median(item.Origins.Select(origin => incomeMetricsById[origin.Id])));
        var demandService = new OriginDemandService();
        Console.WriteLine($"Frozen protocol {protocolHash}: {protocol.Training.Length} training, {protocol.Selection.Length} selection, {protocol.Holdout.Values.Sum(value => value.Length)} final holdout properties.");
        var graph = await routing.GetRoutingGraphIdentityAsync(cancellationToken);
        var routingCases = evaluateFinalHoldout ? cases : cases.Where(item => item.Partition != "holdout").ToArray();
        var routes = await WarmRoutesAsync(origins, facilities, routingCases, graph, cancellationToken);
        var withheldIds = cases.Where(item => item.Partition != "training").Select(item => item.Facility.Id).ToHashSet();
        var featureService = new FacilityAttractivenessService();
        var metrics = new ValidationMetricsService();
        var searchRows = new List<SearchRow>();
        double[] UnitPredictions(double beta, double alpha, double coefficient, double positionsOffset, double floorCoefficient, double tableCoefficient, double travelRegularization, double incomeElasticity, double scale, double outside, IEnumerable<PreparedCase> requestedCases)
        {
            var parameters = FeatureParameters(coefficient, tableCoefficient, positionsOffset);
            parameters["facility.gaming_floor_coefficient"] = floorCoefficient;
            var structural = facilities.ToDictionary(row => row.Id, row => featureService.CalculateStructural(new(row.StableVenueId,
                GravityModelExecutionService.FacilityFeatures(row, parameters), MissingFacilityAttributeBehavior.UseReferenceValue)).NormalizedAttraction);
            return requestedCases.Select(item =>
            {
                var observedPeers = item.Peers.Where(peer => !withheldIds.Contains(peer.Id) && observedById.ContainsKey(peer.Id)).ToArray();
                if (useObservedPeers && observedPeers.Length == 0) throw new InvalidDataException("No non-held-out observed competitor mass.");
                var reference = useObservedPeers ? Median(observedPeers.Select(peer => observedById[peer.Id])) : 1d;
                // Backtest the same sourced facility, retaining missing attributes
                // and known position definitions instead of constructing a different program.
                var proposedMass = structural[item.Facility.Id];
                proposedMass = FacilityAttractivenessService.NormalizeStructuralMass(proposedMass, scale);
                var peerMasses = item.Peers.ToDictionary(peer => peer.Id, peer => useObservedPeers && !withheldIds.Contains(peer.Id) && observedById.TryGetValue(peer.Id, out var amount)
                    ? featureService.CalculateObservedGgr(new(peer.StableVenueId, amount, reference, false)).NormalizedAttraction : FacilityAttractivenessService.NormalizeStructuralMass(structural[peer.Id], scale));
                var gravityParameters = new GravityParameters(alpha, TravelFrictionForm.InversePower, beta, travelRegularization, MissingRouteBehavior.ExcludeFacility);
                return item.Origins.Sum(origin =>
                {
                    var alternatives = item.Peers.Select(peer => Alternative(origin.Id, peer, peerMasses[peer.Id], false, routes))
                        .Append(Alternative(origin.Id, item.Facility, proposedMass, true, routes)).ToArray();
                    var demand = incomeMetricsById is null ? demandUnitsById[origin.Id] : demandService.CalculatePerCapita(new(
                        origin.StableOriginId, demandUnitsById[origin.Id], 1, incomeMetricsById[origin.Id], referenceIncomeByCase![item.Facility.Id],
                        incomeElasticity, protocol.IncomeAdjustmentMinimum, protocol.IncomeAdjustmentMaximum)).Demand;
                    return GravityModelService.AllocateTargetDemand(new(origin.StableOriginId, demand, outside, alternatives),
                        gravityParameters, item.Facility.StableVenueId);
                });
            }).ToArray();
        }
        var development = cases.Where(item => item.Partition != "holdout").ToArray();
        var trainingIndices = development.Select((item, index) => (item, index)).Where(value => value.item.Partition == "training").Select(value => value.index).ToArray();
        var searchTimer = Stopwatch.StartNew();
        foreach (var beta in protocol.Beta)
        foreach (var alpha in protocol.Alpha)
        foreach (var coefficient in protocol.PositionsCoefficient)
        foreach (var positionsOffset in protocol.GamingPositionsOffset ?? [2000d])
        foreach (var floorCoefficient in protocol.GamingFloorCoefficient ?? [0d])
        foreach (var tableCoefficient in protocol.TableGameCoefficient ?? [protocol.TablesCoefficient])
        foreach (var travelRegularization in protocol.TravelTimeRegularizationMinutes ?? [1d])
        foreach (var incomeElasticity in protocol.IncomeElasticity ?? [0d])
        foreach (var scale in protocol.ComparableScale)
        foreach (var outside in protocol.OutsideWeight)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var units = UnitPredictions(beta, alpha, coefficient, positionsOffset, floorCoefficient, tableCoefficient, travelRegularization, incomeElasticity, scale, outside, development);
            if (trainingIndices.Any(index => units[index] <= 0)) continue;
            var share = guardedSelection
                ? CalibrationDevelopmentSelection.FitTrainingWapeScale(trainingIndices.Select(index => (development[index].Observed, units[index])), protocol.MinimumDemandScale, protocol.MaximumDemandScale, protocol.ConstrainTrainingScale)
                : Math.Clamp(Median(trainingIndices.Select(index => development[index].Observed / units[index])), 0.001, 0.03);
            Dictionary<string, double>? jurisdictionShares = null;
            if (protocol.FitDemandScaleByJurisdiction)
                jurisdictionShares = trainingIndices.GroupBy(index => development[index].Facility.State, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => CalibrationDevelopmentSelection.FitTrainingWapeScale(
                        group.Select(index => (development[index].Observed, units[index])), protocol.MinimumDemandScale, protocol.MaximumDemandScale, protocol.ConstrainTrainingScale), StringComparer.Ordinal);
            var observations = development.Select((item, index) => new ValidationObservation(item.Facility.StableVenueId, item.Observed,
                units[index] * (jurisdictionShares is null ? share : jurisdictionShares[item.Facility.State]))).ToArray();
            var selection = observations.Where((_, index) => development[index].Partition == "selection").ToArray();
            var training = observations.Where((_, index) => development[index].Partition == "training").ToArray();
            var admission = guardedSelection ? CalibrationDevelopmentSelection.Evaluate(development.Select((item, index) =>
                new DevelopmentRevenueObservation(item.Facility.StableVenueId, item.Partition, item.Market, item.Facility.State,
                    item.Observed, observations[index].Predicted)).ToArray()) : null;
            searchRows.Add(new($"beta={beta:R};alpha={alpha:R};positions={coefficient:R};positions-offset={positionsOffset:R};floor={floorCoefficient:R};tables={tableCoefficient:R};travel-regularization={travelRegularization:R};income-elasticity={incomeElasticity:R};scale={scale:R};outside={outside:R}", beta, alpha,
                coefficient, positionsOffset, floorCoefficient, tableCoefficient, travelRegularization, incomeElasticity, scale, outside, share, jurisdictionShares, metrics.Calculate(training), metrics.Calculate(selection), Wape(selection), observations, admission));
            if (searchRows.Count % 20 == 0) Console.WriteLine($"Scored {searchRows.Count} training/selection candidates in {searchTimer.Elapsed.TotalSeconds:F0}s; final holdout remains unscored.");
        }
        var ranked = searchRows.OrderBy(row => row.SelectionWape).ThenBy(row => row.SelectionMetrics.MeanAbsolutePercentageError).ThenBy(row => row.Key, StringComparer.Ordinal);
        var selected = ranked.FirstOrDefault(row => !guardedSelection || row.Admission!.Admitted);
        if (selected is null)
        {
            var diagnostic = searchRows.OrderBy(row => Math.Max(Wape(row.Observations.Where((_, index) => development[index].Partition == "training")), row.SelectionWape))
                .ThenBy(row => row.Key, StringComparer.Ordinal).First();
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                ProtocolHash = protocolHash, protocol.ProtocolVersion, protocol.SelectionPolicyVersion, protocol.AttractionSpecification, protocol.DemandSpecification,
                FittedScaleUnits = protocol.DemandSpecification == GravityDemandSpecifications.AgiShare ? "share of AGI" : "USD/eligible-adult/year",
                Scope = "No candidate passed development admission. Diagnostic candidates are not selected specifications; no final holdout scored.",
                CandidateCount = searchRows.Count, AdmittedCandidateCount = 0, Selected = (SearchRow?)null, BestDiagnosticCandidate = diagnostic,
                graph.GraphHash, MatrixCorrelationVersion = ValhallaClient.MatrixCorrelationVersion,
                FinalHoldoutScored = false, ReleaseAccepted = false,
                SearchEvidence = searchRows.Select(row => new { row.Key, row.FittedDemandScale, row.FittedDemandScaleByJurisdiction,
                    row.TrainingMetrics, row.SelectionMetrics, row.SelectionWape, row.Admission })
            }, JsonOptions), cancellationToken);
            Console.WriteLine($"NO ADMITTED CANDIDATE: {searchRows.Count} rejected; final holdout remains unscored. Saved {outputPath}.");
            return;
        }
        if (!evaluateFinalHoldout)
        {
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
            {
                ProtocolHash = protocolHash, Scope = "Development-only structural normalization remediation; previously inspected final holdouts remain withheld and are not rescored.",
                ScalingVersion = "consistent-physical-v2", protocol.AttractionSpecification, protocol.SelectionPolicyVersion, protocol.DemandSpecification,
                FittedScaleUnits = protocol.DemandSpecification == GravityDemandSpecifications.AgiShare ? "share of AGI" : "USD/eligible-adult/year",
                CandidateCount = searchRows.Count, AdmittedCandidateCount = searchRows.Count(row => row.Admission?.Admitted ?? true), SelectedOn = guardedSelection ? "selection among admitted development fits" : "legacy selection only", Selected = selected,
                graph.GraphHash, MatrixCorrelationVersion = ValhallaClient.MatrixCorrelationVersion,
                ReleaseAccepted = false, FinalHoldoutScored = false,
                RouteCoverage = development.Select(item => new { item.Facility.StableVenueId, OriginCount = item.Origins.Length,
                    ReachableCandidateRoutes = item.Origins.Count(origin => routes[(origin.Id, item.Facility.Id)].RouteFound) }),
                SearchEvidence = searchRows.Select(row => new { row.Key, row.FittedDemandScale, row.TrainingMetrics, row.SelectionMetrics, row.SelectionWape })
            }, JsonOptions), cancellationToken);
            Console.WriteLine($"DEVELOPMENT ONLY: selection MAPE={selected.SelectionMetrics.MeanAbsolutePercentageError:F4}% WAPE={selected.SelectionWape:F4}%; no final holdout rescored. Saved {outputPath}.");
            return;
        }
        // The final holdout is evaluated exactly once, after every selection decision.
        var holdoutCases = cases.Where(item => item.Partition == "holdout").ToArray();
        var holdoutUnits = UnitPredictions(selected.Beta, selected.Alpha, selected.PositionsCoefficient, selected.PositionsOffset, selected.FloorCoefficient, selected.TableCoefficient, selected.TravelRegularizationMinutes, selected.IncomeElasticity, selected.Scale, selected.OutsideWeight, holdoutCases);
        var holdoutRows = holdoutCases.Select((item, index) => new ValidationObservation(item.Facility.StableVenueId, item.Observed,
            holdoutUnits[index] * (selected.FittedDemandScaleByJurisdiction is null ? selected.FittedDemandScale : selected.FittedDemandScaleByJurisdiction[item.Facility.State]))).ToArray();
        var holdoutMetrics = metrics.Calculate(holdoutRows);
        var holdoutWape = Wape(holdoutRows);
        var holdoutBias = 100 * holdoutRows.Sum(row => row.Predicted - row.Observed) / holdoutRows.Sum(row => row.Observed);
        var output = new
        {
            ProtocolHash = protocolHash, protocol.ProtocolVersion, protocol.AttractionSpecification, protocol.DemandSpecification,
            Scope = diagnosticReplay ? "Diagnostic replay of inspected properties after development-only selection; not an untouched acceptance evaluation." : "Resident revenue-component calibration; no downstream acceptance or parameter publication.",
            HoldoutPreviouslyInspected = diagnosticReplay, ScalingVersion = "consistent-physical-v2",
            MatrixCorrelationVersion = ValhallaClient.MatrixCorrelationVersion,
            InputSnapshots = snapshots.Select(row => new { row.Id, row.Checksum, row.ValidationState }), graph.GraphHash,
            CandidateCount = searchRows.Count, SelectedOn = "selection only", Selected = selected,
            HoldoutMetrics = holdoutMetrics, HoldoutWape = holdoutWape, HoldoutNormalizedBias = holdoutBias,
            HoldoutPropertyCount = holdoutCases.Length,
            Holdout = holdoutCases.Select((item, index) => new
            {
                item.Facility.StableVenueId, item.Facility.State, item.Market,
                Observed = item.Observed, Predicted = holdoutRows[index].Predicted,
                Ape = 100 * Math.Abs(holdoutRows[index].Predicted - item.Observed) / item.Observed,
                OriginCount = item.Origins.Length, CompetitorCount = item.Peers.Length,
                MissingCandidateRoutes = item.Origins.Count(origin => !routes[(origin.Id, item.Facility.Id)].RouteFound)
            }),
            NumericGates = new { MapePassed = holdoutMetrics.MeanAbsolutePercentageError <= 20, WapePassed = holdoutWape <= 15,
                BiasPassed = Math.Abs(holdoutBias) <= 5, MaximumPropertyPassed = holdoutRows.All(row => 100 * Math.Abs(row.Predicted - row.Observed) / row.Observed <= 40) },
            ReleaseAccepted = false,
            ReleaseBlocks = new[] { "Independent required-input/fiscal/reproducibility audits remain incomplete.", "Competitive inventory and nonresident demand coverage remain scoped and incomplete." },
            SearchEvidence = searchRows.Select(row => new { row.Key, row.FittedDemandScale, row.TrainingMetrics, row.SelectionMetrics, row.SelectionWape })
        };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(output, JsonOptions), cancellationToken);
        await RegisterInspectionAsync(protocolPath, outputPath, cancellationToken);
        Console.WriteLine($"{(diagnosticReplay ? "INSPECTED-SAMPLE DIAGNOSTIC" : "NEW HOLDOUT")}: MAPE={holdoutMetrics.MeanAbsolutePercentageError:F4}% WAPE={holdoutWape:F4}% bias={holdoutBias:F4}% across {holdoutCases.Length} properties. Saved {outputPath}.");
    }

    internal static void ValidateAttractionProtocol(MultimarketCalibrationProtocol protocol)
    {
        if (protocol.AttractionSpecification is not (FacilityAttractionSpecifications.Structural or FacilityAttractionSpecifications.HybridObservedGgr))
            throw new InvalidDataException("Component calibration supports structural or hybrid-observed-ggr attraction.");
        if (protocol.GamingPositionsOffset is { } offsets &&
            (offsets.Length == 0 || offsets.Any(value => !double.IsFinite(value) || value <= 0)))
            throw new InvalidDataException("Gaming-position offsets must be finite and positive before routing or calibration.");
        if (protocol.SelectionPolicyVersion is not ("legacy-selection-wape-v1" or CalibrationDevelopmentSelection.PolicyVersion))
            throw new InvalidDataException("Unknown development selection policy.");
        if (protocol.FitDemandScaleByJurisdiction && protocol.SelectionPolicyVersion != CalibrationDevelopmentSelection.PolicyVersion)
            throw new InvalidDataException("Jurisdiction share fitting requires guarded development selection.");
        if (protocol.DemandSpecification is not (GravityDemandSpecifications.AgiShare or GravityDemandSpecifications.EligibleAdultPerCapita) ||
            (protocol.DemandSpecification == GravityDemandSpecifications.EligibleAdultPerCapita &&
             (protocol.AgePopulationSnapshotId is null || protocol.AgePopulationSnapshotId == Guid.Empty || protocol.EligibleAge is < 18 or > 25 ||
              protocol.SelectionPolicyVersion != CalibrationDevelopmentSelection.PolicyVersion)))
            throw new InvalidDataException("Per-capita calibration requires an explicit age snapshot, eligible age and guarded selection.");
        if (!double.IsFinite(protocol.MinimumDemandScale) || !double.IsFinite(protocol.MaximumDemandScale) ||
            protocol.MinimumDemandScale <= 0 || protocol.MaximumDemandScale < protocol.MinimumDemandScale)
            throw new InvalidDataException("Demand scale bounds must be finite, positive and ordered.");
        if (protocol.GamingFloorCoefficient is { } floors && (floors.Length == 0 || floors.Any(value => !double.IsFinite(value) || value < 0)))
            throw new InvalidDataException("Gaming-floor coefficients must be finite and nonnegative.");
        if (protocol.TableGameCoefficient is { } tables && (tables.Length == 0 || tables.Any(value => !double.IsFinite(value) || value < 0)))
            throw new InvalidDataException("Table-game coefficients must be finite and nonnegative.");
        if (protocol.ConstrainTrainingScale && protocol.SelectionPolicyVersion != CalibrationDevelopmentSelection.PolicyVersion)
            throw new InvalidDataException("Training scale constraints require guarded selection.");
        if (protocol.TravelTimeRegularizationMinutes is { } regularization &&
            (regularization.Length == 0 || regularization.Any(value => !double.IsFinite(value) || value <= 0)))
            throw new InvalidDataException("Travel-time regularization must be finite and positive.");
        if (protocol.IncomeElasticity is { } elasticities && (elasticities.Length == 0 ||
            elasticities.Any(value => !double.IsFinite(value) || value is < -5 or > 5) ||
            (protocol.DemandSpecification != GravityDemandSpecifications.EligibleAdultPerCapita && elasticities.Any(value => value != 0))))
            throw new InvalidDataException("Income elasticity requires per-capita demand and finite values within server parameter limits.");
        if (!double.IsFinite(protocol.IncomeAdjustmentMinimum) || !double.IsFinite(protocol.IncomeAdjustmentMaximum) ||
            protocol.IncomeAdjustmentMinimum <= 0 || protocol.IncomeAdjustmentMaximum < protocol.IncomeAdjustmentMinimum)
            throw new InvalidDataException("Income adjustment bounds must be finite, positive and ordered.");
        if (protocol.SelectionPolicyVersion == CalibrationDevelopmentSelection.PolicyVersion)
        {
            var development = protocol.Training.Concat(protocol.Selection).ToArray();
            var declared = protocol.DevelopmentMarkets?.Values.SelectMany(value => value).ToArray() ?? [];
            if (declared.Length != development.Length || declared.Distinct(StringComparer.Ordinal).Count() != declared.Length ||
                !declared.Order().SequenceEqual(development.Order()))
                throw new InvalidDataException("Guarded selection requires a complete, nonoverlapping development-market declaration.");
        }
    }

    public async Task RegisterInspectionAsync(string protocolPath, string resultPath, CancellationToken cancellationToken = default)
    {
        var protocolBytes = await File.ReadAllBytesAsync(protocolPath, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(protocolBytes)).ToLowerInvariant();
        var protocol = JsonSerializer.Deserialize<MultimarketCalibrationProtocol>(protocolBytes, JsonOptions)
            ?? throw new InvalidDataException("Calibration protocol is missing.");
        var resultBytes = await File.ReadAllBytesAsync(resultPath, cancellationToken);
        using var result = JsonDocument.Parse(resultBytes);
        var root = result.RootElement;
        if (root.GetProperty("protocolHash").GetString() != hash || root.GetProperty("releaseAccepted").GetBoolean())
            throw new InvalidDataException("Only a matching, unaccepted component diagnostic can be registered.");
        var holdoutKeys = root.GetProperty("holdout").EnumerateArray().Select(row => row.GetProperty("stableVenueId").GetString()!).ToArray();
        var expectedKeys = protocol.Holdout.Values.SelectMany(value => value).ToArray();
        if (holdoutKeys.Length != expectedKeys.Length || !holdoutKeys.Order().SequenceEqual(expectedKeys.Order()))
            throw new InvalidDataException("Diagnostic holdout identities do not match the frozen protocol.");
        var evaluationKey = $"resident-component-inspection-{hash}";
        if (await db.ValidationEvaluations.AnyAsync(row => row.EvaluationKey == evaluationKey, cancellationToken)) return;
        db.ValidationEvaluations.Add(new ValidationEvaluation
        {
            EvaluationKey = evaluationKey, Version = "component-diagnostic-v1", ModelVersion = "resident-revenue-component",
            ObjectiveFunction = ValidationObjectiveFunctions.Mape, Status = ValidationEvaluationStatuses.Finalized, IsImmutable = true,
            FinalizedAtUtc = DateTime.UtcNow, TrainingCaseCount = protocol.Training.Length, HoldoutCaseCount = holdoutKeys.Length,
            InclusionRulesJson = JsonSerializer.Serialize(new { evaluationKind = "resident-component-inspection", selectionObjective = "wape", protocolHash = hash,
                resultSha256 = Convert.ToHexString(SHA256.HashData(resultBytes)).ToLowerInvariant(), releaseAccepted = false,
                inspectedPropertyKeys = protocol.Training.Concat(protocol.Selection).Concat(holdoutKeys).ToArray() }, JsonOptions),
            SelectedParametersJson = root.GetProperty("selected").GetRawText(),
            TrainingMetricsJson = root.GetProperty("selected").GetProperty("trainingMetrics").GetRawText(),
            HoldoutMetricsJson = root.GetProperty("holdoutMetrics").GetRawText()
        });
        await db.SaveChangesAsync(cancellationToken);
        Console.WriteLine($"Registered immutable component inspection {evaluationKey}; no model runs or published parameters created.");
    }

    private async Task<Dictionary<(long OriginId, int FacilityId), OriginFacilityTravel>> WarmRoutesAsync(OriginZone[] origins,
        CasinoCompetitor[] facilities, PreparedCase[] cases, ValhallaRoutingGraphIdentity graph, CancellationToken cancellationToken)
    {
        var required = new HashSet<(long, int)>();
        foreach (var item in cases)
        foreach (var origin in item.Origins)
        foreach (var facility in item.Peers.Append(item.Facility)) required.Add((origin.Id, facility.Id));
        var originById = origins.ToDictionary(row => row.Id);
        var facilityById = facilities.ToDictionary(row => row.Id);
        var oldRows = await (from route in db.OriginFacilityTravel.AsNoTracking()
                             join origin in db.OriginZones.AsNoTracking() on route.OriginZoneId equals origin.Id
                             join facility in db.CasinoCompetitors.AsNoTracking() on route.CasinoCompetitorId equals facility.Id
                             where route.RoutingGraphHash == graph.GraphHash && route.CostingProfile == "auto" && route.CasinoCompetitorId.HasValue
                             select new { Route = route, origin.StableOriginId, facility.StableVenueId, Longitude = origin.RepresentativePoint.X, Latitude = origin.RepresentativePoint.Y })
            .ToListAsync(cancellationToken);
        var cached = oldRows.GroupBy(row => (row.StableOriginId, row.StableVenueId))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.Route.CalculatedAtUtc).First());
        var result = new ConcurrentDictionary<(long, int), OriginFacilityTravel>();
        using var writer = new SemaphoreSlim(1, 1);
        var requiredByOrigin = required.GroupBy(pair => pair.Item1).ToDictionary(group => group.Key, group => group.Select(pair => pair.Item2).ToArray());
        var pending = new List<OriginFacilityTravel>();
        foreach (var pair in required)
        {
            var origin = originById[pair.Item1];
            var facility = facilityById[pair.Item2];
            if (cached.TryGetValue((origin.StableOriginId, facility.StableVenueId), out var old) && old.Latitude == origin.RepresentativePoint.Y && old.Longitude == origin.RepresentativePoint.X &&
                old.Route.FacilityCoordinateHash == TravelMatrixService.CandidateCoordinateHash(facility.Latitude, facility.Longitude))
            {
                result[pair] = old.Route;
            }
            else if (!CompetitiveUniverseService.IsWithinBroadPrefilter(origin.RepresentativePoint.Y, origin.RepresentativePoint.X,
                facility.Latitude, facility.Longitude, 200)) Store(origin, facility, null, null, false, "Declared 200-mile matrix eligibility prefilter; no routed time substituted.");
        }
        await SaveAsync();
        Console.WriteLine($"Route preparation: {result.Count}/{required.Count} pairs reusable or outside the declared routing prefilter.");
        var progress = Stopwatch.StartNew();
        // Ten sources plus ten targets respects the deployed 20-location limit.
        var originGroups = required.Select(pair => pair.Item1).Distinct().Select(id => originById[id])
            .OrderBy(row => row.CountyEquivalentCode).ThenBy(row => row.RepresentativePoint.Y).ThenBy(row => row.RepresentativePoint.X).Chunk(10).ToArray();
        await Parallel.ForEachAsync(originGroups, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (originGroup, _) =>
        {
            var targetIds = originGroup.SelectMany(origin => requiredByOrigin[origin.Id].Where(id => !result.ContainsKey((origin.Id, id)))).Distinct().ToArray();
            foreach (var targetGroup in targetIds.Select(id => facilityById[id]).Chunk(10)) await MatrixAsync(originGroup, targetGroup);
            await writer.WaitAsync(cancellationToken);
            try { if (progress.Elapsed.TotalSeconds > 30) { Console.WriteLine($"Routed/prepared {result.Count}/{required.Count} required pairs."); progress.Restart(); } }
            finally { writer.Release(); }
        });
        return result.ToDictionary(pair => pair.Key, pair => pair.Value);

        async Task MatrixAsync(OriginZone[] sources, CasinoCompetitor[] targets)
        {
            if (!sources.Any(source => targets.Any(target => required.Contains((source.Id, target.Id)) && !result.ContainsKey((source.Id, target.Id))))) return;
            try
            {
                var matrix = await routing.GetDriveTimeMatrixAsync(sources.Select(row => new ValhallaMatrixLocation(row.RepresentativePoint.Y, row.RepresentativePoint.X)).ToArray(),
                    targets.Select(row => new ValhallaMatrixLocation(row.Latitude, row.Longitude)).ToArray(), "auto", cancellationToken);
                await writer.WaitAsync(cancellationToken);
                try
                {
                    foreach (var cell in matrix.Cells)
                    {
                        var source = sources[cell.SourceIndex]; var target = targets[cell.TargetIndex];
                        if (!required.Contains((source.Id, target.Id)) || result.ContainsKey((source.Id, target.Id))) continue;
                        Store(source, target, cell.TravelTimeMinutes, cell.RoutedDistanceMeters, cell.RouteFound, cell.RouteFound ? null : "Valhalla unreachable pair.");
                    }
                    await SaveAsync();
                }
                finally { writer.Release(); }
            }
            catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.BadRequest &&
                (error.Message.Contains("442", StringComparison.Ordinal) || error.Message.Contains("154", StringComparison.Ordinal) ||
                 error.Message.Contains("171", StringComparison.Ordinal)))
            {
                if (sources.Length > 1) { var middle = sources.Length / 2; await MatrixAsync(sources[..middle], targets); await MatrixAsync(sources[middle..], targets); }
                else if (targets.Length > 1) { var middle = targets.Length / 2; await MatrixAsync(sources, targets[..middle]); await MatrixAsync(sources, targets[middle..]); }
                else
                {
                    await writer.WaitAsync(cancellationToken);
                    try
                    {
                        Store(sources[0], targets[0], null, null, false,
                            error.Message.Contains("171", StringComparison.Ordinal) ? "Valhalla has no suitable graph edges near an exact input coordinate; coverage/geometry review required." : "Valhalla rejected exact pair as unroutable.");
                        await SaveAsync();
                    }
                    finally { writer.Release(); }
                }
            }
        }
        void Store(OriginZone origin, CasinoCompetitor facility, double? minutes, double? distance, bool found, string? reason)
        {
            var row = new OriginFacilityTravel { OriginZoneId = origin.Id, CasinoCompetitorId = facility.Id, FacilityKey = facility.StableVenueId,
                FacilityKind = FacilityKinds.Incumbent, FacilityCoordinateHash = TravelMatrixService.CandidateCoordinateHash(facility.Latitude, facility.Longitude),
                FacilityLatitude = facility.Latitude, FacilityLongitude = facility.Longitude, RoutingGraphHash = graph.GraphHash, CostingProfile = "auto",
                TravelTimeMinutes = minutes, RoutedDistanceMeters = distance, RouteFound = found, RouteFailureReason = reason };
            result[(origin.Id, facility.Id)] = row; db.OriginFacilityTravel.Add(row); pending.Add(row);
        }
        async Task SaveAsync()
        {
            if (pending.Count == 0) return;
            await db.SaveChangesAsync(cancellationToken);
            foreach (var row in pending) db.Entry(row).State = EntityState.Detached;
            pending.Clear();
        }
    }

    private static GravityAlternativeInput Alternative(long originId, CasinoCompetitor facility, double mass, bool proposed,
        Dictionary<(long OriginId, int FacilityId), OriginFacilityTravel> routes)
    {
        var route = routes[(originId, facility.Id)];
        return new(facility.StableVenueId, mass, route.TravelTimeMinutes, route.RouteFound, IsProposedFacility: proposed);
    }
    private static double Median(IEnumerable<double> values) { var ordered = values.Order().ToArray(); return ordered.Length % 2 == 0 ? (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2 : ordered[ordered.Length / 2]; }
    private static double Wape(IEnumerable<ValidationObservation> values) { var rows = values.ToArray(); return 100 * rows.Sum(row => Math.Abs(row.Predicted - row.Observed)) / rows.Sum(row => row.Observed); }
    private static Dictionary<string, double> FeatureParameters(double positions, double tables, double positionsOffset)
    {
        var result = new Dictionary<string, double>();
        foreach (var (name, reference) in new[] { ("gaming_positions", 2000d), ("table_games", 50d), ("hotel_rooms", 250d),
                     ("gaming_floor_square_feet", 75000d), ("food_beverage_venues", 4d), ("entertainment_capacity", 1000d), ("capital_cost", 500000000d), ("highway_access", 1d) })
        {
            result[$"facility.reference_{name}"] = reference; result[$"facility.{name}_offset"] = reference;
        }
        result["facility.gaming_positions_offset"] = positionsOffset;
        foreach (var name in new[] { "gaming_positions", "table_games", "hotel_rooms", "gaming_floor", "food_beverage", "entertainment_capacity", "capital_scale", "highway_access" }) result[$"facility.{name}_coefficient"] = 0;
        result["facility.gaming_positions_coefficient"] = positions; result["facility.table_games_coefficient"] = tables;
        return result;
    }
    private sealed record PreparedCase(CasinoCompetitor Facility, string Partition, string Market, double Observed, OriginZone[] Origins, CasinoCompetitor[] Peers);
    private sealed record SearchRow(string Key, double Beta, double Alpha, double PositionsCoefficient, double PositionsOffset, double FloorCoefficient, double TableCoefficient, double TravelRegularizationMinutes, double IncomeElasticity, double Scale, double OutsideWeight,
        double FittedDemandScale, Dictionary<string, double>? FittedDemandScaleByJurisdiction, ValidationMetrics TrainingMetrics, ValidationMetrics SelectionMetrics, double SelectionWape,
        ValidationObservation[] Observations, DevelopmentFitReport? Admission);
}

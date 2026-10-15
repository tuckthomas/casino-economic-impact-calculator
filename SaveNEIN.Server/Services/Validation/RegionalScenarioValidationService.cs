// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;

namespace SaveNEIN.Server.Services.Validation;

public sealed record RegionalSnapshotReference(Guid Id, string Checksum);
public sealed record RegionalBenchmarkScenario(string Key, string CountyFips, double Latitude, double Longitude,
    string LocationBasis, int PopulationScenarioYear, string BenchmarkKey, string BenchmarkSourceChecksum,
    string? ResidentForecastPath, string TotalForecastPath);
public sealed record RegionalValidationProtocol(string Version, string Scope, string JurisdictionCode, string StateFips, string RoutingGraphHash,
    Dictionary<string, RegionalSnapshotReference> Snapshots, Dictionary<string, double> CommonOverrides,
    Dictionary<string, double> ExpectedParameters, DevelopmentProgramDefinition Program,
    RegionalBenchmarkScenario[] Scenarios, int PopulationObservationYear, int IncomeTaxYear, int ObservedYear,
    DateOnly FiscalDate, double OriginRadiusMiles, double CompetitorRadiusMiles, string DemandSpecification,
    string FacilityRegime = "commercial-casino");

/// <summary>Execute configured regional scenarios with frozen inputs and common assumptions.
/// Published study forecasts are comparisons, never calibration labels or acceptance observations.</summary>
public sealed class RegionalScenarioValidationService(AppDbContext db, IGravityModelExecutionService execution)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task RunAsync(string protocolPath, string outputPath, CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputPath)) throw new InvalidDataException("Regional result already exists.");
        var bytes = await File.ReadAllBytesAsync(protocolPath, cancellationToken);
        var protocol = JsonSerializer.Deserialize<RegionalValidationProtocol>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Regional protocol is missing.");
        ValidateProtocol(protocol);
        var protocolHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var inputIds = protocol.Snapshots.Values.Select(input => input.Id).ToArray();
        var snapshots = await db.DatasetSnapshots.AsNoTracking().Where(row => inputIds.Contains(row.Id)).ToArrayAsync(cancellationToken);
        if (snapshots.Length != inputIds.Length || protocol.Snapshots.Any(input => !snapshots.Any(row => row.Id == input.Value.Id &&
            row.DatasetKey == input.Key && row.Checksum == input.Value.Checksum && row.IsSealed && row.ValidationState != DatasetValidationStates.Rejected)))
            throw new InvalidDataException("Regional inputs do not match the declared sealed snapshot kinds/checksums.");
        var commonOverrides = protocol.CommonOverrides.Select(pair => new ParameterOverride(pair.Key, pair.Value)).ToArray();
        var parameters = await new ModelParameterService(db).ResolveAsync(new("gravity-v1", null, null, null, commonOverrides), cancellationToken);
        if (parameters.Count != protocol.ExpectedParameters.Count || parameters.Any(parameter =>
            !protocol.ExpectedParameters.TryGetValue(parameter.Definition.Key, out var expected) || parameter.FinalValue != expected))
            throw new InvalidDataException("Resolved parameters drifted from the frozen regional protocol.");

        var reader = new BenchmarkOutputReader();
        var prepared = new List<(RegionalBenchmarkScenario Scenario, BenchmarkStudy Study, decimal? Resident, decimal Total)>();
        foreach (var scenario in protocol.Scenarios)
        {
            var study = await db.BenchmarkStudies.AsNoTracking().SingleAsync(row => row.BenchmarkKey == scenario.BenchmarkKey, cancellationToken);
            if (!string.Equals(study.SourceFileChecksum, scenario.BenchmarkSourceChecksum, StringComparison.OrdinalIgnoreCase) ||
                study.ValidationState is not (BenchmarkValidationStates.Extracted or BenchmarkValidationStates.Validated))
                throw new InvalidDataException("Benchmark source/state does not match the regional protocol.");
            var location = await new CandidateFiscalLocationResolver(db).ResolveAsync(scenario.Latitude, scenario.Longitude, cancellationToken);
            if (location.CountyFips != scenario.CountyFips || location.StateFips != protocol.StateFips)
                throw new InvalidDataException($"Scenario {scenario.Key} does not lie in its declared state/county.");
            var resident = string.IsNullOrWhiteSpace(scenario.ResidentForecastPath) ? null : reader.ReadMonetaryMetric(study.ReportedOutputsJson, scenario.ResidentForecastPath);
            var total = reader.ReadMonetaryMetric(study.ReportedOutputsJson, scenario.TotalForecastPath);
            if (total.Currency != "USD" || total.Value <= 0 || (resident is not null &&
                (resident.Currency != "USD" || resident.Value <= 0 || total.Value < resident.Value)))
                throw new InvalidDataException("Regional forecast comparisons require positive, consistent USD components.");
            prepared.Add((scenario, study, resident?.Value, total.Value));
        }

        var origins = await db.OriginZones.AsNoTracking().Where(row => row.DatasetSnapshotId == protocol.Snapshots[DatasetSnapshotKinds.OriginGeography].Id)
            .OrderBy(row => row.StableOriginId).ToArrayAsync(cancellationToken);
        var selectedOrigins = prepared.ToDictionary(item => item.Scenario.Key, item => origins.Where(origin =>
            CompetitiveUniverseService.IsWithinBroadPrefilter(origin.RepresentativePoint.Y, origin.RepresentativePoint.X,
                item.Scenario.Latitude, item.Scenario.Longitude, protocol.OriginRadiusMiles)).ToArray());
        if (selectedOrigins.Any(selection => selection.Value.Length == 0 || !selection.Value.Any(origin => origin.CountyEquivalentCode ==
            prepared.Single(item => item.Scenario.Key == selection.Key).Scenario.CountyFips)))
            throw new InvalidDataException("Regional origin selection lacks the configured host county.");

        var programDefinition = protocol.Program with { StableProgramId = "regional-validation-" + protocolHash[..16], Version = "1" };
        var program = await db.DevelopmentPrograms.AsNoTracking().SingleOrDefaultAsync(row => row.StableProgramId == programDefinition.StableProgramId && row.Version == "1", cancellationToken)
            ?? await new DevelopmentProgramService(db).CreateAsync(programDefinition, cancellationToken);
        var results = new List<object>();
        var budgetsByOriginAndYear = new Dictionary<(string Origin, int Year), decimal>();
        var budgetChecks = 0;
        var maximumBudgetDifference = 0m;
        foreach (var item in prepared)
        {
            var scenario = item.Scenario;
            var selected = selectedOrigins[scenario.Key];
            var request = new GravityModelRunRequest(scenario.Key, protocol.JurisdictionCode, program.Id, scenario.Latitude, scenario.Longitude,
                protocol.Snapshots[DatasetSnapshotKinds.OriginGeography].Id, protocol.Snapshots[DatasetSnapshotKinds.AgePopulation].Id,
                protocol.Snapshots[DatasetSnapshotKinds.Income].Id, protocol.Snapshots[DatasetSnapshotKinds.Competitors].Id,
                protocol.Snapshots[DatasetSnapshotKinds.ObservedPerformance].Id, selected.Select(origin => origin.StableOriginId).ToArray(), [],
                protocol.PopulationObservationYear, protocol.IncomeTaxYear, protocol.FiscalDate, protocol.FacilityRegime, protocol.DemandSpecification,
                FacilityAttractionSpecifications.HybridObservedGgr, "inverse-power", GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue,
                new(protocol.ObservedYear, 1, 1), new(protocol.ObservedYear, 12, 31), null, null, null, commonOverrides,
                CompetitorPrefilterMiles: protocol.CompetitorRadiusMiles, ImpactGeography: new(ImpactScopeKinds.HostCounty, scenario.CountyFips),
                MissingRoutePolicy: MissingRoutePolicies.ExcludeFacility, PopulationScenarioYear: scenario.PopulationScenarioYear);
            Console.WriteLine($"Executing regional case {scenario.Key}: {selected.Length} origins, county {scenario.CountyFips}, population year {scenario.PopulationScenarioYear}.");
            var result = await execution.ExecuteAsync(request, cancellationToken);
            if (result.RoutingGraphHash != protocol.RoutingGraphHash || result.Status != ModelRunStatuses.Finalized)
                throw new InvalidDataException("Regional run used a different graph or did not finalize.");
            var storedParameters = await (from value in db.ModelRunParameterValues.AsNoTracking()
                                          join definition in db.ModelParameterDefinitions.AsNoTracking() on value.ParameterDefinitionId equals definition.Id
                                          where value.ModelRunId == result.ModelRunId
                                          select new { ParameterKey = definition.Key, value.FinalValue }).ToArrayAsync(cancellationToken);
            if (storedParameters.Length != protocol.ExpectedParameters.Count || storedParameters.Any(row =>
                !protocol.ExpectedParameters.TryGetValue(row.ParameterKey, out var expected) || row.FinalValue != expected))
                throw new InvalidDataException("Saved regional parameters differ from the frozen common assumptions.");
            var originResults = await db.ModelRunOriginResults.AsNoTracking().Where(row => row.ModelRunId == result.ModelRunId).ToArrayAsync(cancellationToken);
            var populations = originResults.ToDictionary(row => row.OriginZoneId);
            if (originResults.Length != selected.Length || originResults.Any(row => row.EligiblePopulation is null || !double.IsFinite(row.EligiblePopulation.Value)))
                throw new InvalidDataException("Regional run omitted eligible origin populations.");
            foreach (var origin in selected)
            {
                var growth = protocol.ExpectedParameters["demographics.population_annual_growth_rate"];
                var key = (origin.StableOriginId, growth == 0 ? 0 : scenario.PopulationScenarioYear);
                var budget = populations[origin.Id].ResidentDemand;
                if (budgetsByOriginAndYear.TryGetValue(key, out var previousBudget))
                {
                    budgetChecks++;
                    maximumBudgetDifference = Math.Max(maximumBudgetDifference, Math.Abs(previousBudget - budget));
                }
                else budgetsByOriginAndYear.Add(key, budget);
            }
            if (protocol.ExpectedParameters.GetValueOrDefault("demand.reference_income") > 0 && maximumBudgetDifference > 0)
                throw new InvalidDataException("A shared regional reference did not preserve overlapping origin budgets.");
            results.Add(new
            {
                scenario.Key, scenario.CountyFips, scenario.Latitude, scenario.Longitude, scenario.LocationBasis, scenario.PopulationScenarioYear,
                result.ModelRunId, result.Status, result.OriginCount, result.IncumbentCount, result.TotalResidentDemand,
                result.ProposedResidentGgr, result.ProposedInducedResidentGgr, result.TourismGgr, result.TrafficGgr, result.StabilizedTotalGgr,
                Population = originResults.Sum(row => row.EligiblePopulation!.Value),
                OriginStates = selected.GroupBy(origin => origin.StateOrTerritoryCode).OrderBy(group => group.Key).Select(group => new
                {
                    State = group.Key, Origins = group.Count(), EligiblePopulation = group.Sum(origin => populations[origin.Id].EligiblePopulation!.Value),
                    ProposedResidentGgr = group.Sum(origin => populations[origin.Id].TotalProposedResidentGgr)
                }),
                Benchmark = new { item.Study.BenchmarkKey, item.Study.SourceUrl, item.Study.SourceFileChecksum,
                    ResidentForecast = item.Resident, TotalForecast = item.Total,
                    ResidentDifferencePercent = item.Resident is > 0 ? 100m * (result.ProposedResidentGgr - item.Resident) / item.Resident : null,
                    TotalDifferencePercent = 100m * (result.StabilizedTotalGgr - item.Total) / item.Total },
                result.Warnings
            });
            await SaveAsync(); // Retain each completed case even if a subsequent run fails.
            Console.WriteLine($"Saved {scenario.Key}: resident GGR {result.ProposedResidentGgr:F2}; total benchmark difference {100m * (result.StabilizedTotalGgr - item.Total) / item.Total:F2}%.");
        }

        async Task SaveAsync() => await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new
        {
            ProtocolHash = protocolHash, protocol.Version, protocol.Scope, protocol.RoutingGraphHash,
            protocol.Snapshots, protocol.ExpectedParameters, ScenarioResults = results, CompletedScenarios = results.Count,
            ExpectedScenarios = protocol.Scenarios.Length, ReleaseAccepted = false,
            SharedOriginBudgetChecks = budgetChecks, MaximumSharedOriginBudgetDifference = maximumBudgetDifference,
            FixedRegionalReference = protocol.ExpectedParameters.GetValueOrDefault("demand.reference_income") > 0,
            Limitations = new[] { "Study values are forecasts, not independent observed casino revenues.",
                "Development program and proxy sites are explicitly assumed; no parcel precision is claimed.",
                "Site-addressable tourism/traffic are not supplied; no statewide totals are converted into site demand.",
                "Regional population coverage, competition completeness, calibrated defaults and causal economic-input acceptance remain independent requirements." }
        }, JsonOptions), cancellationToken);
    }

    internal static void ValidateProtocol(RegionalValidationProtocol protocol)
    {
        var required = new[] { DatasetSnapshotKinds.OriginGeography, DatasetSnapshotKinds.AgePopulation, DatasetSnapshotKinds.Income,
            DatasetSnapshotKinds.Competitors, DatasetSnapshotKinds.ObservedPerformance };
        if (string.IsNullOrWhiteSpace(protocol.Version) || string.IsNullOrWhiteSpace(protocol.Scope) ||
            string.IsNullOrWhiteSpace(protocol.JurisdictionCode) || string.IsNullOrWhiteSpace(protocol.FacilityRegime) ||
            protocol.StateFips?.Length != 2 || !protocol.StateFips.All(char.IsAsciiDigit) || protocol.RoutingGraphHash?.Length != 64 ||
            protocol.Snapshots is null || !required.Order().SequenceEqual(protocol.Snapshots.Keys.Order()) ||
            protocol.Snapshots.Values.Any(input => input.Id == Guid.Empty || input.Checksum?.Length != 64) ||
            protocol.Snapshots.Values.Select(input => input.Id).Distinct().Count() != required.Length)
            throw new InvalidDataException("Regional protocol requires five distinct explicit snapshot identities/checksums and graph provenance.");
        if (protocol.Scenarios is not { Length: > 0 } || protocol.Scenarios.Select(item => item.Key).Distinct().Count() != protocol.Scenarios.Length ||
            protocol.Scenarios.Any(item => string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.LocationBasis) ||
                item.CountyFips?.Length != 5 || !item.CountyFips.StartsWith(protocol.StateFips, StringComparison.Ordinal) || !item.CountyFips.All(char.IsAsciiDigit) ||
                !double.IsFinite(item.Latitude) || !double.IsFinite(item.Longitude) || item.Latitude is < -90 or > 90 || item.Longitude is < -180 or > 180 ||
                item.PopulationScenarioYear < protocol.PopulationObservationYear || string.IsNullOrWhiteSpace(item.BenchmarkKey) ||
                item.BenchmarkSourceChecksum?.Length != 64 || string.IsNullOrWhiteSpace(item.TotalForecastPath)))
            throw new InvalidDataException("Regional scenarios require explicit locations, matching state/county codes, source checksums and forecast components.");
        if (protocol.CommonOverrides is null || protocol.ExpectedParameters is not { Count: > 0 } ||
            protocol.CommonOverrides.Values.Concat(protocol.ExpectedParameters.Values).Any(value => !double.IsFinite(value)) ||
            !double.IsFinite(protocol.OriginRadiusMiles) || protocol.OriginRadiusMiles <= 0 ||
            !double.IsFinite(protocol.CompetitorRadiusMiles) || protocol.CompetitorRadiusMiles <= 0 ||
            protocol.DemandSpecification is not (GravityDemandSpecifications.AgiShare or GravityDemandSpecifications.EligibleAdultPerCapita))
            throw new InvalidDataException("Regional scenarios require frozen common parameters and supported finite scope/demand assumptions.");
    }
}

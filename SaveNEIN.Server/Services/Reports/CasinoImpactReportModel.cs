// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Reports;

public sealed record ReportPresentationOptions(
    string? Title = null,
    string? PreparedFor = null,
    int TopOriginCount = 20,
    string CurrencyCode = "USD",
    Guid? SensitivityAnalysisId = null);

public sealed record CasinoImpactReportModel(
    ReportIdentity Identity,
    ReportScenario Scenario,
    ReportDevelopmentProgram DevelopmentProgram,
    ReportRevenueSummary Revenue,
    IReadOnlyList<ReportDatasetSource> DataSources,
    IReadOnlyList<ReportParameter> Parameters,
    IReadOnlyList<ReportOrigin> Origins,
    IReadOnlyList<ReportOriginGroup> OriginStates,
    IReadOnlyList<ReportOriginGroup> OriginCounties,
    IReadOnlyList<ReportFacility> Facilities,
    IReadOnlyList<ReportDemandComponent> DemandComponents,
    ReportCapacity? Capacity,
    IReadOnlyList<ReportRampYear> Ramp,
    ReportGeographicAccounting? GeographicAccounting,
    IReadOnlyList<ReportSectorDisplacement> SectorDisplacement,
    ReportEmployment? Employment,
    ReportFiscal? Fiscal,
    IReadOnlyList<ReportSocialCost> SocialCosts,
    ReportNetImpact? NetImpact,
    ReportSensitivityAnalysis? Sensitivity,
    IReadOnlyList<ReportBenchmarkReconciliation> Benchmarks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Limitations);

public sealed record ReportIdentity(
    Guid ModelRunId,
    string ModelVersion,
    string TemplateVersion,
    DateTime GeneratedAtUtc,
    DateTime RunCreatedAtUtc,
    DateTime RunFinalizedAtUtc,
    string JurisdictionCode,
    string JurisdictionName,
    string JurisdictionProfileVersion,
    IReadOnlyList<string> RoutingGraphHashes,
    IReadOnlyList<string> CostingProfiles);

public sealed record ReportScenario(
    string Name,
    double CandidateLatitude,
    double CandidateLongitude,
    string DemandSpecification,
    string AttractionSpecification,
    string FrictionForm,
    string ComputationalOriginType,
    string ImpactScopeKind,
    string ImpactScopeCode,
    IReadOnlyList<string> ParameterSets);

public sealed record ReportDevelopmentProgram(
    Guid Id,
    string StableProgramId,
    string Version,
    string Name,
    int SlotOrVltPositions,
    int TableGameCount,
    int PokerTableCount,
    bool HasSportsbook,
    int HotelRoomCount,
    int GamingFloorSquareFeet,
    int FoodBeverageVenueCount,
    int EventCapacity,
    int ResortAmenityCount,
    decimal? CapitalCost,
    int? CapitalCostDollarYear,
    DateOnly? PlannedOpeningDate,
    int StabilizedYearNumber);

public sealed record ReportRevenueSummary(
    decimal TotalResidentDemand,
    decimal RedistributedResidentGgr,
    decimal InducedResidentGgr,
    decimal TotalResidentGgr,
    decimal TourismGgr,
    decimal TrafficGgr,
    decimal StabilizedTotalGgr);

public sealed record ReportDatasetSource(
    string Role,
    string ReferenceKey,
    string DatasetKey,
    string Period,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    string ValidationState,
    string Checksum,
    string TransformVersion,
    string SourceName,
    string SourceUrl,
    string SourceContentHash);

public sealed record ReportParameter(
    string Key,
    string Category,
    string DisplayName,
    string Units,
    double SystemFallbackValue,
    double DefaultValue,
    double? ScenarioValue,
    double? UserOverrideValue,
    double FinalValue,
    string SourceLayer,
    double? RecommendedMinimum,
    double? RecommendedMaximum,
    bool IsOutsideRecommendedRange,
    string? WarningText,
    string? ProvenanceNotes);

public sealed record ReportOrigin(
    string StableOriginId,
    string OriginType,
    string GeographyCode,
    string StateCode,
    string? CountyCode,
    string? MsaCode,
    double Latitude,
    double Longitude,
    decimal ResidentDemand,
    decimal RedistributedResidentGgr,
    decimal InducedResidentGgr,
    decimal TotalProposedResidentGgr,
    decimal HostJurisdictionCapture,
    decimal ExternalJurisdictionCapture,
    decimal TribalOrOtherJurisdictionCapture,
    decimal OutsideOptionCapture,
    double ShareOfProposedResidentGgr);

public sealed record ReportOriginGroup(
    string GeographyType,
    string GeographyCode,
    decimal RedistributedResidentGgr,
    decimal InducedResidentGgr,
    decimal TotalProposedResidentGgr,
    double ShareOfProposedResidentGgr,
    int OriginCount);

public sealed record ReportFacility(
    string FacilityKey,
    string FacilityName,
    string FacilityKind,
    bool IsProposedFacility,
    double Latitude,
    double Longitude,
    double NormalizedAttraction,
    decimal BaselineResidentGgr,
    decimal WithProjectResidentGgr,
    decimal ChangeInResidentGgr,
    decimal InducedResidentGgr,
    decimal TotalWithProjectResidentGgr,
    decimal TourismGgr,
    decimal TrafficGgr,
    decimal StabilizedTotalGgr);

public sealed record ReportDemandComponent(
    string ComponentType,
    string SourceRecordKey,
    decimal InputQuantity,
    decimal DeduplicatedQuantity,
    decimal EligibleQuantity,
    decimal ParticipatingQuantity,
    decimal CapturedQuantity,
    decimal Ggr,
    string DetailsJson);

public sealed record ReportCapacity(
    string FacilityKey,
    decimal StabilizedGgr,
    decimal? PlausibleCapacityMinimum,
    decimal? PlausibleCapacityMaximum,
    bool IsBelowValidatedRange,
    bool IsAboveValidatedRange,
    string Status,
    string? WarningText);

public sealed record ReportRampYear(
    int CalendarYear,
    int OperatingYearNumber,
    string PeriodKind,
    double OperatingYearFraction,
    double StabilizationShare,
    decimal ProjectedGgr);

public sealed record ReportGeographicAccounting(
    string ScopeKind,
    string ScopeCode,
    int LocalOriginCount,
    decimal HostJurisdictionCannibalization,
    decimal CrossJurisdictionCapture,
    decimal OutsideOrUnmodeledLeakageCapture,
    decimal InducedResidentGgr,
    decimal TourismGgr,
    decimal TrafficGgr,
    decimal TransferEffectGgr,
    decimal MarketExpansionAndImportGgr,
    decimal StabilizedGgr,
    decimal LocalResidentGamingBase,
    decimal ExcludedLocalCasinoCannibalization,
    decimal ExcludedRepatriatedOrLeakedResidentGgr,
    decimal RemainingLocalResidentGamingBase);

public sealed record ReportSectorDisplacement(
    string SectorKey,
    double NormalizedWeight,
    decimal DisplacementEligibleBase,
    double DisplacementCoefficient,
    decimal DisplacedSales,
    decimal DisplacedTaxableSales,
    decimal DisplacedBusinessIncome,
    decimal SalesTaxLoss,
    decimal BusinessIncomeTaxLoss,
    double DisplacedJobs);

public sealed record ReportEmployment(
    double DirectCasinoJobs,
    double ConstructionJobYears,
    double IndirectAndInducedJobs,
    double DisplacedSectorJobs,
    double IncumbentCasinoJobsLost,
    double NetPermanentJobs,
    decimal DirectLaborIncome,
    decimal IndirectLaborIncome,
    decimal IncumbentLaborIncomeLost);

public sealed record ReportFiscal(
    decimal GrossGamingTax,
    decimal HostLocalGrossPublicRevenue,
    decimal HostStateGrossPublicRevenue,
    decimal DisplacedLocalFiscalLoss,
    decimal HostIncumbentGamingTaxLoss,
    decimal OtherJurisdictionGamingTaxLoss,
    decimal NetHostLocalFiscalImpact,
    decimal NetHostStateFiscalImpact,
    decimal OtherJurisdictionFiscalImpact,
    string RuleProvenanceJson);

public sealed record ReportSocialCost(
    string DomainKey,
    double ExposedEligiblePopulation,
    double IncrementalCases,
    decimal PerCaseCost,
    decimal AnnualCost,
    decimal LowAnnualCost,
    decimal HighAnnualCost,
    bool Included,
    string? ProvenanceNotes);

public sealed record ReportNetImpact(
    decimal GrossPropertyGgr,
    decimal TransferEffectGgr,
    decimal CrossJurisdictionImportedGgr,
    decimal OutsideOrUnmodeledLeakageCapture,
    decimal InducedResidentGgr,
    decimal TourismAndTrafficImportGgr,
    decimal LocalDiscretionaryDisplacement,
    decimal DirectAndIndirectLaborIncome,
    decimal NetHostLocalFiscalImpact,
    decimal NetHostStateFiscalImpact,
    decimal GrossSocialCost,
    decimal NetNewLocalGamingActivity,
    decimal NetHostLocalImpact,
    decimal NetHostStateImpact,
    string AccountingMethodKey);

public sealed record ReportBenchmarkReconciliation(
    string CaseKey,
    string CaseName,
    string MarketCode,
    string DatasetPartition,
    decimal ObservedRevenue,
    decimal PredictedRevenue,
    decimal Residual,
    string? BenchmarkTitle,
    string? ConsultantOrSource,
    string? SourceUrl);

public sealed record ReportSensitivityAnalysis(
    Guid SensitivityAnalysisId,
    string AnalysisKey,
    string Version,
    string Name,
    string OutputMetric,
    string OutputUnits,
    Guid BaselineModelRunId,
    decimal BaselineMetricValue,
    IReadOnlyList<ReportSensitivityRow> Rows);

public sealed record ReportSensitivityRow(
    string ParameterKey,
    double LowParameterValue,
    double BaseParameterValue,
    double HighParameterValue,
    Guid LowModelRunId,
    Guid HighModelRunId,
    decimal LowMetricValue,
    decimal BaseMetricValue,
    decimal HighMetricValue,
    decimal LowDelta,
    decimal HighDelta,
    decimal TotalRange);

public interface ICasinoImpactReportModelFactory
{
    Task<CasinoImpactReportModel> BuildAsync(
        Guid modelRunId,
        string templateVersion,
        DateTime generatedAtUtc,
        ReportPresentationOptions options,
        CancellationToken cancellationToken = default);
}

public sealed class CasinoImpactReportModelFactory(AppDbContext db) : ICasinoImpactReportModelFactory
{
    public async Task<CasinoImpactReportModel> BuildAsync(
        Guid modelRunId,
        string templateVersion,
        DateTime generatedAtUtc,
        ReportPresentationOptions options,
        CancellationToken cancellationToken = default)
    {
        var run = await db.ModelRuns.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == modelRunId, cancellationToken)
            ?? throw new KeyNotFoundException($"Model run '{modelRunId}' was not found.");
        if (run.Status != ModelRunStatuses.Finalized || run.FinalizedAtUtc is null)
        {
            throw new InvalidOperationException("Only finalized model runs can generate full reports.");
        }
        var jurisdiction = await db.Jurisdictions.AsNoTracking()
            .SingleAsync(item => item.Id == run.JurisdictionId, cancellationToken);
        var jurisdictionProfileVersion = await BuildJurisdictionProfileVersionAsync(
            jurisdiction,
            DateOnly.FromDateTime(run.FinalizedAtUtc.Value),
            cancellationToken);
        var program = await db.DevelopmentPrograms.AsNoTracking()
            .SingleAsync(item => item.Id == run.DevelopmentProgramId, cancellationToken);
        var proposed = await db.ModelRunFacilityResults.AsNoTracking()
            .SingleAsync(item => item.ModelRunId == modelRunId && item.IsProposedFacility, cancellationToken);
        var originRows = await db.ModelRunOriginResults.AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .Join(
                db.OriginZones.AsNoTracking(),
                result => result.OriginZoneId,
                origin => origin.Id,
                (result, origin) => new { result, origin })
            .OrderByDescending(item => item.result.TotalProposedResidentGgr)
            .ThenBy(item => item.origin.StableOriginId)
            .ToListAsync(cancellationToken);
        var residentTotal = originRows.Sum(item => item.result.TotalProposedResidentGgr);
        var origins = originRows.Select(item => new ReportOrigin(
            item.origin.StableOriginId,
            item.origin.OriginType,
            item.origin.GeographyCode,
            item.origin.StateOrTerritoryCode ?? "unassigned",
            item.origin.CountyEquivalentCode,
            item.origin.MetropolitanStatisticalAreaCode,
            item.origin.RepresentativePoint.Y,
            item.origin.RepresentativePoint.X,
            item.result.ResidentDemand,
            item.result.ProposedResidentGgr,
            item.result.ProposedInducedResidentGgr,
            item.result.TotalProposedResidentGgr,
            item.result.HostJurisdictionCapture,
            item.result.ExternalJurisdictionCapture,
            item.result.TribalOrOtherJurisdictionCapture,
            item.result.OutsideOptionCapture,
            Share(item.result.TotalProposedResidentGgr, residentTotal))).ToArray();

        var geographicAccountingEntity = await db.ModelRunGeographicAccounting.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ModelRunId == modelRunId, cancellationToken);
        var scenarioValues = ParseResolvedInput(run.ResolvedInputJson);
        var parameterSets = await db.ModelRunParameterSetReferences.AsNoTracking()
            .Where(reference => reference.ModelRunId == modelRunId)
            .Join(
                db.ModelParameterSets.AsNoTracking(),
                reference => reference.ParameterSetId,
                set => set.Id,
                (reference, set) => reference.SourceLayer + ":" + set.Key + "@" + set.Version)
            .OrderBy(value => value)
            .ToListAsync(cancellationToken);
        var routeMetadata = await db.OriginFacilityTravel.AsNoTracking()
            .Where(route => route.ModelRunId == modelRunId)
            .Select(route => new { route.RoutingGraphHash, route.CostingProfile })
            .Distinct()
            .ToListAsync(cancellationToken);
        var parameters = await db.ModelRunParameterValues.AsNoTracking()
            .Where(value => value.ModelRunId == modelRunId)
            .Join(
                db.ModelParameterDefinitions.AsNoTracking(),
                value => value.ParameterDefinitionId,
                definition => definition.Id,
                (value, definition) => new { value, definition })
            .OrderBy(item => item.definition.Category)
            .ThenBy(item => item.definition.DisplayName)
            .Select(item => new ReportParameter(
                item.definition.Key,
                item.definition.Category,
                item.definition.DisplayName,
                item.definition.Units,
                item.value.SystemFallbackValue,
                item.value.DefaultValue,
                item.value.ScenarioValue,
                item.value.UserOverrideValue,
                item.value.FinalValue,
                item.value.SourceLayer,
                item.definition.RecommendedMinimum,
                item.definition.RecommendedMaximum,
                item.value.IsOutsideRecommendedRange,
                item.value.WarningText,
                item.definition.ProvenanceNotes))
            .ToListAsync(cancellationToken);
        var dataSources = await db.ModelRunDatasetSnapshotReferences.AsNoTracking()
            .Where(reference => reference.ModelRunId == modelRunId)
            .Join(db.DatasetSnapshots.AsNoTracking(), reference => reference.DatasetSnapshotId, snapshot => snapshot.Id,
                (reference, snapshot) => new { reference, snapshot })
            .Join(db.DataSources.AsNoTracking(), item => item.snapshot.DataSourceId, source => source.Id,
                (item, source) => new { item.reference, item.snapshot, source })
            .OrderBy(item => item.reference.Role)
            .ThenBy(item => item.reference.ReferenceKey)
            .Select(item => new ReportDatasetSource(
                item.reference.Role,
                item.reference.ReferenceKey,
                item.snapshot.DatasetKey,
                item.snapshot.Period,
                item.snapshot.PeriodStart,
                item.snapshot.PeriodEnd,
                item.snapshot.ValidationState,
                item.snapshot.Checksum,
                item.snapshot.TransformVersion,
                item.source.Name,
                item.source.Url,
                item.source.ContentHash))
            .ToListAsync(cancellationToken);
        var facilities = await db.ModelRunFacilityResults.AsNoTracking()
            .Where(result => result.ModelRunId == modelRunId)
            .GroupJoin(
                db.CasinoCompetitors.AsNoTracking(),
                result => result.CasinoCompetitorId,
                competitor => competitor.Id,
                (result, competitors) => new { result, competitor = competitors.FirstOrDefault() })
            .OrderByDescending(item => item.result.IsProposedFacility)
            .ThenBy(item => item.result.FacilityKey)
            .Select(item => new ReportFacility(
                item.result.FacilityKey,
                item.result.IsProposedFacility ? program.Name : item.competitor!.Name,
                item.result.FacilityKind,
                item.result.IsProposedFacility,
                item.result.IsProposedFacility ? run.CandidateLatitude : item.competitor!.Latitude,
                item.result.IsProposedFacility ? run.CandidateLongitude : item.competitor!.Longitude,
                item.result.NormalizedAttraction,
                item.result.BaselineResidentGgr,
                item.result.WithProjectResidentGgr,
                item.result.ChangeInResidentGgr,
                item.result.InducedResidentGgr,
                item.result.TotalWithProjectResidentGgr,
                item.result.TourismGgr,
                item.result.TrafficGgr,
                item.result.StabilizedTotalGgr))
            .ToListAsync(cancellationToken);
        var demandComponents = await db.ModelRunDemandComponents.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .OrderBy(item => item.ComponentType)
            .ThenBy(item => item.SourceRecordKey)
            .Select(item => new ReportDemandComponent(
                item.ComponentType,
                item.SourceRecordKey,
                item.InputQuantity,
                item.DeduplicatedQuantity,
                item.EligibleQuantity,
                item.ParticipatingQuantity,
                item.CapturedQuantity,
                item.Ggr,
                item.DetailsJson))
            .ToListAsync(cancellationToken);
        var capacity = await db.ModelRunCapacityDiagnostics.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .Select(item => new ReportCapacity(
                item.FacilityKey,
                item.StabilizedGgr,
                item.PlausibleCapacityMinimum,
                item.PlausibleCapacityMaximum,
                item.IsBelowValidatedRange,
                item.IsAboveValidatedRange,
                item.Status,
                item.WarningText))
            .SingleOrDefaultAsync(cancellationToken);
        var ramp = await db.ModelRunRampResults.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .OrderBy(item => item.CalendarYear)
            .Select(item => new ReportRampYear(
                item.CalendarYear,
                item.OperatingYearNumber,
                item.PeriodKind,
                item.OperatingYearFraction,
                item.StabilizationShare,
                item.ProjectedGgr))
            .ToListAsync(cancellationToken);
        var sectors = await db.ModelRunSectorDisplacement.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .OrderByDescending(item => item.DisplacedSales)
            .Select(item => new ReportSectorDisplacement(
                item.SectorKey,
                item.NormalizedWeight,
                item.DisplacementEligibleBase,
                item.DisplacementCoefficient,
                item.DisplacedSales,
                item.DisplacedTaxableSales,
                item.DisplacedBusinessIncome,
                item.SalesTaxLoss,
                item.BusinessIncomeTaxLoss,
                item.DisplacedJobs))
            .ToListAsync(cancellationToken);
        var employment = await db.ModelRunEmploymentImpacts.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .Select(item => new ReportEmployment(
                item.DirectCasinoJobs,
                item.ConstructionJobYears,
                item.IndirectAndInducedJobs,
                item.DisplacedSectorJobs,
                item.IncumbentCasinoJobsLost,
                item.NetPermanentJobs,
                item.DirectLaborIncome,
                item.IndirectLaborIncome,
                item.IncumbentLaborIncomeLost))
            .SingleOrDefaultAsync(cancellationToken);
        var fiscal = await db.ModelRunFiscalImpacts.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .Select(item => new ReportFiscal(
                item.GrossGamingTax,
                item.HostLocalGrossPublicRevenue,
                item.HostStateGrossPublicRevenue,
                item.DisplacedLocalFiscalLoss,
                item.HostIncumbentGamingTaxLoss,
                item.OtherJurisdictionGamingTaxLoss,
                item.NetHostLocalFiscalImpact,
                item.NetHostStateFiscalImpact,
                item.OtherJurisdictionFiscalImpact,
                item.RuleProvenanceJson))
            .SingleOrDefaultAsync(cancellationToken);
        var socialCosts = await db.ModelRunSocialCosts.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .OrderByDescending(item => item.AnnualCost)
            .Select(item => new ReportSocialCost(
                item.DomainKey,
                item.ExposedEligiblePopulation,
                item.IncrementalCases,
                item.PerCaseCost,
                item.AnnualCost,
                item.LowAnnualCost,
                item.HighAnnualCost,
                item.Included,
                item.ProvenanceNotes))
            .ToListAsync(cancellationToken);
        var net = await db.ModelRunNetImpacts.AsNoTracking()
            .Where(item => item.ModelRunId == modelRunId)
            .Select(item => new ReportNetImpact(
                item.GrossPropertyGgr,
                item.TransferEffectGgr,
                item.CrossJurisdictionImportedGgr,
                item.OutsideOrUnmodeledLeakageCapture,
                item.InducedResidentGgr,
                item.TourismAndTrafficImportGgr,
                item.LocalDiscretionaryDisplacement,
                item.DirectAndIndirectLaborIncome,
                item.NetHostLocalFiscalImpact,
                item.NetHostStateFiscalImpact,
                item.GrossSocialCost,
                item.NetNewLocalGamingActivity,
                item.NetHostLocalImpact,
                item.NetHostStateImpact,
                item.AccountingMethodKey))
            .SingleOrDefaultAsync(cancellationToken);
        var benchmarks = await db.ValidationCases.AsNoTracking()
            .Where(validationCase => validationCase.ModelRunId == modelRunId)
            .GroupJoin(
                db.BenchmarkStudies.AsNoTracking(),
                validationCase => validationCase.BenchmarkStudyId,
                study => study.Id,
                (validationCase, studies) => new { validationCase, study = studies.FirstOrDefault() })
            .OrderBy(item => item.validationCase.CaseKey)
            .Select(item => new ReportBenchmarkReconciliation(
                item.validationCase.CaseKey,
                item.validationCase.Name,
                item.validationCase.MarketCode,
                item.validationCase.DatasetPartition,
                item.validationCase.ObservedRevenue,
                proposed.StabilizedTotalGgr,
                proposed.StabilizedTotalGgr - item.validationCase.ObservedRevenue,
                item.study == null ? null : item.study.Title,
                item.study == null ? null : item.study.ConsultantOrSource,
                item.study == null ? null : item.study.SourceUrl))
            .ToListAsync(cancellationToken);

        ReportSensitivityAnalysis? sensitivity = null;
        if (options.SensitivityAnalysisId is { } sensitivityAnalysisId)
        {
            var analysis = await db.SensitivityAnalyses.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == sensitivityAnalysisId, cancellationToken)
                ?? throw new KeyNotFoundException($"Sensitivity analysis '{sensitivityAnalysisId}' was not found.");
            if (analysis.Status != SensitivityAnalysisStatuses.Finalized || !analysis.IsImmutable ||
                analysis.BaselineModelRunId != modelRunId)
            {
                throw new InvalidOperationException(
                    "A report sensitivity exhibit must reference a finalized immutable analysis whose baseline is the report run.");
            }
            var pointEntities = await db.SensitivityAnalysisPoints.AsNoTracking()
                .Where(point => point.SensitivityAnalysisId == sensitivityAnalysisId)
                .OrderBy(point => point.ParameterKey)
                .ThenBy(point => point.Direction)
                .ToListAsync(cancellationToken);
            var parameterBaseValues = parameters.ToDictionary(parameter => parameter.Key, parameter => parameter.FinalValue, StringComparer.Ordinal);
            var rows = pointEntities.GroupBy(point => point.ParameterKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    if (group.Count() != 2 || !parameterBaseValues.TryGetValue(group.Key, out var baseValue))
                    {
                        throw new InvalidOperationException($"Sensitivity parameter '{group.Key}' does not have a complete low/base/high report series.");
                    }
                    var low = group.Single(point => point.Direction == "low");
                    var high = group.Single(point => point.Direction == "high");
                    return new ReportSensitivityRow(
                        group.Key,
                        low.ParameterValue,
                        baseValue,
                        high.ParameterValue,
                        low.ModelRunId,
                        high.ModelRunId,
                        low.OutputMetricValue,
                        analysis.BaselineMetricValue,
                        high.OutputMetricValue,
                        low.DeltaFromBaseline,
                        high.DeltaFromBaseline,
                        Math.Abs(high.OutputMetricValue - low.OutputMetricValue));
                })
                .OrderByDescending(row => row.TotalRange)
                .ThenBy(row => row.ParameterKey, StringComparer.Ordinal)
                .ToArray();
            sensitivity = new ReportSensitivityAnalysis(
                analysis.Id,
                analysis.AnalysisKey,
                analysis.Version,
                analysis.Name,
                analysis.OutputMetric,
                analysis.OutputMetric == SensitivityOutputMetrics.NetPermanentJobs ? "jobs" : "USD",
                analysis.BaselineModelRunId,
                analysis.BaselineMetricValue,
                rows);
        }

        var geographicAccounting = geographicAccountingEntity is null ? null : new ReportGeographicAccounting(
            geographicAccountingEntity.ScopeKind,
            geographicAccountingEntity.ScopeCode,
            geographicAccountingEntity.LocalOriginCount,
            geographicAccountingEntity.HostJurisdictionCannibalization,
            geographicAccountingEntity.CrossJurisdictionCapture,
            geographicAccountingEntity.OutsideOrUnmodeledLeakageCapture,
            geographicAccountingEntity.InducedResidentGgr,
            geographicAccountingEntity.TourismGgr,
            geographicAccountingEntity.TrafficGgr,
            geographicAccountingEntity.TransferEffectGgr,
            geographicAccountingEntity.MarketExpansionAndImportGgr,
            geographicAccountingEntity.StabilizedGgr,
            geographicAccountingEntity.LocalResidentGamingBase,
            geographicAccountingEntity.ExcludedLocalCasinoCannibalization,
            geographicAccountingEntity.ExcludedRepatriatedOrLeakedResidentGgr,
            geographicAccountingEntity.RemainingLocalResidentGamingBase);
        var warnings = ParseWarnings(run.WarningSummary)
            .Concat(parameters.Where(parameter => !string.IsNullOrWhiteSpace(parameter.WarningText))
                .Select(parameter => parameter.WarningText!))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new CasinoImpactReportModel(
            new ReportIdentity(
                run.Id,
                run.ModelVersion,
                templateVersion,
                generatedAtUtc,
                run.CreatedAtUtc,
                run.FinalizedAtUtc.Value,
                jurisdiction.Code,
                jurisdiction.Name,
                jurisdictionProfileVersion,
                routeMetadata.Select(item => item.RoutingGraphHash).Distinct().Order().ToArray(),
                routeMetadata.Select(item => item.CostingProfile).Distinct().Order().ToArray()),
            new ReportScenario(
                scenarioValues.ScenarioName,
                run.CandidateLatitude,
                run.CandidateLongitude,
                scenarioValues.DemandSpecification,
                scenarioValues.AttractionSpecification,
                scenarioValues.FrictionForm,
                origins.Select(origin => origin.OriginType).Distinct().SingleOrDefault() ?? "unknown",
                geographicAccounting?.ScopeKind ?? "not-configured",
                geographicAccounting?.ScopeCode ?? "not-configured",
                parameterSets),
            new ReportDevelopmentProgram(
                program.Id,
                program.StableProgramId,
                program.Version,
                program.Name,
                program.SlotOrVltPositions,
                program.TableGameCount,
                program.PokerTableCount,
                program.HasSportsbook,
                program.HotelRoomCount,
                program.GamingFloorSquareFeet,
                program.FoodBeverageVenueCount,
                program.EventCapacity,
                program.ResortAmenityCount,
                program.CapitalCost,
                program.CapitalCostDollarYear,
                program.PlannedOpeningDate,
                program.StabilizedYearNumber),
            new ReportRevenueSummary(
                originRows.Sum(item => item.result.ResidentDemand),
                proposed.TotalWithProjectResidentGgr - proposed.InducedResidentGgr,
                proposed.InducedResidentGgr,
                proposed.TotalWithProjectResidentGgr,
                proposed.TourismGgr,
                proposed.TrafficGgr,
                proposed.StabilizedTotalGgr),
            dataSources,
            parameters,
            origins,
            GroupOrigins(origins, origin => origin.StateCode, "state"),
            GroupOrigins(origins, origin => string.IsNullOrWhiteSpace(origin.CountyCode)
                ? $"{origin.StateCode}:unassigned"
                : $"{origin.StateCode}-{origin.CountyCode}", "county"),
            facilities,
            demandComponents,
            capacity,
            ramp,
            geographicAccounting,
            sectors,
            employment,
            fiscal,
            socialCosts,
            net,
            sensitivity,
            benchmarks,
            warnings,
            [
                "Results are estimates conditioned on the cited data snapshots, routes, development program, and parameter values.",
                "Public benchmark studies are validation anchors; reported outputs are not forced targets.",
                "Capacity diagnostics flag implausible ranges and do not cap modeled demand.",
                "Social-cost estimates depend on uncertain prevalence and per-case assumptions disclosed in the parameter appendix."
            ]);
    }

    private static IReadOnlyList<ReportOriginGroup> GroupOrigins(
        IReadOnlyCollection<ReportOrigin> origins,
        Func<ReportOrigin, string> keySelector,
        string geographyType)
    {
        var total = origins.Sum(origin => origin.TotalProposedResidentGgr);
        return origins.GroupBy(keySelector, StringComparer.Ordinal)
            .Select(group => new ReportOriginGroup(
                geographyType,
                group.Key,
                group.Sum(origin => origin.RedistributedResidentGgr),
                group.Sum(origin => origin.InducedResidentGgr),
                group.Sum(origin => origin.TotalProposedResidentGgr),
                Share(group.Sum(origin => origin.TotalProposedResidentGgr), total),
                group.Count()))
            .OrderByDescending(group => group.TotalProposedResidentGgr)
            .ThenBy(group => group.GeographyCode, StringComparer.Ordinal)
            .ToArray();
    }

    private static double Share(decimal value, decimal total) => total == 0 ? 0 : Convert.ToDouble(value / total);

    private static IReadOnlyList<string> ParseWarnings(string? warningSummary) =>
        string.IsNullOrWhiteSpace(warningSummary)
            ? []
            : Regex.Split(warningSummary, @"(?:\r?\n|;|(?<=\.)\s+(?=[A-Z]))")
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();

    private async Task<string> BuildJurisdictionProfileVersionAsync(
        Jurisdiction jurisdiction,
        DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        var jurisdictionIds = new List<int>();
        var current = jurisdiction;
        var visited = new HashSet<int>();
        while (visited.Add(current.Id))
        {
            jurisdictionIds.Add(current.Id);
            if (current.ParentJurisdictionId is not { } parentId)
            {
                break;
            }

            current = await db.Jurisdictions.AsNoTracking()
                .SingleAsync(item => item.Id == parentId, cancellationToken);
        }

        var rules = await db.JurisdictionRules.AsNoTracking()
            .Where(rule => jurisdictionIds.Contains(rule.JurisdictionId) &&
                           rule.EffectiveFrom <= effectiveOn &&
                           (rule.EffectiveTo == null || rule.EffectiveTo >= effectiveOn))
            .OrderBy(rule => rule.JurisdictionId)
            .ThenBy(rule => rule.RuleType)
            .ThenBy(rule => rule.Id)
            .Select(rule => new
            {
                rule.Id,
                rule.JurisdictionId,
                rule.RuleType,
                rule.RuleValueJson,
                rule.ValidationState,
                rule.EffectiveFrom,
                rule.EffectiveTo,
                rule.SourceUrl
            })
            .ToListAsync(cancellationToken);
        var fingerprint = string.Join('\n', rules.Select(rule =>
            $"{rule.JurisdictionId}|{rule.Id}|{rule.RuleType}|{rule.ValidationState}|{rule.EffectiveFrom:yyyy-MM-dd}|{rule.EffectiveTo:yyyy-MM-dd}|{rule.RuleValueJson}|{rule.SourceUrl}"));
        var hashInput = $"{jurisdiction.Code}|{effectiveOn:yyyy-MM-dd}|{fingerprint}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
        return $"effective-rules@{effectiveOn:yyyy-MM-dd}#{hash[..16]}";
    }

    private static ResolvedScenario ParseResolvedInput(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new ResolvedScenario(
            ReadString(root, "scenarioName", "Unnamed scenario"),
            ReadString(root, "demandSpecification", "unknown"),
            ReadString(root, "attractionSpecification", "unknown"),
            ReadString(root, "frictionForm", "unknown"));
    }

    private static string ReadString(JsonElement root, string property, string fallback)
    {
        if (root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? fallback;
        }

        foreach (var candidate in root.EnumerateObject())
        {
            if (candidate.Name.Equals(property, StringComparison.OrdinalIgnoreCase) &&
                candidate.Value.ValueKind == JsonValueKind.String)
            {
                return candidate.Value.GetString() ?? fallback;
            }
        }
        return fallback;
    }

    private sealed record ResolvedScenario(
        string ScenarioName,
        string DemandSpecification,
        string AttractionSpecification,
        string FrictionForm);
}

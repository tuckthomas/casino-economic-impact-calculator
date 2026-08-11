using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Tests;

public sealed class ModelFoundationSchemaTests
{
    [Fact]
    public void Model_UsesMigrationCompatibleSnakeCaseColumnsAndJsonb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Database=metadata_only;Username=metadata_only;Password=metadata_only",
                provider => provider.UseNetTopologySuite())
            .Options;
        using var db = new AppDbContext(options);

        var ruleEntity = db.Model.FindEntityType(typeof(JurisdictionRule))!;
        var ruleTable = StoreObjectIdentifier.Table("jurisdiction_rules", null);
        Assert.Equal("rule_value_json", ruleEntity.FindProperty(nameof(JurisdictionRule.RuleValueJson))!.GetColumnName(ruleTable));
        Assert.Equal("jsonb", ruleEntity.FindProperty(nameof(JurisdictionRule.RuleValueJson))!.GetColumnType());

        var runEntity = db.Model.FindEntityType(typeof(ModelRun))!;
        var runTable = StoreObjectIdentifier.Table("model_runs", null);
        Assert.Equal("finalized_at_utc", runEntity.FindProperty(nameof(ModelRun.FinalizedAtUtc))!.GetColumnName(runTable));
        Assert.Equal("jsonb", runEntity.FindProperty(nameof(ModelRun.ResolvedInputJson))!.GetColumnType());

        var benchmarkEntity = db.Model.FindEntityType(typeof(BenchmarkStudy))!;
        Assert.Equal("'{}'::jsonb", benchmarkEntity.FindProperty(nameof(BenchmarkStudy.DevelopmentProgramJson))!.GetDefaultValueSql());
        Assert.Equal("'{}'::jsonb", benchmarkEntity.FindProperty(nameof(BenchmarkStudy.ReportedOutputsJson))!.GetDefaultValueSql());
        Assert.Equal("'{}'::jsonb", benchmarkEntity.FindProperty(nameof(BenchmarkStudy.ReportedAssumptionsJson))!.GetDefaultValueSql());
        Assert.Equal("NOW()", benchmarkEntity.FindProperty(nameof(BenchmarkStudy.CreatedAtUtc))!.GetDefaultValueSql());

        var referenceEntity = db.Model.FindEntityType(typeof(ModelRunParameterSetReference))!;
        var referenceTable = StoreObjectIdentifier.Table("model_run_parameter_set_references", null);
        Assert.Equal("parameter_set_id", referenceEntity.FindProperty(nameof(ModelRunParameterSetReference.ParameterSetId))!.GetColumnName(referenceTable));

        var snapshotEntity = db.Model.FindEntityType(typeof(DatasetSnapshot))!;
        var snapshotTable = StoreObjectIdentifier.Table("dataset_snapshots", null);
        Assert.Equal("validation_state", snapshotEntity.FindProperty(nameof(DatasetSnapshot.ValidationState))!.GetColumnName(snapshotTable));
        Assert.Equal("jsonb", snapshotEntity.FindProperty(nameof(DatasetSnapshot.WarningsJson))!.GetColumnType());
        Assert.Equal("is_sealed", snapshotEntity.FindProperty(nameof(DatasetSnapshot.IsSealed))!.GetColumnName(snapshotTable));

        var originEntity = db.Model.FindEntityType(typeof(OriginZone))!;
        var originTable = StoreObjectIdentifier.Table("origin_zones", null);
        Assert.Equal("stable_origin_id", originEntity.FindProperty(nameof(OriginZone.StableOriginId))!.GetColumnName(originTable));
        Assert.Equal("geometry(Point, 4326)", originEntity.FindProperty(nameof(OriginZone.RepresentativePoint))!.GetColumnType());

        var competitorEntity = db.Model.FindEntityType(typeof(CasinoCompetitor))!;
        var stableVenueIndex = Assert.Single(
            competitorEntity.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(CasinoCompetitor.DatasetSnapshotId), nameof(CasinoCompetitor.StableVenueId)]));
        Assert.True(stableVenueIndex.IsUnique);

        var revenueEntity = db.Model.FindEntityType(typeof(CasinoGamingRevenuePeriod))!;
        Assert.Contains(
            revenueEntity.GetIndexes(),
            index => index.IsUnique && index.Properties.First().Name == nameof(CasinoGamingRevenuePeriod.DatasetSnapshotId));

        var travelEntity = db.Model.FindEntityType(typeof(OriginFacilityTravel))!;
        var travelTable = StoreObjectIdentifier.Table("origin_facility_travel", null);
        Assert.Equal("routing_graph_hash", travelEntity.FindProperty(nameof(OriginFacilityTravel.RoutingGraphHash))!.GetColumnName(travelTable));
        var candidateTravelEntity = db.Model.FindEntityType(typeof(CandidateLocationTravelCache))!;
        var candidateTravelTable = StoreObjectIdentifier.Table("candidate_location_travel_cache", null);
        Assert.Equal("id", candidateTravelEntity.FindProperty(nameof(CandidateLocationTravelCache.Id))!.GetColumnName(candidateTravelTable));
        Assert.Equal(
            "candidate_coordinate_hash",
            candidateTravelEntity.FindProperty(nameof(CandidateLocationTravelCache.CandidateCoordinateHash))!.GetColumnName(candidateTravelTable));
        Assert.Contains(
            candidateTravelEntity.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(CandidateLocationTravelCache.OriginZoneId),
                nameof(CandidateLocationTravelCache.CandidateCoordinateHash),
                nameof(CandidateLocationTravelCache.RoutingGraphHash),
                nameof(CandidateLocationTravelCache.CostingProfile)
            ]));

        var allocationEntity = db.Model.FindEntityType(typeof(ModelRunOriginFacilityAllocation))!;
        var allocationTable = StoreObjectIdentifier.Table("model_run_origin_facility_allocations", null);
        Assert.Equal("allocated_resident_ggr", allocationEntity.FindProperty(nameof(ModelRunOriginFacilityAllocation.AllocatedResidentGgr))!.GetColumnName(allocationTable));
        Assert.Equal(20, allocationEntity.FindProperty(nameof(ModelRunOriginFacilityAllocation.AllocatedResidentGgr))!.GetPrecision());
        Assert.Equal(20, allocationEntity.FindProperty(nameof(ModelRunOriginFacilityAllocation.AllocatedInducedResidentGgr))!.GetPrecision());

        var trafficEntity = db.Model.FindEntityType(typeof(TrafficCorridorObservation))!;
        var trafficTable = StoreObjectIdentifier.Table("traffic_corridor_observations", null);
        Assert.Equal(
            "geometry(Point, 4326)",
            trafficEntity.FindProperty(nameof(TrafficCorridorObservation.CountLocation))!.GetColumnType());

        var demandComponentEntity = db.Model.FindEntityType(typeof(ModelRunDemandComponent))!;
        Assert.Equal("jsonb", demandComponentEntity.FindProperty(nameof(ModelRunDemandComponent.DetailsJson))!.GetColumnType());

        var geographicAccountingEntity = db.Model.FindEntityType(typeof(ModelRunGeographicAccounting))!;
        var geographicAccountingTable = StoreObjectIdentifier.Table("model_run_geographic_accounting", null);
        Assert.Equal(
            "host_jurisdiction_cannibalization",
            geographicAccountingEntity.FindProperty(nameof(ModelRunGeographicAccounting.HostJurisdictionCannibalization))!
                .GetColumnName(geographicAccountingTable));
        Assert.Equal("jsonb", geographicAccountingEntity.FindProperty(nameof(ModelRunGeographicAccounting.LocalOriginIdsJson))!.GetColumnType());
        Assert.Equal(20, geographicAccountingEntity.FindProperty(nameof(ModelRunGeographicAccounting.StabilizedGgr))!.GetPrecision());

        var fiscalEntity = db.Model.FindEntityType(typeof(ModelRunFiscalImpact))!;
        Assert.Equal("jsonb", fiscalEntity.FindProperty(nameof(ModelRunFiscalImpact.RuleProvenanceJson))!.GetColumnType());
    }

    [Fact]
    public void Assembly_ContainsAdditiveFoundationMigration()
    {
        var resourceNames = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceNames();
        Assert.Contains(resourceNames, name => name.EndsWith("005_casino_competitors.sql", StringComparison.Ordinal));
        Assert.Contains(resourceNames, name => name.EndsWith("006_gravity_model_foundation.sql", StringComparison.Ordinal));
        var dataFoundationMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("007_model_data_foundation.sql", StringComparison.Ordinal));

        using var stream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(dataFoundationMigration)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        Assert.Contains("prevent_dataset_snapshot_mutation", sql, StringComparison.Ordinal);
        Assert.Contains("prevent_referenced_data_source_mutation", sql, StringComparison.Ordinal);
        Assert.Contains("prevent_finalized_dataset_reference_mutation", sql, StringComparison.Ordinal);
        Assert.Contains("casino_gaming_revenue_periods", sql, StringComparison.Ordinal);

        var gravityEngineMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("008_gravity_engine.sql", StringComparison.Ordinal));
        using var gravityStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(gravityEngineMigration)!;
        using var gravityReader = new StreamReader(gravityStream);
        var gravitySql = gravityReader.ReadToEnd();
        Assert.Contains("origin_facility_travel", gravitySql, StringComparison.Ordinal);
        Assert.Contains("model_run_origin_facility_allocations", gravitySql, StringComparison.Ordinal);
        Assert.Contains("prevent_immutable_development_program_mutation", gravitySql, StringComparison.Ordinal);
        Assert.Contains("trg_prevent_finalized_allocation_mutation", gravitySql, StringComparison.Ordinal);
        Assert.Contains("ix_casino_competitors_snapshot_stable_venue_id", gravitySql, StringComparison.Ordinal);
        Assert.Contains("ix_casino_gaming_revenue_snapshot_period", gravitySql, StringComparison.Ordinal);
        Assert.Contains("prevent_sealed_snapshot_data_mutation", gravitySql, StringComparison.Ordinal);

        var marketExpansionMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("009_market_expansion.sql", StringComparison.Ordinal));
        using var expansionStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(marketExpansionMigration)!;
        using var expansionReader = new StreamReader(expansionStream);
        var expansionSql = expansionReader.ReadToEnd();
        Assert.Contains("baseline_log_accessibility", expansionSql, StringComparison.Ordinal);
        Assert.Contains("allocated_induced_resident_ggr", expansionSql, StringComparison.Ordinal);

        var extendedDemandMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("010_tourism_traffic_capacity_ramp.sql", StringComparison.Ordinal));
        using var extendedStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(extendedDemandMigration)!;
        using var extendedReader = new StreamReader(extendedStream);
        var extendedSql = extendedReader.ReadToEnd();
        Assert.Contains("tourism_market_observations", extendedSql, StringComparison.Ordinal);
        Assert.Contains("traffic_corridor_observations", extendedSql, StringComparison.Ordinal);
        Assert.Contains("model_run_capacity_diagnostics", extendedSql, StringComparison.Ordinal);
        Assert.Contains("model_run_ramp_results", extendedSql, StringComparison.Ordinal);

        var impactMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("011_comprehensive_impact_accounting.sql", StringComparison.Ordinal));
        using var impactStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(impactMigration)!;
        using var impactReader = new StreamReader(impactStream);
        var impactSql = impactReader.ReadToEnd();
        Assert.Contains("model_run_geographic_accounting", impactSql, StringComparison.Ordinal);
        Assert.Contains("model_run_sector_displacement", impactSql, StringComparison.Ordinal);
        Assert.Contains("model_run_employment_impacts", impactSql, StringComparison.Ordinal);
        Assert.Contains("model_run_fiscal_impacts", impactSql, StringComparison.Ordinal);
        Assert.Contains("model_run_social_costs", impactSql, StringComparison.Ordinal);
        Assert.Contains("model_run_net_impacts", impactSql, StringComparison.Ordinal);

        var validationMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("012_validation_and_calibration.sql", StringComparison.Ordinal));
        using var validationStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(validationMigration)!;
        using var validationReader = new StreamReader(validationStream);
        var validationSql = validationReader.ReadToEnd();
        Assert.Contains("benchmark_studies", validationSql, StringComparison.Ordinal);
        Assert.Contains("validation_cases", validationSql, StringComparison.Ordinal);
        Assert.Contains("validation_evaluations", validationSql, StringComparison.Ordinal);
        Assert.Contains("validation_case_results", validationSql, StringComparison.Ordinal);
        Assert.Contains("prevent_immutable_validation_evaluation_mutation", validationSql, StringComparison.Ordinal);
        Assert.Contains("development_program_json, reported_outputs_json", validationSql, StringComparison.Ordinal);
        Assert.Contains("validation_state, created_at_utc", validationSql, StringComparison.Ordinal);

        var reportMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("013_stored_run_reports.sql", StringComparison.Ordinal));
        using var reportStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(reportMigration)!;
        using var reportReader = new StreamReader(reportStream);
        var reportSql = reportReader.ReadToEnd();
        Assert.Contains("model_run_report_artifacts", reportSql, StringComparison.Ordinal);
        Assert.Contains("prevent_model_run_report_artifact_mutation", reportSql, StringComparison.Ordinal);

        var benchmarkEvidenceMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("014_indiana_benchmark_evidence.sql", StringComparison.Ordinal));
        using var benchmarkEvidenceStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(benchmarkEvidenceMigration)!;
        using var benchmarkEvidenceReader = new StreamReader(benchmarkEvidenceStream);
        var benchmarkEvidenceSql = benchmarkEvidenceReader.ReadToEnd();
        Assert.Contains("spectrum-in-relocation-2025", benchmarkEvidenceSql, StringComparison.Ordinal);
        Assert.Contains("cbre-union-gaming-fort-wayne-2025", benchmarkEvidenceSql, StringComparison.Ordinal);
        Assert.Contains("steinberg-steuben-feasibility", benchmarkEvidenceSql, StringComparison.Ordinal);
        Assert.Contains("validation_state = 'extracted'", benchmarkEvidenceSql, StringComparison.Ordinal);

        var sensitivityMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("015_sensitivity_analyses.sql", StringComparison.Ordinal));
        using var sensitivityStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(sensitivityMigration)!;
        using var sensitivityReader = new StreamReader(sensitivityStream);
        var sensitivitySql = sensitivityReader.ReadToEnd();
        Assert.Contains("sensitivity_analyses", sensitivitySql, StringComparison.Ordinal);
        Assert.Contains("sensitivity_analysis_points", sensitivitySql, StringComparison.Ordinal);
        Assert.Contains("prevent_immutable_sensitivity_point_mutation", sensitivitySql, StringComparison.Ordinal);

        var localEconomicMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("016_local_economic_inventory.sql", StringComparison.Ordinal));
        using var localEconomicStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(localEconomicMigration)!;
        using var localEconomicReader = new StreamReader(localEconomicStream);
        var localEconomicSql = localEconomicReader.ReadToEnd();
        Assert.Contains("local_economic_sector_observations", localEconomicSql, StringComparison.Ordinal);
        Assert.Contains("prevent_sealed_snapshot_data_mutation", localEconomicSql, StringComparison.Ordinal);

        var nullableFacilityFlagsMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("017_nullable_facility_evidence_flags.sql", StringComparison.Ordinal));
        using var nullableFacilityFlagsStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(nullableFacilityFlagsMigration)!;
        using var nullableFacilityFlagsReader = new StreamReader(nullableFacilityFlagsStream);
        var nullableFacilityFlagsSql = nullableFacilityFlagsReader.ReadToEnd();
        Assert.Contains("ALTER COLUMN has_hotel DROP NOT NULL", nullableFacilityFlagsSql, StringComparison.Ordinal);
        Assert.Contains("ALTER COLUMN is_border_market DROP NOT NULL", nullableFacilityFlagsSql, StringComparison.Ordinal);

        var candidateCacheMigration = Assert.Single(
            resourceNames,
            name => name.EndsWith("018_candidate_location_travel_cache.sql", StringComparison.Ordinal));
        using var candidateCacheStream = typeof(ModelFoundationInitializer).Assembly.GetManifestResourceStream(candidateCacheMigration)!;
        using var candidateCacheReader = new StreamReader(candidateCacheStream);
        var candidateCacheSql = candidateCacheReader.ReadToEnd();
        Assert.Contains("candidate_location_travel_cache", candidateCacheSql, StringComparison.Ordinal);
        Assert.Contains("candidate_coordinate_hash", candidateCacheSql, StringComparison.Ordinal);
        Assert.Contains("routing_graph_hash", candidateCacheSql, StringComparison.Ordinal);
    }
}

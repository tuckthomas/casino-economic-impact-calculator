using Microsoft.EntityFrameworkCore;
using SaveNEIN.Shared;

namespace SaveNEIN.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<ImpactFact> ImpactFacts => Set<ImpactFact>();
    public DbSet<Legislator> Legislators => Set<Legislator>();
    
    public DbSet<SaveNEIN.Server.Data.Entities.County> Counties => Set<SaveNEIN.Server.Data.Entities.County>();
    public DbSet<SaveNEIN.Server.Data.Entities.BlockGroup> BlockGroups => Set<SaveNEIN.Server.Data.Entities.BlockGroup>();
    public DbSet<SaveNEIN.Server.Data.Entities.IsochroneCache> IsochroneCache => Set<SaveNEIN.Server.Data.Entities.IsochroneCache>();
    
    // Phase 9: Address Point Infrastructure
    public DbSet<SaveNEIN.Server.Data.Entities.AddressPoint> AddressPoints => Set<SaveNEIN.Server.Data.Entities.AddressPoint>();
    public DbSet<SaveNEIN.Server.Data.Entities.TigerAddressRange> TigerAddressRanges => Set<SaveNEIN.Server.Data.Entities.TigerAddressRange>();
    public DbSet<SaveNEIN.Server.Data.Entities.CasinoCompetitor> CasinoCompetitors => Set<SaveNEIN.Server.Data.Entities.CasinoCompetitor>();
    public DbSet<SaveNEIN.Server.Data.Entities.Jurisdiction> Jurisdictions => Set<SaveNEIN.Server.Data.Entities.Jurisdiction>();
    public DbSet<SaveNEIN.Server.Data.Entities.JurisdictionRule> JurisdictionRules => Set<SaveNEIN.Server.Data.Entities.JurisdictionRule>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelParameterDefinition> ModelParameterDefinitions => Set<SaveNEIN.Server.Data.Entities.ModelParameterDefinition>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelParameterSet> ModelParameterSets => Set<SaveNEIN.Server.Data.Entities.ModelParameterSet>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelParameterSetValue> ModelParameterSetValues => Set<SaveNEIN.Server.Data.Entities.ModelParameterSetValue>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRun> ModelRuns => Set<SaveNEIN.Server.Data.Entities.ModelRun>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunParameterValue> ModelRunParameterValues => Set<SaveNEIN.Server.Data.Entities.ModelRunParameterValue>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunParameterSetReference> ModelRunParameterSetReferences => Set<SaveNEIN.Server.Data.Entities.ModelRunParameterSetReference>();
    public DbSet<SaveNEIN.Server.Data.Entities.DataSource> DataSources => Set<SaveNEIN.Server.Data.Entities.DataSource>();
    public DbSet<SaveNEIN.Server.Data.Entities.DatasetSnapshot> DatasetSnapshots => Set<SaveNEIN.Server.Data.Entities.DatasetSnapshot>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunDatasetSnapshotReference> ModelRunDatasetSnapshotReferences => Set<SaveNEIN.Server.Data.Entities.ModelRunDatasetSnapshotReference>();
    public DbSet<SaveNEIN.Server.Data.Entities.OriginZone> OriginZones => Set<SaveNEIN.Server.Data.Entities.OriginZone>();
    public DbSet<SaveNEIN.Server.Data.Entities.OriginZoneAgeBin> OriginZoneAgeBins => Set<SaveNEIN.Server.Data.Entities.OriginZoneAgeBin>();
    public DbSet<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod> OriginZoneIncomePeriods => Set<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>();
    public DbSet<SaveNEIN.Server.Data.Entities.CasinoCompetitorHistory> CasinoCompetitorHistory => Set<SaveNEIN.Server.Data.Entities.CasinoCompetitorHistory>();
    public DbSet<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod> CasinoGamingRevenuePeriods => Set<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>();
    public DbSet<SaveNEIN.Server.Data.Entities.DevelopmentProgram> DevelopmentPrograms => Set<SaveNEIN.Server.Data.Entities.DevelopmentProgram>();
    public DbSet<SaveNEIN.Server.Data.Entities.OriginFacilityTravel> OriginFacilityTravel => Set<SaveNEIN.Server.Data.Entities.OriginFacilityTravel>();
    public DbSet<SaveNEIN.Server.Data.Entities.CandidateLocationTravelCache> CandidateLocationTravelCache => Set<SaveNEIN.Server.Data.Entities.CandidateLocationTravelCache>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunOriginResult> ModelRunOriginResults => Set<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult> ModelRunFacilityResults => Set<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation> ModelRunOriginFacilityAllocations => Set<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>();
    public DbSet<SaveNEIN.Server.Data.Entities.TourismMarketObservation> TourismMarketObservations => Set<SaveNEIN.Server.Data.Entities.TourismMarketObservation>();
    public DbSet<SaveNEIN.Server.Data.Entities.TrafficCorridorObservation> TrafficCorridorObservations => Set<SaveNEIN.Server.Data.Entities.TrafficCorridorObservation>();
    public DbSet<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation> LocalEconomicSectorObservations => Set<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent> ModelRunDemandComponents => Set<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic> ModelRunCapacityDiagnostics => Set<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunRampResult> ModelRunRampResults => Set<SaveNEIN.Server.Data.Entities.ModelRunRampResult>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunGeographicAccounting> ModelRunGeographicAccounting => Set<SaveNEIN.Server.Data.Entities.ModelRunGeographicAccounting>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunSectorDisplacement> ModelRunSectorDisplacement => Set<SaveNEIN.Server.Data.Entities.ModelRunSectorDisplacement>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunEmploymentImpact> ModelRunEmploymentImpacts => Set<SaveNEIN.Server.Data.Entities.ModelRunEmploymentImpact>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunFiscalImpact> ModelRunFiscalImpacts => Set<SaveNEIN.Server.Data.Entities.ModelRunFiscalImpact>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunSocialCost> ModelRunSocialCosts => Set<SaveNEIN.Server.Data.Entities.ModelRunSocialCost>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunNetImpact> ModelRunNetImpacts => Set<SaveNEIN.Server.Data.Entities.ModelRunNetImpact>();
    public DbSet<SaveNEIN.Server.Data.Entities.BenchmarkStudy> BenchmarkStudies => Set<SaveNEIN.Server.Data.Entities.BenchmarkStudy>();
    public DbSet<SaveNEIN.Server.Data.Entities.ValidationCase> ValidationCases => Set<SaveNEIN.Server.Data.Entities.ValidationCase>();
    public DbSet<SaveNEIN.Server.Data.Entities.ValidationEvaluation> ValidationEvaluations => Set<SaveNEIN.Server.Data.Entities.ValidationEvaluation>();
    public DbSet<SaveNEIN.Server.Data.Entities.ValidationCaseResult> ValidationCaseResults => Set<SaveNEIN.Server.Data.Entities.ValidationCaseResult>();
    public DbSet<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern> ValidationGeographicResidualPatterns => Set<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>();
    public DbSet<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact> ModelRunReportArtifacts => Set<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact>();
    public DbSet<SaveNEIN.Server.Data.Entities.SensitivityAnalysis> SensitivityAnalyses => Set<SaveNEIN.Server.Data.Entities.SensitivityAnalysis>();
    public DbSet<SaveNEIN.Server.Data.Entities.SensitivityAnalysisPoint> SensitivityAnalysisPoints => Set<SaveNEIN.Server.Data.Entities.SensitivityAnalysisPoint>();
    public DbSet<SaveNEIN.Server.Data.Entities.CoalitionSignup> CoalitionSignups => Set<SaveNEIN.Server.Data.Entities.CoalitionSignup>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // PostGIS GIST Indexes
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.County>()
            .HasIndex(c => c.Geom)
            .HasMethod("gist");

        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BlockGroup>()
            .HasIndex(b => b.Geom)
            .HasMethod("gist");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BlockGroup>()
            .HasIndex(b => b.CountyFips);

        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.IsochroneCache>()
            .HasIndex(i => i.Geom)
            .HasMethod("gist");
            
        // Unique route-surface cache key (application should round before query).
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.IsochroneCache>()
            .HasIndex(i => new { i.Lat, i.Lon, i.Minutes, i.SourceHash });
            
        // --- Phase 9: Address Point Indexes ---
        
        // AddressPoint: Unique index for upsert on (source, source_id)
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.AddressPoint>()
            .HasIndex(a => new { a.Source, a.SourceId })
            .IsUnique();
            
        // AddressPoint: GIST index on geometry
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.AddressPoint>()
            .HasIndex(a => a.Geom)
            .HasMethod("gist");
            
        // AddressPoint: Lookup index for geocoding queries
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.AddressPoint>()
            .HasIndex(a => new { a.State, a.Zip, a.StreetNameNorm, a.HouseNumber });
            
        // TigerAddressRange: GIST index on geometry
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TigerAddressRange>()
            .HasIndex(t => t.Geom)
            .HasMethod("gist");
            
        // TigerAddressRange: Lookup index for interpolation
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TigerAddressRange>()
            .HasIndex(t => new { t.State, t.NameNorm });
            
        // CasinoCompetitor: GIST index on geometry
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .HasIndex(c => c.Geom)
            .HasMethod("gist");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .HasIndex(c => new { c.DatasetSnapshotId, c.StableVenueId })
            .IsUnique();

        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .HasIndex(jurisdiction => jurisdiction.Code)
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.JurisdictionRule>()
            .HasIndex(rule => new { rule.JurisdictionId, rule.RuleType, rule.EffectiveFrom });
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelParameterDefinition>()
            .HasIndex(definition => definition.Key)
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelParameterSet>()
            .HasIndex(set => new { set.Key, set.Version })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelParameterSetValue>()
            .HasIndex(value => new { value.ParameterSetId, value.ParameterDefinitionId })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunParameterValue>()
            .HasIndex(value => new { value.ModelRunId, value.ParameterDefinitionId })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunParameterSetReference>()
            .HasIndex(reference => new { reference.ModelRunId, reference.ParameterSetId, reference.SourceLayer })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DataSource>()
            .HasIndex(source => new { source.Url, source.ContentHash })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .HasIndex(snapshot => new { snapshot.DatasetKey, snapshot.Checksum })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDatasetSnapshotReference>()
            .HasIndex(reference => new { reference.ModelRunId, reference.Role, reference.ReferenceKey })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZone>()
            .HasIndex(origin => new { origin.DatasetSnapshotId, origin.StableOriginId })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZone>()
            .HasIndex(origin => origin.RepresentativePoint)
            .HasMethod("gist");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZone>()
            .HasIndex(origin => origin.AreaGeometry)
            .HasMethod("gist");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneAgeBin>()
            .HasIndex(bin => new { bin.OriginZoneId, bin.DatasetSnapshotId, bin.ObservationYear, bin.MinimumAge })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>()
            .HasIndex(period => new { period.OriginZoneId, period.DatasetSnapshotId, period.TaxYear })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitorHistory>()
            .HasIndex(history => new { history.CasinoCompetitorId, history.EventType, history.EffectiveFrom });
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>()
            .HasIndex(period => new
            {
                period.DatasetSnapshotId,
                period.CasinoCompetitorId,
                period.PeriodStart,
                period.PeriodEnd,
                period.ReportedMetricKey
            })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DevelopmentProgram>()
            .HasIndex(program => new { program.StableProgramId, program.Version })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginFacilityTravel>()
            .HasIndex(route => new
            {
                route.OriginZoneId,
                route.FacilityKey,
                route.FacilityCoordinateHash,
                route.RoutingGraphHash,
                route.CostingProfile
            })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CandidateLocationTravelCache>()
            .HasIndex(route => new
            {
                route.OriginZoneId,
                route.CandidateCoordinateHash,
                route.RoutingGraphHash,
                route.CostingProfile
            })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .HasIndex(result => new { result.ModelRunId, result.OriginZoneId, result.DemandSpecification })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .HasIndex(result => new { result.ModelRunId, result.FacilityKey })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .HasIndex(result => new { result.ModelRunId, result.OriginZoneId, result.FacilityKey, result.MarketState })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TourismMarketObservation>()
            .HasIndex(observation => new { observation.DatasetSnapshotId, observation.StableObservationId })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TrafficCorridorObservation>()
            .HasIndex(observation => new { observation.DatasetSnapshotId, observation.StableObservationId })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TrafficCorridorObservation>()
            .HasIndex(observation => observation.CountLocation)
            .HasMethod("gist");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation>()
            .HasIndex(observation => new { observation.DatasetSnapshotId, observation.StableObservationId })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .HasIndex(component => new { component.ModelRunId, component.ComponentType, component.SourceRecordKey })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>()
            .HasIndex(diagnostic => new { diagnostic.ModelRunId, diagnostic.FacilityKey })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunRampResult>()
            .HasIndex(result => new { result.ModelRunId, result.FacilityKey, result.CalendarYear })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunGeographicAccounting>()
            .HasIndex(result => new { result.ModelRunId, result.ScopeKind, result.ScopeCode })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunSectorDisplacement>()
            .HasIndex(result => new { result.ModelRunId, result.ScopeKind, result.ScopeCode, result.SectorKey })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunEmploymentImpact>()
            .HasIndex(result => new { result.ModelRunId, result.ScopeKind, result.ScopeCode })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFiscalImpact>()
            .HasIndex(result => new { result.ModelRunId, result.ScopeKind, result.ScopeCode })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunSocialCost>()
            .HasIndex(result => new { result.ModelRunId, result.ScopeKind, result.ScopeCode, result.DomainKey })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunNetImpact>()
            .HasIndex(result => new { result.ModelRunId, result.ScopeKind, result.ScopeCode })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BenchmarkStudy>()
            .HasIndex(study => study.BenchmarkKey)
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .HasIndex(validationCase => validationCase.CaseKey)
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .HasIndex(evaluation => new { evaluation.EvaluationKey, evaluation.Version })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .HasIndex(result => new { result.ValidationEvaluationId, result.ValidationCaseId, result.PredictionKind })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .HasIndex(pattern => new
            {
                pattern.ValidationEvaluationId,
                pattern.PredictionKind,
                pattern.DatasetPartition,
                pattern.GeographyKind,
                pattern.GeographyCode
            })
            .IsUnique();
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact>()
            .HasIndex(artifact => new { artifact.ModelRunId, artifact.TemplateVersion, artifact.PresentationOptionsHash })
            .IsUnique();

        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.JurisdictionRule>()
            .Property(rule => rule.RuleValueJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRun>()
            .Property(run => run.ResolvedInputJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRun>()
            .Property(run => run.DataSnapshotReferencesJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .Property(snapshot => snapshot.WarningsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .Property(snapshot => snapshot.ErrorsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>()
            .Property(period => period.AnomalyFlagsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation>()
            .Property(observation => observation.NaicsCodesJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>()
            .Property(period => period.AdjustedGrossIncome)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>()
            .Property(period => period.InflationAdjustedAdjustedGrossIncome)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>()
            .Property(period => period.MedianHouseholdIncome)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>()
            .Property(period => period.ReportedAmount)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>()
            .Property(period => period.InflationAdjustedAmount)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .Property(competitor => competitor.DevelopmentCost)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DevelopmentProgram>()
            .Property(program => program.CapitalCost)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.ResidentDemand)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.ProposedResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.InducedResidentDemand)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.InducedOutsideOptionGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.ProposedInducedResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.TotalProposedResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.HostJurisdictionCapture)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.ExternalJurisdictionCapture)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.TribalOrOtherJurisdictionCapture)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .Property(result => result.OutsideOptionCapture)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.BaselineResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.WithProjectResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.ChangeInResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.InducedResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.TotalWithProjectResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.TourismGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.TrafficGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .Property(result => result.StabilizedTotalGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .Property(result => result.AllocatedResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .Property(result => result.AllocatedInducedResidentGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TourismMarketObservation>()
            .Property(observation => observation.SourceQuantity)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TourismMarketObservation>()
            .Property(observation => observation.NormalizedVisitorPersonTrips)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation>()
            .Property(observation => observation.AnnualPayroll)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation>()
            .Property(observation => observation.AnnualReceiptsOrSales)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.InputQuantity)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.DeduplicatedQuantity)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.EligibleQuantity)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.ParticipatingQuantity)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.CapturedQuantity)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.Ggr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .Property(component => component.DetailsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>()
            .Property(diagnostic => diagnostic.BenchmarkProvenanceJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunGeographicAccounting>()
            .Property(result => result.LocalOriginIdsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFiscalImpact>()
            .Property(result => result.RuleProvenanceJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BenchmarkStudy>()
            .Property(study => study.DevelopmentProgramJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BenchmarkStudy>()
            .Property(study => study.ReportedOutputsJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BenchmarkStudy>()
            .Property(study => study.ReportedAssumptionsJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.BenchmarkStudy>()
            .Property(study => study.CreatedAtUtc)
            .HasDefaultValueSql("NOW()");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .Property(validationCase => validationCase.InclusionRulesJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .Property(validationCase => validationCase.PredictorValuesJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .Property(validationCase => validationCase.ExecutionRequestJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.InclusionRulesJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.SelectedParametersJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.TrainingMetricsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.HoldoutMetricsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.BenchmarkMetricsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.ComparableModelJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.ComparableTrainingMetricsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.ComparableHoldoutMetricsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .Property(evaluation => evaluation.ComparableBenchmarkMetricsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .Property(result => result.DiagnosticsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact>()
            .Property(artifact => artifact.PresentationOptionsJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact>()
            .Property(artifact => artifact.ReportModelJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.SensitivityAnalysis>()
            .Property(analysis => analysis.InputJson)
            .HasColumnType("jsonb");
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.SensitivityAnalysis>()
            .Property(analysis => analysis.BaselineMetricValue)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.SensitivityAnalysisPoint>()
            .Property(point => point.OutputMetricValue)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.SensitivityAnalysisPoint>()
            .Property(point => point.DeltaFromBaseline)
            .HasPrecision(20, 4);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>()
            .Property(diagnostic => diagnostic.StabilizedGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>()
            .Property(diagnostic => diagnostic.PlausibleCapacityMinimum)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>()
            .Property(diagnostic => diagnostic.PlausibleCapacityMaximum)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunRampResult>()
            .Property(result => result.ProjectedGgr)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .Property(validationCase => validationCase.ObservedRevenue)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .Property(result => result.ObservedRevenue)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .Property(result => result.PredictedRevenue)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .Property(result => result.Residual)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .Property(pattern => pattern.ObservedRevenue)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .Property(pattern => pattern.PredictedRevenue)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .Property(pattern => pattern.Residual)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .Property(pattern => pattern.MeanResidual)
            .HasPrecision(20, 2);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .Property(pattern => pattern.MeanAbsoluteError)
            .HasPrecision(20, 2);

        var impactAccountingTypes = new HashSet<Type>
        {
            typeof(SaveNEIN.Server.Data.Entities.ModelRunGeographicAccounting),
            typeof(SaveNEIN.Server.Data.Entities.ModelRunSectorDisplacement),
            typeof(SaveNEIN.Server.Data.Entities.ModelRunEmploymentImpact),
            typeof(SaveNEIN.Server.Data.Entities.ModelRunFiscalImpact),
            typeof(SaveNEIN.Server.Data.Entities.ModelRunSocialCost),
            typeof(SaveNEIN.Server.Data.Entities.ModelRunNetImpact)
        };
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => impactAccountingTypes.Contains(entityType.ClrType)))
        {
            foreach (var property in entityType.GetProperties()
                         .Where(property => property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)))
            {
                property.SetPrecision(20);
                property.SetScale(2);
            }
        }

        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .HasOne<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .WithMany()
            .HasForeignKey(jurisdiction => jurisdiction.ParentJurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRun>()
            .HasOne<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .WithMany()
            .HasForeignKey(run => run.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRun>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelParameterSet>()
            .WithMany()
            .HasForeignKey(run => run.BaseParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRun>()
            .HasOne<SaveNEIN.Server.Data.Entities.DevelopmentProgram>()
            .WithMany()
            .HasForeignKey(run => run.DevelopmentProgramId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.JurisdictionRule>()
            .HasOne<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .WithMany()
            .HasForeignKey(rule => rule.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelParameterSet>()
            .HasOne<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .WithMany()
            .HasForeignKey(set => set.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelParameterSetValue>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelParameterSet>()
            .WithMany()
            .HasForeignKey(value => value.ParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelParameterSetValue>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelParameterDefinition>()
            .WithMany()
            .HasForeignKey(value => value.ParameterDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunParameterValue>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(value => value.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunParameterValue>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelParameterDefinition>()
            .WithMany()
            .HasForeignKey(value => value.ParameterDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunParameterSetReference>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(reference => reference.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunParameterSetReference>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelParameterSet>()
            .WithMany()
            .HasForeignKey(reference => reference.ParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .HasOne<SaveNEIN.Server.Data.Entities.DataSource>()
            .WithMany()
            .HasForeignKey(snapshot => snapshot.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDatasetSnapshotReference>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(reference => reference.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDatasetSnapshotReference>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(reference => reference.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZone>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(origin => origin.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneAgeBin>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginZone>()
            .WithMany()
            .HasForeignKey(bin => bin.OriginZoneId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneAgeBin>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(bin => bin.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginZone>()
            .WithMany()
            .HasForeignKey(period => period.OriginZoneId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginZoneIncomePeriod>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(period => period.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .HasOne<SaveNEIN.Server.Data.Entities.Jurisdiction>()
            .WithMany()
            .HasForeignKey(competitor => competitor.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(competitor => competitor.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitorHistory>()
            .HasOne<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .WithMany()
            .HasForeignKey(history => history.CasinoCompetitorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoCompetitorHistory>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(history => history.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>()
            .HasOne<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .WithMany()
            .HasForeignKey(period => period.CasinoCompetitorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CasinoGamingRevenuePeriod>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(period => period.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginFacilityTravel>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginZone>()
            .WithMany()
            .HasForeignKey(route => route.OriginZoneId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginFacilityTravel>()
            .HasOne<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .WithMany()
            .HasForeignKey(route => route.CasinoCompetitorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.OriginFacilityTravel>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(route => route.ModelRunId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CandidateLocationTravelCache>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginZone>()
            .WithMany()
            .HasForeignKey(route => route.OriginZoneId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginZone>()
            .WithMany()
            .HasForeignKey(result => result.OriginZoneId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFacilityResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .WithMany()
            .HasForeignKey(result => result.CasinoCompetitorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginZone>()
            .WithMany()
            .HasForeignKey(result => result.OriginZoneId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .HasOne<SaveNEIN.Server.Data.Entities.OriginFacilityTravel>()
            .WithMany()
            .HasForeignKey(result => result.OriginFacilityTravelId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunOriginFacilityAllocation>()
            .HasOne<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .WithMany()
            .HasForeignKey(result => result.CasinoCompetitorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TourismMarketObservation>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(observation => observation.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.TrafficCorridorObservation>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(observation => observation.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.LocalEconomicSectorObservation>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(observation => observation.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(component => component.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunDemandComponent>()
            .HasOne<SaveNEIN.Server.Data.Entities.DatasetSnapshot>()
            .WithMany()
            .HasForeignKey(component => component.DatasetSnapshotId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunCapacityDiagnostic>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(diagnostic => diagnostic.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunRampResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunGeographicAccounting>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunSectorDisplacement>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunEmploymentImpact>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunFiscalImpact>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunSocialCost>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunNetImpact>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .HasOne<SaveNEIN.Server.Data.Entities.BenchmarkStudy>()
            .WithMany()
            .HasForeignKey(validationCase => validationCase.BenchmarkStudyId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .HasOne<SaveNEIN.Server.Data.Entities.CasinoCompetitor>()
            .WithMany()
            .HasForeignKey(validationCase => validationCase.TargetCasinoCompetitorId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(validationCase => validationCase.ModelRunId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelParameterSet>()
            .WithMany()
            .HasForeignKey(evaluation => evaluation.PublishedParameterSetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .WithMany()
            .HasForeignKey(result => result.ValidationEvaluationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.ValidationCase>()
            .WithMany()
            .HasForeignKey(result => result.ValidationCaseId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationCaseResult>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(result => result.ModelRunId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ValidationGeographicResidualPattern>()
            .HasOne<SaveNEIN.Server.Data.Entities.ValidationEvaluation>()
            .WithMany()
            .HasForeignKey(pattern => pattern.ValidationEvaluationId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.ModelRunReportArtifact>()
            .HasOne<SaveNEIN.Server.Data.Entities.ModelRun>()
            .WithMany()
            .HasForeignKey(artifact => artifact.ModelRunId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SaveNEIN.Server.Data.Entities.CoalitionSignup>()
            .HasIndex(signup => signup.NormalizedEmail)
            .IsUnique();

        ConfigureModelFoundationColumnNames(modelBuilder);
    }

    private static void ConfigureModelFoundationColumnNames(ModelBuilder modelBuilder)
    {
        var modelFoundationTypes = new HashSet<Type>
        {
            typeof(Entities.Jurisdiction),
            typeof(Entities.JurisdictionRule),
            typeof(Entities.ModelParameterDefinition),
            typeof(Entities.ModelParameterSet),
            typeof(Entities.ModelParameterSetValue),
            typeof(Entities.ModelRun),
            typeof(Entities.ModelRunParameterValue),
            typeof(Entities.ModelRunParameterSetReference),
            typeof(Entities.DataSource),
            typeof(Entities.DatasetSnapshot),
            typeof(Entities.ModelRunDatasetSnapshotReference),
            typeof(Entities.OriginZone),
            typeof(Entities.OriginZoneAgeBin),
            typeof(Entities.OriginZoneIncomePeriod),
            typeof(Entities.CasinoCompetitorHistory),
            typeof(Entities.CasinoGamingRevenuePeriod),
            typeof(Entities.DevelopmentProgram),
            typeof(Entities.OriginFacilityTravel),
            typeof(Entities.CandidateLocationTravelCache),
            typeof(Entities.ModelRunOriginResult),
            typeof(Entities.ModelRunFacilityResult),
            typeof(Entities.ModelRunOriginFacilityAllocation),
            typeof(Entities.TourismMarketObservation),
            typeof(Entities.TrafficCorridorObservation),
            typeof(Entities.LocalEconomicSectorObservation),
            typeof(Entities.ModelRunDemandComponent),
            typeof(Entities.ModelRunCapacityDiagnostic),
            typeof(Entities.ModelRunRampResult),
            typeof(Entities.ModelRunGeographicAccounting),
            typeof(Entities.ModelRunSectorDisplacement),
            typeof(Entities.ModelRunEmploymentImpact),
            typeof(Entities.ModelRunFiscalImpact),
            typeof(Entities.ModelRunSocialCost),
            typeof(Entities.ModelRunNetImpact),
            typeof(Entities.BenchmarkStudy),
            typeof(Entities.ValidationCase),
            typeof(Entities.ValidationEvaluation),
            typeof(Entities.ValidationCaseResult),
            typeof(Entities.ValidationGeographicResidualPattern),
            typeof(Entities.ModelRunReportArtifact),
            typeof(Entities.SensitivityAnalysis),
            typeof(Entities.SensitivityAnalysisPoint)
        };

        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => modelFoundationTypes.Contains(entityType.ClrType)))
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    private static string ToSnakeCase(string value)
    {
        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1]))
            {
                characters.Add('_');
            }

            characters.Add(char.ToLowerInvariant(character));
        }

        return new string(characters.ToArray());
    }
}

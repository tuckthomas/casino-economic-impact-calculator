using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Reports;

namespace SaveNEIN.Server.Tests;

public sealed class ReportModelFactoryIntegrationTests
{
    [Fact]
    public async Task StoredRun_GeneratesDynamicOriginsAndOneImmutableArtifactPerPresentation()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        await using var db = CreateContext();
        var runId = await SeedFinalizedRunAsync(db);
        var factory = new CasinoImpactReportModelFactory(db);
        var options = new ReportPresentationOptions("Stored run report", "Verification", 10, "usd");

        var model = await factory.BuildAsync(
            runId,
            ReportArtifactService.TemplateVersion,
            new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc),
            options);

        Assert.Equal(["IN", "MI"], model.OriginStates.Select(group => group.GeographyCode).ToArray());
        Assert.Equal(1_000m, model.Origins.Sum(origin => origin.TotalProposedResidentGgr));
        Assert.Equal(1_000m, model.OriginStates.Sum(group => group.TotalProposedResidentGgr));
        Assert.Equal(1_000m, model.OriginCounties.Sum(group => group.TotalProposedResidentGgr));
        Assert.Equal(1d, model.OriginStates.Sum(group => group.ShareOfProposedResidentGgr), 10);
        Assert.Equal("zcta", model.Scenario.ComputationalOriginType);
        Assert.Equal("Dynamic origin verification", model.Scenario.Name);
        Assert.Equal("agi-share", model.Scenario.DemandSpecification);
        Assert.Equal("observed-ggr", model.Scenario.AttractionSpecification);
        Assert.Equal("inverse-power", model.Scenario.FrictionForm);
        Assert.Equal(41.08, model.Origins[0].Latitude, 6);
        Assert.Equal(-85.14, model.Origins[0].Longitude, 6);
        Assert.StartsWith("POLYGON", model.Origins[0].AreaGeometryWkt, StringComparison.Ordinal);
        Assert.Equal(12.5, model.Origins[0].CandidateTravelTimeMinutes);
        Assert.Equal(10_000, model.Origins[0].CandidateRoutedDistanceMeters);
        Assert.True(model.Origins[0].CandidateRouteFound);
        Assert.Equal(41.08, model.Facilities.Single(facility => facility.IsProposedFacility).Latitude, 6);
        Assert.Equal(-85.14, model.Facilities.Single(facility => facility.IsProposedFacility).Longitude, 6);
        Assert.StartsWith("effective-rules@2026-08-11#", model.Identity.JurisdictionProfileVersion, StringComparison.Ordinal);
        Assert.Equal(["First warning.", "Second warning."], model.Warnings);

        var service = new ReportArtifactService(
            db,
            factory,
            new HtmlReportRenderer(),
            new PdfReportRenderer(),
            new CsvReportRenderer());
        var first = await service.GetOrCreateAsync(runId, options);
        var second = await service.GetOrCreateAsync(
            runId,
            options with { Title = " Stored run report ", PreparedFor = " Verification ", CurrencyCode = "USD" });

        Assert.Equal(first.Id, second.Id);
        Assert.True(first.IsImmutable);
        Assert.Equal("professional-v5", first.TemplateVersion);
        Assert.Equal(64, first.ReportModelHash.Length);
        Assert.Equal(64, first.HtmlContentHash.Length);
        Assert.Equal(64, first.PdfContentHash.Length);
        Assert.Equal(64, first.CsvContentHash.Length);
        Assert.Contains("USA-ZCTA-46802", first.HtmlContent, StringComparison.Ordinal);
        Assert.Contains("County/parish composition", first.HtmlContent, StringComparison.Ordinal);
        Assert.Contains("Proposed site and competitive facilities", first.HtmlContent, StringComparison.Ordinal);
        Assert.Contains("Patron-origin intensity", first.HtmlContent, StringComparison.Ordinal);
        Assert.Contains("Patron-origin contribution choropleth", first.HtmlContent, StringComparison.Ordinal);
        Assert.Contains("Origin-to-candidate routed travel-time map", first.HtmlContent, StringComparison.Ordinal);
        Assert.Contains("origin_county,IN-003,total_proposed_resident_ggr,700,USD", first.CsvContent, StringComparison.Ordinal);
        Assert.Contains("origin,USA-ZCTA-46802,total_proposed_resident_ggr,700,USD", first.CsvContent, StringComparison.Ordinal);
        Assert.Equal(1, await db.ModelRunReportArtifacts.CountAsync());
    }

    [Fact]
    public async Task DraftRun_CannotGenerateAReportModel()
    {
        await using var db = CreateContext();
        var runId = await SeedFinalizedRunAsync(db);
        var run = await db.ModelRuns.SingleAsync(item => item.Id == runId);
        run.Status = ModelRunStatuses.Draft;
        run.FinalizedAtUtc = null;
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CasinoImpactReportModelFactory(db).BuildAsync(
                runId,
                ReportArtifactService.TemplateVersion,
                DateTime.UtcNow,
                new ReportPresentationOptions()));

        Assert.Contains("finalized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"report-model-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<Guid> SeedFinalizedRunAsync(AppDbContext db)
    {
        var jurisdiction = new Jurisdiction
        {
            Id = 1,
            Code = "US-IN",
            Name = "Indiana",
            Kind = "state"
        };
        var program = new DevelopmentProgram
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            StableProgramId = "integration-program",
            Version = "1",
            Name = "Integration development",
            SlotOrVltPositions = 1_000,
            TableGameCount = 40,
            StabilizedYearNumber = 3,
            IsImmutable = true
        };
        var run = new ModelRun
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            ModelVersion = "gravity-v1",
            Status = ModelRunStatuses.Finalized,
            JurisdictionId = jurisdiction.Id,
            DevelopmentProgramId = program.Id,
            CandidateLatitude = 41.08,
            CandidateLongitude = -85.14,
            CreatedAtUtc = new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc),
            FinalizedAtUtc = new DateTime(2026, 8, 11, 10, 1, 0, DateTimeKind.Utc),
            ResolvedInputJson = """
                {
                  "ScenarioName": "Dynamic origin verification",
                  "DemandSpecification": "agi-share",
                  "AttractionSpecification": "observed-ggr",
                  "FrictionForm": "inverse-power"
                }
                """,
            DataSnapshotReferencesJson = "{}",
            WarningSummary = "First warning. Second warning."
        };
        var originSnapshotId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var origins = new[]
        {
            new OriginZone
            {
                Id = 1,
                DatasetSnapshotId = originSnapshotId,
                StableOriginId = "USA-ZCTA-46802",
                OriginType = "zcta",
                GeographyCode = "46802",
                StateOrTerritoryCode = "IN",
                CountyEquivalentCode = "003",
                RepresentativePoint = new Point(-85.14, 41.08) { SRID = 4326 },
                AreaGeometry = Box(-85.16, 41.06, -85.12, 41.10)
            },
            new OriginZone
            {
                Id = 2,
                DatasetSnapshotId = originSnapshotId,
                StableOriginId = "USA-ZCTA-49014",
                OriginType = "zcta",
                GeographyCode = "49014",
                StateOrTerritoryCode = "MI",
                CountyEquivalentCode = "025",
                RepresentativePoint = new Point(-85.18, 42.32) { SRID = 4326 },
                AreaGeometry = Box(-85.20, 42.30, -85.16, 42.34)
            }
        };
        var originResults = new[]
        {
            Result(run.Id, 1, 1_200m, 600m, 100m, 700m),
            Result(run.Id, 2, 800m, 250m, 50m, 300m)
        };
        var proposed = new ModelRunFacilityResult
        {
            ModelRunId = run.Id,
            FacilityKey = "scenario:integration",
            FacilityKind = FacilityKinds.Scenario,
            IsProposedFacility = true,
            NormalizedAttraction = 1,
            WithProjectResidentGgr = 850m,
            ChangeInResidentGgr = 850m,
            InducedResidentGgr = 150m,
            TotalWithProjectResidentGgr = 1_000m,
            TourismGgr = 100m,
            TrafficGgr = 50m,
            StabilizedTotalGgr = 1_150m
        };
        var routes = new[]
        {
            CandidateRoute(run.Id, 1, 12.5, 10_000),
            CandidateRoute(run.Id, 2, 95, 145_000)
        };

        db.AddRange(jurisdiction, program, run);
        db.OriginZones.AddRange(origins);
        db.ModelRunOriginResults.AddRange(originResults);
        db.ModelRunFacilityResults.Add(proposed);
        db.OriginFacilityTravel.AddRange(routes);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private static ModelRunOriginResult Result(
        Guid runId,
        long originId,
        decimal demand,
        decimal redistributed,
        decimal induced,
        decimal total) => new()
        {
            ModelRunId = runId,
            OriginZoneId = originId,
            DemandSpecification = "agi-share",
            ResidentDemand = demand,
            ProposedResidentGgr = redistributed,
            ProposedInducedResidentGgr = induced,
            TotalProposedResidentGgr = total,
            HostJurisdictionCapture = redistributed * 0.5m,
            ExternalJurisdictionCapture = redistributed * 0.25m,
            OutsideOptionCapture = redistributed * 0.25m
        };

    private static OriginFacilityTravel CandidateRoute(Guid runId, long originId, double minutes, double meters) => new()
    {
        Id = originId,
        ModelRunId = runId,
        OriginZoneId = originId,
        FacilityKey = "scenario:integration",
        FacilityKind = FacilityKinds.Scenario,
        RoutingGraphHash = "test-graph",
        CostingProfile = "auto",
        TravelTimeMinutes = minutes,
        RoutedDistanceMeters = meters,
        RouteFound = true
    };

    private static Polygon Box(double minimumLongitude, double minimumLatitude, double maximumLongitude, double maximumLatitude)
    {
        var geometry = new GeometryFactory(new PrecisionModel(), 4326);
        return geometry.CreatePolygon([
            new Coordinate(minimumLongitude, minimumLatitude),
            new Coordinate(maximumLongitude, minimumLatitude),
            new Coordinate(maximumLongitude, maximumLatitude),
            new Coordinate(minimumLongitude, maximumLatitude),
            new Coordinate(minimumLongitude, minimumLatitude)
        ]);
    }
}

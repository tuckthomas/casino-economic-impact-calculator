// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using SaveNEIN.Client.Pages;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Shared.Modeling;

namespace SaveNEIN.Server.Tests;

public sealed class FacilityInputControlsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkbenchDisablesMissingInputsAndKeepsSupportedInputsSelectable(bool missingFloor)
    {
        var program = new DevelopmentProgram { SlotOrVltPositions = 1000, GamingFloorSquareFeet = 50000 };
        var availability = FacilityInputSelection.Availability(program,
            [new CasinoCompetitor { StableVenueId = "test-casino", SlotOrVltPositions = 500, GamingFloorSquareFeet = missingFloor ? null : 40000 }], "structural");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HttpClient(new CatalogHandler(availability)) { BaseAddress = new Uri("http://test/") });
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<EconomicImpact>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Embedded"] = true }));
            return root.ToHtmlString();
        });
        Assert.Contains("Inputs used to compare facilities", html);
        var floor = Regex.Match(html, "<input[^>]*aria-describedby=\"facility-input-gaming-floor-square-feet-status\"[^>]*>").Value;
        Assert.NotEmpty(floor);
        Assert.Equal(missingFloor, floor.Contains("disabled"));
        var positions = Regex.Match(html, "<input[^>]*aria-describedby=\"facility-input-gaming-positions-status\"[^>]*>").Value;
        Assert.NotEmpty(positions);
        Assert.DoesNotContain("disabled", positions);
        Assert.Contains("checked", positions);
        if (missingFloor) Assert.Contains("No reference value will be substituted", html);
    }

    [Fact]
    public async Task AvailabilityEndpointUsesTheActualOriginCompetitiveFieldAndSnapshot()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var origins = Guid.NewGuid(); var competitors = Guid.NewGuid(); var program = Guid.NewGuid();
        db.DatasetSnapshots.AddRange(new DatasetSnapshot { Id = origins, DatasetKey = DatasetSnapshotKinds.OriginGeography, IsSealed = true },
            new DatasetSnapshot { Id = competitors, DatasetKey = DatasetSnapshotKinds.Competitors, IsSealed = true });
        db.DevelopmentPrograms.Add(new DevelopmentProgram { Id = program, SlotOrVltPositions = 1000, GamingFloorSquareFeet = 50000 });
        db.OriginZones.Add(new OriginZone { Id = 1, DatasetSnapshotId = origins, StableOriginId = "local", RepresentativePoint = new Point(-85, 41) { SRID = 4326 }, AreaGeometry = new Polygon(new LinearRing([new(-85.01, 40.99), new(-84.99, 40.99), new(-84.99, 41.01), new(-85.01, 41.01), new(-85.01, 40.99)])) { SRID = 4326 } });
        db.CasinoCompetitors.AddRange(
            new CasinoCompetitor { Id = 1, DatasetSnapshotId = competitors, StableVenueId = "near", IsActive = true, HasSlots = true, VenueType = "commercial-casino", Latitude = 41, Longitude = -85, Geom = new Point(-85, 41) { SRID = 4326 }, SlotOrVltPositions = 500, GamingFloorSquareFeet = 40000 },
            new CasinoCompetitor { Id = 2, DatasetSnapshotId = competitors, StableVenueId = "far-missing", IsActive = true, HasSlots = true, VenueType = "commercial-casino", Latitude = 30, Longitude = -100, Geom = new Point(-100, 30) { SRID = 4326 }, SlotOrVltPositions = 500 });
        await db.SaveChangesAsync();
        var controller = new FacilityInputAvailabilityController(db, new CompetitiveUniverseService(db));
        var request = new FacilityInputAvailabilityRequest(program, competitors, origins, ["local"], new DateOnly(2025, 12, 31), "structural", 100);
        var result = Assert.IsType<OkObjectResult>(await controller.Get(request, default));
        var inputs = Assert.IsType<FacilityInputAvailability[]>(result.Value);
        Assert.True(inputs.Single(item => item.Key == "gaming-floor-square-feet").Available);
        var included = Assert.IsType<OkObjectResult>(await controller.Get(request with { CompetitorIds = [2] }, default));
        Assert.False(Assert.IsType<FacilityInputAvailability[]>(included.Value).Single(item => item.Key == "gaming-floor-square-feet").Available);
        Assert.IsType<BadRequestObjectResult>(await controller.Get(request with { StableOriginIds = ["absent"] }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.Get(request with { CompetitorSnapshotId = Guid.NewGuid() }, default));
    }

    private sealed class CatalogHandler(FacilityInputAvailability[] availability) : HttpMessageHandler
    {
        private readonly Guid _program = Guid.NewGuid();
        private readonly string[] _kinds = ["origin-geography", "age-population", "income", "competitors", "observed-performance"];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object body;
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/facility-input-availability")) body = availability;
            else if (path.EndsWith("/origins")) body = new { Total = 1, Origins = new[] { new { StableOriginId = "local" } } };
            else if (path.EndsWith("/gravity-model-runs")) body = Array.Empty<object>();
            else if (path.EndsWith("/gravity-model-configuration")) body = new
            {
                ModelVersion = "gravity-v1", Jurisdictions = new[] { new { Id = 1, Code = "US-IN", Name = "Indiana", Kind = "state" } },
                DevelopmentPrograms = new[] { new { Id = _program, Name = "Test program", Version = "1" } },
                Snapshots = _kinds.Select(kind => new { Id = Guid.NewGuid(), DatasetKey = kind, PeriodEnd = new DateOnly(2025, 12, 31), PeriodStart = new DateOnly(2025, 1, 1), RowCount = 1 }),
                ParameterDefinitions = new[] { new { Key = "facility.gaming_positions_coefficient", Category = "facility", DisplayName = "Gaming positions weight", SystemDefaultValue = 1, IsUserOverridable = true } },
                ParameterSets = Array.Empty<object>(), ParameterSetValues = Array.Empty<object>()
            };
            else throw new InvalidOperationException("Unexpected fixture HTTP request: " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
        }
    }
}

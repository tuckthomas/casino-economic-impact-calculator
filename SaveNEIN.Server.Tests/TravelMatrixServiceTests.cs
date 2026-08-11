// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Server.Services.Gravity;
using SaveNEIN.Server.Services.Valhalla;

namespace SaveNEIN.Server.Tests;

public sealed class TravelMatrixServiceTests
{
    [Fact]
    public async Task ExactCandidateCoordinateHash_ReusesValhallaRouteAndMaterializesPerRunEvidence()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"candidate-route-cache-{Guid.NewGuid():N}")
            .Options);
        var handler = new ValhallaMatrixHandler();
        var client = new ValhallaClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://valhalla.test") },
            NullLogger<ValhallaClient>.Instance);
        var service = new TravelMatrixService(db, client);
        var origin = new TravelMatrixOrigin(1, "USA-ZCTA-46802", 41.08, -85.14);
        var firstRunId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var secondRunId = Guid.Parse("20000000-0000-0000-0000-000000000002");

        var first = await service.ResolveAsync(
            [origin],
            [new TravelMatrixFacility("scenario:first", FacilityKinds.Scenario, null, firstRunId, 41.0793, -85.1394)]);
        var second = await service.ResolveAsync(
            [origin],
            [new TravelMatrixFacility("scenario:second", FacilityKinds.Scenario, null, secondRunId, 41.0793, -85.1394)]);

        Assert.Equal(1, handler.MatrixRequestCount);
        var cache = Assert.Single(await db.CandidateLocationTravelCache.AsNoTracking().ToListAsync());
        Assert.Equal(TravelMatrixService.CandidateCoordinateHash(41.0793, -85.1394), cache.CandidateCoordinateHash);
        Assert.Equal(41.0793, cache.CandidateLatitude);
        Assert.Equal(-85.1394, cache.CandidateLongitude);
        Assert.Equal(first.RoutingGraphHash, cache.RoutingGraphHash);
        Assert.Equal("3.5.1", cache.ValhallaVersion);
        Assert.Equal(10, first.Routes.Single().TravelTimeMinutes);
        Assert.Equal(12_500, second.Routes.Single().RoutedDistanceMeters);
        Assert.Equal(secondRunId, second.Routes.Single().ModelRunId);
        Assert.Equal(2, await db.OriginFacilityTravel.CountAsync());
    }

    [Fact]
    public void CandidateCoordinateHash_PreservesExactCoordinatesInsteadOfRoundingToNearbySite()
    {
        var exact = TravelMatrixService.CandidateCoordinateHash(41.0793, -85.1394);
        var nearby = TravelMatrixService.CandidateCoordinateHash(41.0793001, -85.1394);

        Assert.NotEqual(exact, nearby);
        Assert.Equal(64, exact.Length);
    }

    private sealed class ValhallaMatrixHandler : HttpMessageHandler
    {
        public int MatrixRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/status")
            {
                return Task.FromResult(Json("""
                    {"version":"3.5.1","tileset_last_modified":123456789}
                    """));
            }
            if (request.RequestUri?.AbsolutePath == "/sources_to_targets")
            {
                MatrixRequestCount++;
                return Task.FromResult(Json("""
                    {
                      "algorithm":"timedistancematrix",
                      "units":"kilometers",
                      "sources_to_targets":[[{"time":600,"distance":12.5}]]
                    }
                    """));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

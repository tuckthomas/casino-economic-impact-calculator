using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SaveNEIN.Server.Services.Valhalla;

namespace SaveNEIN.Server.Tests;

public sealed class ValhallaMatrixClientTests
{
    [Fact]
    public async Task Matrix_UsesValhallaDriveTimesAndConvertsDocumentedUnits()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/status" => Json("""
                {"version":"3.5.1","tileset_last_modified":1786233600}
                """),
            "/sources_to_targets" => Json("""
                {
                  "algorithm":"costmatrix",
                  "units":"kilometers",
                  "sources_to_targets":[
                    [
                      {"from_index":0,"to_index":0,"time":900,"distance":12.5},
                      {"from_index":0,"to_index":1,"time":null,"distance":null}
                    ]
                  ]
                }
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://valhalla.test") };
        var client = new ValhallaClient(httpClient, NullLogger<ValhallaClient>.Instance);

        var graph = await client.GetRoutingGraphIdentityAsync();
        var matrix = await client.GetDriveTimeMatrixAsync(
            [new ValhallaMatrixLocation(41.08, -85.14)],
            [
                new ValhallaMatrixLocation(41.68, -86.25),
                new ValhallaMatrixLocation(42.33, -83.05)
            ]);

        Assert.Equal("3.5.1", graph.ValhallaVersion);
        Assert.Equal(64, graph.GraphHash.Length);
        Assert.Equal(15, matrix.Cells[0].TravelTimeMinutes);
        Assert.Equal(12_500, matrix.Cells[0].RoutedDistanceMeters);
        Assert.True(matrix.Cells[0].RouteFound);
        Assert.False(matrix.Cells[1].RouteFound);
        Assert.Null(matrix.Cells[1].TravelTimeMinutes);

        Assert.Contains(handler.RequestBodies, body =>
            body.Contains("\"costing\":\"auto\"", StringComparison.Ordinal) &&
            body.Contains("\"verbose\":true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Matrix_ReportsValhallaFailureReasonWithoutFallingBackToStraightLineTravel()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                "{\"error\":\"Path distance exceeds the max distance limit\"}",
                Encoding.UTF8,
                "application/json")
        });
        var client = new ValhallaClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://valhalla.test") },
            NullLogger<ValhallaClient>.Instance);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetDriveTimeMatrixAsync(
            [new ValhallaMatrixLocation(41, -85)],
            [new ValhallaMatrixLocation(42, -86)]));

        Assert.Contains("Path distance exceeds", error.Message, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return responseFactory(request);
        }
    }
}

using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class IllinoisGamingBoardProviderTests
{
    [Fact]
    public async Task RevenueProvider_PreservesIllinoisAgrAndComparableCrosswalk()
    {
        var handler = new IllinoisReportHandler(CasinoSummaryCsv);
        var provider = new IllinoisGamingBoardRevenueProvider(
            new HttpClient(handler),
            Options.Create(TestOptions()));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IL",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));

        Assert.Equal(DatasetSnapshotKinds.ObservedPerformance, dataset.DatasetKey);
        Assert.Equal(2, dataset.Rows.Count);
        Assert.All(dataset.Rows, row => Assert.Equal("USA-IL-IGB-hard-rock-casino-rockford", row.StableVenueId));
        Assert.Equal(
            146_186_677.59m,
            dataset.Rows.Single(row => row.ReportedMetricKey == "illinois-total-agr").ReportedAmount);
        Assert.Equal(
            146_186_677.59m,
            dataset.Rows.Single(row => row.ReportedMetricKey == GamingRevenueMetricKeys.ComparableLandBasedGamingRevenue).ReportedAmount);
        Assert.All(dataset.Rows, row => Assert.Equal("annual", row.PeriodGranularity));
        Assert.Contains("CasinoReportTypes=Casino+Summary", handler.PostBody);
        Assert.Contains("SearchStartYear=2025", handler.PostBody);
        Assert.Equal(64, dataset.ContentChecksum.Length);
    }

    [Fact]
    public void RevenueParser_RejectsAgrThatDoesNotReconcile()
    {
        var invalid = CasinoSummaryCsv.Replace("146186677.5900", "146186678.5900", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            IllinoisGamingBoardRevenueProvider.ParseCasinoSummaryCsv(invalid));
    }

    [Fact]
    public void LicenseParser_UsesLicensedDbaAddressAndSkipsApplicants()
    {
        var rows = IllinoisGamingBoardFacilityInventoryProvider.ParseLicensedFacilities(
            Encoding.UTF8.GetBytes(OwnerLicenseJson));

        var row = Assert.Single(rows);
        Assert.Equal("USA-IL-IGB-hard-rock-casino-rockford", row.StableVenueId);
        Assert.Equal("Hard Rock Casino Rockford", row.DisplayName);
        Assert.Equal("815 Entertainment, LLC", row.OperatorName);
        Assert.Equal("7801 East State Street, Rockford, IL 61108", row.FullAddress);
        Assert.False(row.IsOrganizationGaming);
    }

    [Fact]
    public async Task FacilityProvider_IntersectsRevenueUniverseWithLicensedFacilitiesAndGeocodesAddress()
    {
        var configured = TestOptions();
        var revenueProvider = new IllinoisGamingBoardRevenueProvider(
            new HttpClient(new IllinoisReportHandler(CasinoSummaryCsv)),
            Options.Create(configured));
        var facilityHandler = new IllinoisFacilityHandler(OwnerLicenseJson);
        var provider = new IllinoisGamingBoardFacilityInventoryProvider(
            new HttpClient(facilityHandler),
            revenueProvider,
            Options.Create(configured));

        var dataset = await provider.FetchAsync(new ProviderFetchRequest(
            "US-IL",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)));

        var row = Assert.Single(dataset.Rows);
        Assert.Equal("Hard Rock Casino Rockford", row.Name);
        Assert.Equal(67_000, row.GamingFloorSquareFeet);
        Assert.True(row.HasSlots);
        Assert.True(row.HasTableGames);
        Assert.Null(row.SlotOrVltPositions);
        Assert.Equal(42.27054506646, row.Latitude, 10);
        Assert.Equal(-88.962631062121, row.Longitude, 10);
        Assert.Contains("score 100.0", row.Notes);
        Assert.Single(dataset.Warnings);
    }

    [Fact]
    public async Task CompositeProviders_SelectRequestedJurisdictionsAndRejectUnknownCoverage()
    {
        var inProvider = new StubFacilityProvider("US-IN", "USA-IN-one");
        var ilProvider = new StubFacilityProvider("US-IL", "USA-IL-one");
        var ohCasinoProvider = new StubFacilityProvider("US-OH", "USA-OH-casino");
        var ohRacinoProvider = new StubFacilityProvider("US-OH", "USA-OH-racino");
        var composite = new CompositeGamingFacilityInventoryProvider(
            [inProvider, ilProvider, ohCasinoProvider, ohRacinoProvider]);
        var request = new ProviderFetchRequest(
            "US-IN,US-IL",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31));

        var dataset = await composite.FetchAsync(request);

        Assert.Equal(2, dataset.Rows.Count);
        Assert.Equal("US-IL", ilProvider.LastRequest!.GeographicCoverage);
        Assert.Equal("US-IN", inProvider.LastRequest!.GeographicCoverage);
        Assert.Equal(64, dataset.ContentChecksum.Length);
        var ohio = await composite.FetchAsync(request with { GeographicCoverage = "US-OH" });
        Assert.Equal(2, ohio.Rows.Count);
        Assert.Equal("US-OH", ohCasinoProvider.LastRequest!.GeographicCoverage);
        Assert.Equal("US-OH", ohRacinoProvider.LastRequest!.GeographicCoverage);
        await Assert.ThrowsAsync<NotSupportedException>(() => composite.FetchAsync(
            request with { GeographicCoverage = "US-KY" }));
    }

    private static IllinoisGamingBoardProviderOptions TestOptions() => new()
    {
        ReportsApplicationUrl = "https://example.test/reports",
        ReportsPublicationUrl = "https://example.test/report-index",
        LicenseesPublicationUrl = "https://example.test/license-index",
        OrganizationLicenseesJsonUrl = "https://example.test/organization.json",
        OwnerLicenseesJsonUrl = "https://example.test/owners.json",
        GeocoderUrl = "https://example.test/geocode"
    };

    private const string CasinoSummaryCsv = "\"Casino Summary\"\r\n" +
        "\"January 2025 - December 2025\"\r\n\r\n" +
        "\"Casino\",\"Square Feet\",\"Admissions\",\"Operating Days\",\"Table Game AGR\",\"EGD AGR\",\"Total AGR\",\r\n" +
        "\"Hard Rock Casino Rockford\",\"67000\",\"1433251\",\"365\",\"20268849.8000\",\"125917827.7900\",\"146186677.5900\",\r\n";

    private const string OwnerLicenseJson = """
        {
          "listItems": [
            {
              "businessName": "815 Entertainment, LLC (Licensed)",
              "licenseType": "Owners Applicants & Licensees",
              "licenseStatus": "Licensed",
              "licenseDisplay": "d/b/a Hard Rock Casino Rockford<BR>7801 East State Street<BR>Rockford, IL 61108<BR><strong>License Status: Licensed - 1/27/2022</strong>"
            },
            {
              "businessName": "Applicant Only, LLC (Preliminarily Suitable)",
              "licenseType": "Owners Applicants & Licensees",
              "licenseStatus": "Preliminarily Suitable",
              "licenseDisplay": "d/b/a Not Operating<BR>1 Main Street<BR>Chicago, IL 60601"
            }
          ]
        }
        """;

    private sealed class IllinoisReportHandler(string csv) : HttpMessageHandler
    {
        public string PostBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                PostBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(csv, Encoding.UTF8, "text/csv")
                };
            }
            const string form = "<input type=\"hidden\" name=\"__VIEWSTATE\" value=\"state\" />" +
                                "<input type=\"hidden\" id=\"__VIEWSTATEGENERATOR\" value=\"generator\" />" +
                                "<input type=\"hidden\" name=\"__EVENTVALIDATION\" value=\"validation\" />";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(form, Encoding.UTF8, "text/html")
            };
        }
    }

    private sealed class IllinoisFacilityHandler(string ownerLicenseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = request.RequestUri!.AbsolutePath switch
            {
                "/organization.json" => "{\"listItems\":[]}",
                "/owners.json" => ownerLicenseJson,
                "/geocode" => "{\"candidates\":[{\"address\":\"7801 E State St, Rockford, Illinois, 61108\",\"location\":{\"x\":-88.962631062121,\"y\":42.27054506646},\"score\":100}]}",
                _ => throw new InvalidOperationException($"Unexpected test URI {request.RequestUri}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubFacilityProvider(string coverage, string stableVenueId) : IGamingFacilityInventoryProvider
    {
        public string ProviderKey => $"stub-{stableVenueId}";
        public string GeographicCoverage => coverage;
        public ProviderFetchRequest? LastRequest { get; private set; }

        public Task<ProviderDataset<CasinoCompetitorImportRow>> FetchAsync(
            ProviderFetchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var row = new CasinoCompetitorImportRow(
                StableVenueId: stableVenueId,
                Name: stableVenueId,
                State: coverage[3..],
                CountryCode: "USA",
                VenueType: "commercial-casino",
                FacilityRegime: null,
                RegulatoryStatus: "active",
                JurisdictionId: null,
                RegulatorName: "regulator",
                RegulatorLicenseId: null,
                TribalNationName: null,
                OpenedOn: null,
                ClosedOn: null,
                County: null,
                City: null,
                Latitude: 40,
                Longitude: -87,
                IsActive: true,
                OperatorName: null,
                SourceUrl: null,
                LastVerifiedAt: null,
                HasSlots: true,
                HasTableGames: true,
                HasPoker: null,
                HasSportsbook: null,
                HasRacetrack: null,
                HasHotel: null,
                HasRestaurants: null,
                HasEntertainment: null,
                HasLoyaltyProgram: null,
                HasResortAmenities: null,
                GamingPositions: null,
                SlotOrVltPositions: null,
                TableGameCount: null,
                PokerTableCount: null,
                GamingFloorSquareFeet: null,
                HotelRoomCount: null,
                EventCapacity: null,
                FoodBeverageVenueCount: null,
                DevelopmentCost: null,
                DevelopmentCostDollarYear: null,
                AccessContext: null,
                LimitedAccessDistanceMiles: null,
                HasInterchangeAccess: null,
                MarketOrientation: null,
                IsBorderMarket: null,
                Notes: null);
            var checksum = new string(coverage[^1], 64);
            return Task.FromResult(new ProviderDataset<CasinoCompetitorImportRow>(
                new RegisterDataSourceRequest(
                    coverage, coverage, $"https://example.test/{coverage}", "test", coverage, "2025",
                    DateTime.UtcNow, checksum, true, null, null),
                DatasetSnapshotKinds.Competitors,
                "2025",
                request.PeriodStart,
                request.PeriodEnd,
                checksum,
                "test-v1",
                [row],
                []));
        }
    }
}

using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public sealed class MichiganGamingRevenueProviderTests
{
    [Fact]
    public void WorkbookParser_ReconcilesMonthlyFacilityAndAnnualTotals()
    {
        var rows = CompleteWorkbookCells();

        var result = MichiganDetroitRevenueWorkbookParser.ParseCells(rows, 2025);

        Assert.Equal(12, result.Count);
        Assert.Equal(1m, result[0].MgmGrandDetroit);
        Assert.Equal(36m, result[11].HollywoodGreektown);
        Assert.Equal(468m, result.Sum(row => row.AllDetroitCasinos));
    }

    [Fact]
    public void WorkbookParser_RejectsMonthlyAllCasinoMismatch()
    {
        var rows = CompleteWorkbookCells();
        rows[2][7] = 6.01m;

        var exception = Assert.Throws<InvalidDataException>(() =>
            MichiganDetroitRevenueWorkbookParser.ParseCells(rows, 2025));

        Assert.Contains("does not reconcile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkbookLink_IsDiscoveredFromAuthoritativeIndexAndDecoded()
    {
        const string html = """
            <a href="/-/media/Project/Websites/mgcb/Detroit-Casino-Revenue-Files/Detroit_Casino_Revenue-2025-XLS.xls?hash=abc&amp;rev=def">Excel</a>
            """;

        var result = MichiganGamingControlBoardRevenueProvider.ResolveWorkbookUrl(
            html,
            "https://www.michigan.gov/mgcb/Detroit-Casinos/resources/revenues-and-wagering-tax-information",
            2025);

        Assert.Equal(
            "https://www.michigan.gov/-/media/Project/Websites/mgcb/Detroit-Casino-Revenue-Files/Detroit_Casino_Revenue-2025-XLS.xls?hash=abc&rev=def",
            result);
    }

    [Fact]
    public async Task Provider_RejectsPartialPeriodBeforeNetworkAccess()
    {
        var handler = new CountingHandler();
        var provider = new MichiganGamingControlBoardRevenueProvider(
            new HttpClient(handler),
            Options.Create(new MichiganGamingFacilityProviderOptions()));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.FetchAsync(new ProviderFetchRequest(
            "US-MI",
            new DateOnly(2025, 1, 2),
            new DateOnly(2025, 1, 31))));

        Assert.Equal(0, handler.RequestCount);
    }

    private static List<object?[]> CompleteWorkbookCells()
    {
        var rows = new List<object?[]>
        {
            new object?[] { null, "MGM GRAND DETROIT", null, "MOTORCITY CASINO", null, "GREEKTOWN CASINO", null, "All Detroit Casinos" },
            new object?[] { "Month", "Total Adjusted Revenue", null, "Total Adjusted Revenue", null, "Total Adjusted Revenue", null, "Total Adjusted Gross Receipts" }
        };
        decimal mgmTotal = 0;
        decimal motorCityTotal = 0;
        decimal greektownTotal = 0;
        for (var month = 1; month <= 12; month++)
        {
            var mgm = (decimal)month;
            var motorCity = month * 2m;
            var greektown = month * 3m;
            mgmTotal += mgm;
            motorCityTotal += motorCity;
            greektownTotal += greektown;
            rows.Add(
            [
                CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month),
                mgm,
                null,
                motorCity,
                null,
                greektown,
                null,
                mgm + motorCity + greektown
            ]);
        }
        rows.Add(["Total", mgmTotal, null, motorCityTotal, null, greektownTotal, null, mgmTotal + motorCityTotal + greektownTotal]);
        return rows;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}

using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Tests;

public sealed class LocalEconomyProfileTests
{
    private static Dictionary<string, string> Row(string code, string jobs, string disclosure = "") => new()
    {
        ["industry_code"] = code, ["own_code"] = "5", ["size_code"] = "0", ["disclosure_code"] = disclosure,
        ["annual_avg_emplvl"] = jobs, ["annual_avg_estabs"] = "10", ["avg_annual_pay"] = "50000", ["total_annual_wages"] = "100000"
    };

    [Fact]
    public void SuppressedZeroFilledJobsAndWagesStayNullWhilePublicEstablishmentCountRemainsAvailable()
    {
        var rows = LocalEconomyProfileService.BuildIndustries([Row("10", "100"), Row("72", "0", "N")], [], [], []);
        var industry = Assert.Single(rows, row => row.Code == "72");
        Assert.True(industry.Suppressed);
        Assert.Null(industry.Jobs);
        Assert.Null(industry.AnnualPay);
        Assert.Null(industry.Share);
        Assert.Null(industry.ChangeFiveYear);
        Assert.Equal(10, industry.Establishments);
    }

    [Fact]
    public void TrendsUseSamePrivateOwnershipAndSpecializationUsesPrivateCountyAndNationalDenominators()
    {
        var rows = LocalEconomyProfileService.BuildIndustries([Row("10", "1000"), Row("72", "200")],
            [Row("72", "100")], [Row("72", "0")], [Row("10", "10000"), Row("72", "1000")]);
        var industry = Assert.Single(rows, row => row.Code == "72");
        Assert.Equal(20, industry.Share);
        Assert.Equal(100, industry.ChangeFiveYear);
        Assert.Null(industry.ChangeTenYear);
        Assert.Equal(2, industry.LocationQuotient);
    }

    [Fact]
    public void ParserRejectsUnexpectedSchemasInsteadOfConvertingUpstreamErrorPagesIntoZeroData()
    {
        Assert.Throws<InvalidDataException>(() => LocalEconomyProfileService.ParseCsv("<html>Missing data</html>"));
        var rows = LocalEconomyProfileService.ParseCsv("\"own_code\",\"size_code\",\"industry_code\",\"disclosure_code\",\"annual_avg_emplvl\"\n\"5\",\"0\",\"72\",\"N\",\"0\"\n");
        Assert.Single(rows);
        Assert.Equal("N", rows[0]["disclosure_code"]);
    }
}

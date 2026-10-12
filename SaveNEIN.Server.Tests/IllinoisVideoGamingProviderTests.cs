using System.Text;
using SaveNEIN.Server.Services.Providers;

namespace SaveNEIN.Server.Tests;

public class IllinoisVideoGamingProviderTests
{
    private const string Header = "\"All Establishment Details\"\n\"January 2025 - December 2025\"\n\"Report Date: fixture\"\n" +
        "\"Establishment\",\"License Number\",\"Municipality\",\"VGT Count\",\"Amount Played\",\"Amount Won\",\"Net Wager\",\"Funds In\",\"Funds Out\",\"Net Terminal Income\",\"NTI Tax\",\"State Share\",\"Municipality Share\"\n";
    private const string Row = "\"Example, Inc.\",\"140700460\",\"Springfield\",\"6\",\"1000\",\"900\",\"100\",\"550\",\"449.70\",\"100.30\",\"35.10\",\"30.10\",\"5\"\n";
    private static object Parse(string csv, int year = 2025) => IllinoisVideoGamingProvider.ParseCapture(Encoding.UTF8.GetBytes(csv), year, DateTime.UtcNow);

    [Fact]
    public void PreservesNtiSeparatelyFromTurnoverAndNetWager()
    {
        var capture = IllinoisVideoGamingProvider.ParseCapture(Encoding.UTF8.GetBytes(Header + Row), 2025, DateTime.UtcNow);
        var row = Assert.Single(capture.Rows);
        Assert.Equal(1000m, row.AmountPlayed);
        Assert.Equal(100m, row.NetWager);
        Assert.Equal(100.30m, row.NetTerminalIncome);
        Assert.Equal(6, row.ReportedVgtCount);
        Assert.NotEmpty(capture.Warnings);
    }

    [Fact]
    public void RejectsWrongObservationYear() => Assert.Throws<InvalidDataException>(() => Parse(Header + Row, 2024));

    [Fact]
    public void RejectsDuplicateObservation() => Assert.Throws<InvalidDataException>(() => Parse(Header + Row + Row));

    [Fact]
    public void PreservesPublishedNtiAndFlagsSourceFundsDiscrepancy()
    {
        var capture = IllinoisVideoGamingProvider.ParseCapture(Encoding.UTF8.GetBytes(Header + Row.Replace("\"100.30\"", "\"1000\"", StringComparison.Ordinal)), 2025, DateTime.UtcNow);
        var row = Assert.Single(capture.Rows);
        Assert.Equal(1000m, row.NetTerminalIncome);
        Assert.Contains("reported-nti-differs-from-funds-subtraction", row.AnomalyFlags);
    }

    [Fact]
    public void RetainsDifferentMunicipalityRowsForTheSameLicense()
    {
        var capture = IllinoisVideoGamingProvider.ParseCapture(Encoding.UTF8.GetBytes(Header + Row + Row.Replace("Springfield", "Sangamon County", StringComparison.Ordinal)), 2025, DateTime.UtcNow);
        Assert.Equal(2, capture.Rows.Count);
        Assert.Single(capture.Rows.Select(row => row.StableLicenseId).Distinct());
    }

    [Fact]
    public void PriorPeriodAdjustmentDoesNotInventAnEstablishmentLicense()
    {
        var source = Row.Replace("Example, Inc.", "Prior Period Adjustment", StringComparison.Ordinal)
            .Replace("140700460", "", StringComparison.Ordinal).Replace("\"6\"", "\"0\"", StringComparison.Ordinal);
        var capture = IllinoisVideoGamingProvider.ParseCapture(Encoding.UTF8.GetBytes(Header + source), 2025, DateTime.UtcNow);
        var row = Assert.Single(capture.Rows);
        Assert.Null(row.StableLicenseId);
        Assert.True(row.IsPriorPeriodAdjustment);
        Assert.Equal(0, row.ReportedVgtCount);
    }
}

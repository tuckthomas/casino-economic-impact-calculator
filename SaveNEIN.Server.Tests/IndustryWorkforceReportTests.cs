using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Controllers;
using SaveNEIN.Server.Services;
using SaveNEIN.Server.Services.Reports;
using SaveNEIN.Shared;
using UglyToad.PdfPig;

namespace SaveNEIN.Server.Tests;

public sealed class IndustryWorkforceReportTests
{
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SaveNEIN.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Theory]
    [InlineData("18151", "Northern Indiana nonmetropolitan area")]
    [InlineData("18033", "Northern Indiana nonmetropolitan area")]
    [InlineData("18003", "Fort Wayne, IN")]
    public async Task PackagedCountyEvidenceReconcilesAndAppearsInRenderedReport(string fips, string expectedWageArea)
    {
        using var http = new HttpClient();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new LocalEconomyProfileService(http, new TestEnvironment { ContentRootPath = Path.Combine(RepositoryRoot(), "SaveNEIN.Server") }, cache, NullLogger<LocalEconomyProfileService>.Instance);
        var profile = Assert.IsType<LocalEconomyProfile>(await service.GetAsync(fips, CancellationToken.None));
        Assert.Equal(expectedWageArea, profile.WageAreaName);
        Assert.Equal(TimeSpan.Zero, profile.RetrievedAtUtc.Offset);
        Assert.True(profile.PackagedIndustrySnapshot);
        Assert.Equal(22, profile.Industries.Count);
        Assert.Equal(7, profile.Occupations.Count);
        var workforce = Assert.IsType<WorkforceProfile>(profile.Workforce);
        Assert.Equal(workforce.ResidentWorkers.Value, workforce.WorkInCounty.Value + workforce.WorkElsewhereInState.Value + workforce.WorkOutsideState.Value);
        Assert.Equal(workforce.CivilianLaborForce.Value, workforce.Employed.Value + workforce.Unemployed.Value);
        var monthly = Assert.IsType<MonthlyLaborMarket>(profile.CurrentLaborMarket);
        Assert.Equal(monthly.LaborForce, monthly.Employed + monthly.Unemployed);
        QuestPDF.Settings.License = LicenseType.Community;
        var bytes = LegacyEconomicReportDocument.Render(new ReportRequest
        {
            SubjectCountyName = "Test", LocalEconomy = new LocalEconomyReport { Profile = profile },
            AnalysisText = "### Industry Composition and Trends\n* Source periods are retained.\n### Workforce and Recruitment Analysis\n* Hiring assignments are conditional."
        }, null, new DateTime(2026, 11, 2));
        using var pdf = PdfDocument.Open(bytes);
        var text = string.Join(" ", pdf.GetPages().Select(page => page.Text));
        Assert.Contains("Industry composition and trends", text);
        Assert.Contains("Resident workforce and commuting", text);
        Assert.Contains("Occupational and industry pay", text);
        Assert.Contains("Recruitment scenario", text);
        // Glyph-order PDF extraction can omit whitespace at wrapped line boundaries.
        Assert.Contains(string.Concat(expectedWageArea.Where(character => !char.IsWhiteSpace(character))),
            string.Concat(text.Where(character => !char.IsWhiteSpace(character))));
        Assert.Contains("2026-09", text);
        Assert.Contains("No supporting information supplied", text);
        Assert.True(pdf.NumberOfPages >= 9);
    }
}

// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using SaveNEIN.Shared;

namespace SaveNEIN.Server.Services;

/// <summary>Public descriptive evidence only. Does not alter the calculator's financial model.</summary>
public sealed class LocalEconomyProfileService(HttpClient http, IWebHostEnvironment environment, IMemoryCache cache,
    ILogger<LocalEconomyProfileService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim UpstreamLimit = new(4);
    private static readonly Dictionary<string, string> Sectors = new()
    {
        ["11"] = "Agriculture, forestry and fishing", ["21"] = "Mining", ["22"] = "Utilities", ["23"] = "Construction",
        ["31-33"] = "Manufacturing", ["42"] = "Wholesale trade", ["44-45"] = "Retail trade",
        ["48-49"] = "Transportation and warehousing", ["51"] = "Information", ["52"] = "Finance and insurance",
        ["53"] = "Real estate and leasing", ["54"] = "Professional and technical services", ["55"] = "Company management",
        ["56"] = "Administrative and support services", ["61"] = "Educational services", ["62"] = "Health care and social assistance",
        ["71"] = "Arts, entertainment and recreation", ["72"] = "Accommodation and food services", ["81"] = "Other services",
        ["721"] = "Accommodation (within sector 72)", ["722"] = "Food services (within sector 72)"
    };

    public async Task<LocalEconomyProfile?> GetAsync(string fips, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue<LocalEconomyProfile>("local-economy:" + fips, out var cached)) return cached;
        var acs = await ReadSnapshot<AcsSnapshot>("acs-2024-counties.json", cancellationToken);
        if (acs is null || !acs.Counties.TryGetValue(fips, out var workforce)) return null;
        var profile = new LocalEconomyProfile { CountyFips = fips, Workforce = workforce, RetrievedAtUtc = acs.RetrievedAtUtc };
        var monthly = await ReadSnapshot<LausSnapshot>("laus-counties.json", cancellationToken);
        profile.CurrentLaborMarket = monthly?.Counties.GetValueOrDefault(fips);
        if (profile.CurrentLaborMarket is not null)
            profile.SourceUrls.Add($"https://data.bls.gov/timeseries/LAUCN{fips}0000000003");
        var tasks = new[] { 2025, 2020, 2015 }.Select(year => Qcew(fips, year, cancellationToken)).ToArray();
        var periods = await Task.WhenAll(tasks);
        var national = await Qcew("US000", 2025, cancellationToken);
        profile.Industries = BuildIndustries(periods[0]?.Rows ?? [], periods[1]?.Rows ?? [], periods[2]?.Rows ?? [], national?.Rows ?? []);
        profile.PackagedIndustrySnapshot = periods[0]?.Packaged == true;
        foreach (var period in periods.Where(period => period is not null))
            profile.SourceUrls.Add($"https://data.bls.gov/cew/data/api/{period!.Year}/a/area/{fips}.csv");
        profile.SourceUrls.Add("https://data.bls.gov/cew/data/api/2025/a/area/US000.csv");
        profile.SourceUrls.Add("https://www2.census.gov/programs-surveys/acs/summary_file/2024/table-based-SF/data/5YRData/acsdt5y2024-b23025.dat");
        profile.SourceUrls.Add("https://www2.census.gov/programs-surveys/acs/summary_file/2024/table-based-SF/data/5YRData/acsdt5y2024-b08007.dat");
        if (periods[0] is null) profile.Notices.Add("BLS industry data could not be retrieved. Workforce evidence remains available; industry values have not been substituted.");
        if (periods[1] is null || periods[2] is null) profile.Notices.Add("One or more historical BLS periods are unavailable. Unavailable changes are not treated as zero.");
        var wages = await ReadSnapshot<WageSnapshot>("oews-2025-regions.json", cancellationToken);
        var wageArea = fips switch { "18151" or "18033" => "1800001", "18003" => "0023060", _ => "" };
        var area = wages?.Areas.FirstOrDefault(area => area.AreaCode == wageArea);
        if (area is not null)
        {
            profile.WageAreaName = area.AreaName;
            profile.Occupations = area.Occupations;
            profile.WageSourceUrl = $"https://data.bls.gov/oes/#/area/{wageArea}/2025";
            profile.SourceUrls.Add(profile.WageSourceUrl);
        }
        else profile.Notices.Add("A verified regional occupational-wage snapshot is not available for this county. County industry pay is shown; no other region's occupational wages have been substituted.");
        profile.Notices.Add("QCEW covers private UI-covered jobs at workplaces, not employed residents. Industry pay is annual payroll per average job, not a full-time salary or benefits package. Subindustries 721 and 722 are included in sector 72, not additional jobs.");
        profile.Notices.Add("ACS 2020-2024 figures are five-year survey estimates for residents age 16+, not current monthly unemployment. Margins are the published 90% sampling margins for each estimate. They exclude nonsampling error.");
        profile.Notices.Add("Five-year growth starts in pandemic year 2020; the 2015 comparison provides a longer baseline. Comparisons can also reflect NAICS revisions. All pay and payroll are nominal dollars for the stated year.");
        cache.Set("local-economy:" + fips, profile, TimeSpan.FromMinutes(periods.Any(period => period is null) ? 15 : 1440));
        return profile;
    }

    internal static List<IndustryProfile> BuildIndustries(List<Dictionary<string, string>> current,
        List<Dictionary<string, string>> fiveYears, List<Dictionary<string, string>> tenYears, List<Dictionary<string, string>> national)
    {
        Dictionary<string, Dictionary<string, string>> Index(List<Dictionary<string, string>> rows) => rows
            .Where(row => row.GetValueOrDefault("own_code") == "5")
            .GroupBy(row => row.GetValueOrDefault("industry_code", "")).ToDictionary(group => group.Key, group => group.First());
        var now = Index(current); var five = Index(fiveYears); var ten = Index(tenYears); var us = Index(national);
        var total = Published(now.GetValueOrDefault("10"), "annual_avg_emplvl");
        var usTotal = Published(us.GetValueOrDefault("10"), "annual_avg_emplvl");
        var result = new List<IndustryProfile>();
        foreach (var (code, name) in new[] { new KeyValuePair<string, string>("10", "All private industries") }.Concat(Sectors))
        {
            var row = now.GetValueOrDefault(code); var jobs = Published(row, "annual_avg_emplvl");
            var usJobs = Published(us.GetValueOrDefault(code), "annual_avg_emplvl");
            result.Add(new IndustryProfile
            {
                Code = code, Name = name, Jobs = jobs,
                Establishments = Numeric(row?.GetValueOrDefault("annual_avg_estabs")), // establishment counts remain public when wages/jobs are suppressed
                AnnualPay = Published(row, "avg_annual_pay"), Payroll = Published(row, "total_annual_wages"),
                Share = Ratio(jobs, total, 100),
                ChangeFiveYear = Change(jobs, Published(five.GetValueOrDefault(code), "annual_avg_emplvl")),
                ChangeTenYear = Change(jobs, Published(ten.GetValueOrDefault(code), "annual_avg_emplvl")),
                LocationQuotient = Ratio(Ratio(jobs, total), Ratio(usJobs, usTotal)),
                Suppressed = row?.GetValueOrDefault("disclosure_code") == "N"
            });
        }
        return result;
    }

    private static double? Numeric(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0 ? number : null;
    private static double? Published(Dictionary<string, string>? row, string key) => row is null || row.GetValueOrDefault("disclosure_code") == "N" ? null : Numeric(row.GetValueOrDefault(key));
    private static double? Ratio(double? value, double? basis, double scale = 1) => value is not null && basis > 0 ? value / basis * scale : null;
    private static double? Change(double? current, double? baseline) => Ratio(current - baseline, baseline, 100);

    internal static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new InvalidDataException("QCEW returned no data.");
        string[] Fields(string line) => line.Trim().Split(',').Select(field => field.Trim('"')).ToArray();
        var header = Fields(lines[0]);
        if (!header.Contains("disclosure_code") || !header.Contains("annual_avg_emplvl")) throw new InvalidDataException("Unexpected QCEW schema.");
        return lines.Skip(1).Select(Fields).Where(fields => fields.Length == header.Length)
            .Select(fields => header.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second))
            .Where(row => row.GetValueOrDefault("own_code") == "5" && row.GetValueOrDefault("size_code") == "0").ToList();
    }

    private async Task<QcewSnapshot?> Qcew(string fips, int year, CancellationToken token)
    {
        var snapshot = await ReadSnapshot<QcewSnapshot>($"qcew-{year}-{fips}.json", token);
        if (snapshot is not null) { snapshot.Year = year; snapshot.Packaged = true; return snapshot; }
        try
        {
            await UpstreamLimit.WaitAsync(token);
            try
            {
                var text = await http.GetStringAsync($"https://data.bls.gov/cew/data/api/{year}/a/area/{fips}.csv", token);
                return new QcewSnapshot { Year = year, RetrievedAtUtc = DateTime.UtcNow, Rows = ParseCsv(text) };
            }
            finally { UpstreamLimit.Release(); }
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            token.ThrowIfCancellationRequested();
            logger.LogWarning("Public QCEW data unavailable for county {Fips}, year {Year}: {ErrorType}", fips, year, error.GetType().Name);
            return null;
        }
    }

    private async Task<T?> ReadSnapshot<T>(string name, CancellationToken token)
    {
        var key = "public-economy-snapshot:" + name;
        if (cache.TryGetValue<T>(key, out var cached)) return cached;
        var path = Path.Combine(environment.ContentRootPath, "Data", "PublicEconomy", name);
        if (!File.Exists(path)) return default;
        await using var stream = File.OpenRead(path);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, Json, token);
        cache.Set(key, result, TimeSpan.FromDays(1));
        return result;
    }

    private sealed class AcsSnapshot { public DateTime RetrievedAtUtc { get; set; } public Dictionary<string, WorkforceProfile> Counties { get; set; } = []; }
    private sealed class QcewSnapshot { public int Year { get; set; } public bool Packaged { get; set; } public DateTime RetrievedAtUtc { get; set; } public List<Dictionary<string, string>> Rows { get; set; } = []; }
    private sealed class WageSnapshot { public List<WageArea> Areas { get; set; } = []; }
    private sealed class LausSnapshot { public Dictionary<string, MonthlyLaborMarket> Counties { get; set; } = []; }
    private sealed class WageArea { public string AreaCode { get; set; } = ""; public string AreaName { get; set; } = ""; public List<OccupationWage> Occupations { get; set; } = []; }
}

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SaveNEIN.Server.Services.Providers;

public sealed class IndianaTourismProviderOptions
{
    public const string ConfigurationSection = "IndianaTourism";

    public string ReportUrl { get; set; } =
        "https://visitindiana.in.gov/articles/post/iddc-releases-2023-contribution-of-travel-tourism-to-the-indiana-economy/";
    public string ResearchIndexUrl { get; set; } =
        "https://visitindiana.in.gov/about-iddc/tourism-research/";
}

public sealed partial class IndianaDestinationDevelopmentPersonTripsProvider(
    HttpClient http,
    IOptions<IndianaTourismProviderOptions> options) : ITourismObservationProvider
{
    private const int SupportedYear = 2023;

    public string ProviderKey => "iddc-indiana-statewide-person-trips";

    public async Task<ProviderDataset<TourismMarketObservationImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireRequest(request);
        var configured = options.Value;
        var reportUri = new Uri(configured.ReportUrl);
        var retrievedAt = DateTime.UtcNow;
        using var response = await http.GetAsync(reportUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var html = System.Text.Encoding.UTF8.GetString(bytes);
        var text = NormalizeHtml(html);
        var match = PersonTripStatement().Match(text);
        if (!match.Success ||
            !decimal.TryParse(match.Groups["current"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var millionPersonTrips) ||
            millionPersonTrips <= 0)
        {
            throw new InvalidDataException(
                "The IDDC report page did not contain the expected current-year million person-trips statement.");
        }

        var normalizedTrips = millionPersonTrips * 1_000_000m;
        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var periodStart = new DateOnly(SupportedYear, 1, 1);
        var periodEnd = new DateOnly(SupportedYear, 12, 31);
        var rows = new[]
        {
            new TourismMarketObservationImportRow(
                $"USA-IN-IDDC-{SupportedYear}-statewide-person-trips",
                "indiana-statewide-tourism",
                "state",
                "US-IN",
                periodStart,
                periodEnd,
                "million-person-trips",
                millionPersonTrips,
                normalizedTrips,
                "Published million person-trips multiplied by 1,000,000; no conversion to unique visitors or visitor-days.",
                "Statewide IDDC/Rockport travel volume. Person-trips are not unique people, and this observation is not a local-market capture estimate." )
        };
        var warnings = new[]
        {
            "This is statewide person-trip volume, not site-addressable tourism. A model run must apply an evidenced " +
            "local relevance/capture rate and must not interpret the full statewide total as the candidate market.",
            "Person-trips may include Indiana residents and repeat trips. Resident-origin overlap, casino eligibility, " +
            "gaming participation, and traffic overlap must be explicitly deduplicated in the model run."
        };

        return new ProviderDataset<TourismMarketObservationImportRow>(
            new RegisterDataSourceRequest(
                $"IDDC {SupportedYear} Indiana statewide tourism person-trips",
                "Indiana Destination Development Corporation",
                reportUri.ToString(),
                "state-tourism-agency-html",
                "Indiana statewide",
                SupportedYear.ToString(CultureInfo.InvariantCulture),
                retrievedAt,
                contentHash,
                true,
                "IDDC website terms apply; underlying study copyright remains with Rockport Analytics.",
                $"Research index: {configured.ResearchIndexUrl}. The agency page reports the study's statewide person-trip metric."),
            DatasetSnapshotKinds.Tourism,
            SupportedYear.ToString(CultureInfo.InvariantCulture),
            periodStart,
            periodEnd,
            contentHash,
            "iddc-rockport-statewide-person-trips-2023-v1",
            rows,
            warnings);
    }

    private static void RequireRequest(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The IDDC tourism adapter requires GeographicCoverage 'US-IN'.");
        }
        if (request.PeriodStart != new DateOnly(SupportedYear, 1, 1) ||
            request.PeriodEnd != new DateOnly(SupportedYear, 12, 31))
        {
            throw new NotSupportedException(
                $"This versioned IDDC adapter supports the complete {SupportedYear} tourism year only.");
        }
    }

    private static string NormalizeHtml(string html) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.Singleline)),
        "\\s+",
        " ").Trim();

    [GeneratedRegex(
        @"Total Indiana visitor volume grew.{0,500}?from\s+(?<prior>[0-9]+(?:\.[0-9]+)?)\s+to\s+(?<current>[0-9]+(?:\.[0-9]+)?)\s+million\s+person-trips",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PersonTripStatement();
}

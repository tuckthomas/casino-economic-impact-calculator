using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace SaveNEIN.Server.Services.Providers;

public sealed class IndianaDepartmentOfTransportationProviderOptions
{
    public const string ConfigurationSection = "IndianaDepartmentOfTransportation";

    public string AadtArchiveUrl { get; set; } =
        "https://secure.in.gov/indot/files/AADT-2025-2024-Count-Data-20260324.zip";
    public string PublicationUrl { get; set; } = "https://secure.in.gov/indot/resources/traffic-data/";
}

public sealed class IndianaDepartmentOfTransportationAadtProvider(
    HttpClient http,
    IOptions<IndianaDepartmentOfTransportationProviderOptions> options) : ITrafficObservationProvider
{
    public string ProviderKey => "indot-aadt-count-zones";

    public async Task<ProviderDataset<TrafficCorridorObservationImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireIndianaVintage(request);
        var configured = options.Value;
        var archiveUri = new Uri(configured.AadtArchiveUrl);
        var retrievedAt = DateTime.UtcNow;
        using var response = await http.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var filter = IndotAadtFilter.FromRequest(request);
        var parsed = IndotAadtArchiveReader.Read(bytes, request, filter);
        if (parsed.Rows.Count == 0)
        {
            throw new InvalidDataException(
                $"The INDOT AADT archive produced no {request.PeriodStart.Year} rows for the requested corridor filter.");
        }
        var datasetChecksum = DatasetChecksum(contentHash, request.Options);

        return new ProviderDataset<TrafficCorridorObservationImportRow>(
            new RegisterDataSourceRequest(
                $"INDOT {request.PeriodStart.Year} annual average daily traffic count zones",
                "Indiana Department of Transportation",
                archiveUri.ToString(),
                "state-dot-shapefile",
                "Indiana roadway traffic sections",
                request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture),
                retrievedAt,
                contentHash,
                true,
                "Indiana public-record terms apply.",
                $"Publication index: {configured.PublicationUrl}. Coordinates are transformed from the source NAD83 / UTM zone 16N geometry."),
            DatasetSnapshotKinds.Traffic,
            request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            datasetChecksum,
            "indot-aadt-shapefile-nad83-utm16-v1",
            parsed.Rows,
            parsed.Warnings);
    }

    private static void RequireIndianaVintage(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-IN", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The INDOT AADT adapter requires GeographicCoverage 'US-IN'.");
        }
        if (request.PeriodStart != new DateOnly(request.PeriodStart.Year, 1, 1) ||
            request.PeriodEnd != new DateOnly(request.PeriodStart.Year, 12, 31))
        {
            throw new ArgumentException(
                "An INDOT AADT request must span exactly one complete calendar year.",
                nameof(request));
        }
    }

    private static string DatasetChecksum(
        string contentHash,
        IReadOnlyDictionary<string, string>? requestOptions)
    {
        var canonicalOptions = requestOptions is null
            ? string.Empty
            : string.Join(
                '\n',
                requestOptions.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key.Trim()}={pair.Value.Trim()}"));
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{contentHash}\n{canonicalOptions}")))
            .ToLowerInvariant();
    }
}

internal sealed record IndotAadtFilter(
    IReadOnlySet<string> RouteIds,
    IReadOnlySet<string> SiteNumbers,
    double? MinimumLatitude,
    double? MaximumLatitude,
    double? MinimumLongitude,
    double? MaximumLongitude)
{
    public static IndotAadtFilter FromRequest(ProviderFetchRequest request)
    {
        var options = request.Options ?? new Dictionary<string, string>();
        return new IndotAadtFilter(
            Values(options, "route-ids"),
            Values(options, "site-numbers"),
            Number(options, "minimum-latitude", -90, 90),
            Number(options, "maximum-latitude", -90, 90),
            Number(options, "minimum-longitude", -180, 180),
            Number(options, "maximum-longitude", -180, 180));
    }

    public bool Includes(string routeId, string siteNumber, double latitude, double longitude) =>
        IncludesIdentifiers(routeId, siteNumber) &&
        (!MinimumLatitude.HasValue || latitude >= MinimumLatitude.Value) &&
        (!MaximumLatitude.HasValue || latitude <= MaximumLatitude.Value) &&
        (!MinimumLongitude.HasValue || longitude >= MinimumLongitude.Value) &&
        (!MaximumLongitude.HasValue || longitude <= MaximumLongitude.Value);

    public bool IncludesIdentifiers(string routeId, string siteNumber) =>
        (RouteIds.Count == 0 || RouteIds.Contains(routeId)) &&
        (SiteNumbers.Count == 0 || SiteNumbers.Contains(siteNumber));

    private static IReadOnlySet<string> Values(
        IReadOnlyDictionary<string, string> options,
        string key) =>
        options.TryGetValue(key, out var raw)
            ? raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static double? Number(
        IReadOnlyDictionary<string, string> options,
        string key,
        double minimum,
        double maximum)
    {
        if (!options.TryGetValue(key, out var raw))
        {
            return null;
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentException(
                $"INDOT request option '{key}' must be a number from {minimum} through {maximum}.");
        }
        return value;
    }
}

internal static class IndotAadtArchiveReader
{
    public static IndotAadtArchiveResult Read(
        byte[] archiveBytes,
        ProviderFetchRequest request,
        IndotAadtFilter filter)
    {
        var extractionDirectory = Path.Combine(
            Path.GetTempPath(),
            "savenein-indot-aadt",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(extractionDirectory);
        try
        {
            var shapefilePath = ExtractShapefile(archiveBytes, extractionDirectory);
            return ReadShapefile(shapefilePath, request, filter);
        }
        finally
        {
            if (Directory.Exists(extractionDirectory))
            {
                Directory.Delete(extractionDirectory, recursive: true);
            }
        }
    }

    private static string ExtractShapefile(byte[] archiveBytes, string extractionDirectory)
    {
        using var stream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var shapeEntry = archive.Entries.SingleOrDefault(entry =>
            string.Equals(Path.GetExtension(entry.Name), ".shp", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("The INDOT archive contains no .shp file.");
        var sourceBaseName = Path.GetFileNameWithoutExtension(shapeEntry.Name);
        var targetBasePath = Path.Combine(extractionDirectory, "indot-aadt");
        foreach (var extension in new[] { ".shp", ".shx", ".dbf", ".cpg", ".prj" })
        {
            var entry = archive.Entries.SingleOrDefault(candidate =>
                string.Equals(
                    Path.GetFileNameWithoutExtension(candidate.Name),
                    sourceBaseName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(candidate.Name), extension, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                if (extension is ".shp" or ".shx" or ".dbf")
                {
                    throw new InvalidDataException($"The INDOT archive is missing its required '{extension}' component.");
                }
                continue;
            }
            using var source = entry.Open();
            using var target = File.Create(targetBasePath + extension);
            source.CopyTo(target);
        }
        return targetBasePath + ".shp";
    }

    private static IndotAadtArchiveResult ReadShapefile(
        string shapefilePath,
        ProviderFetchRequest request,
        IndotAadtFilter filter)
    {
        var rows = new List<TrafficCorridorObservationImportRow>();
        var missingGeometryCount = 0;
        var invalidAadtCount = 0;
        using var reader = new ShapefileDataReader(
            shapefilePath,
            new GeometryFactory(new PrecisionModel(), 26916),
            Encoding.UTF8);
        var ordinals = new IndotFieldOrdinals(reader);
        while (reader.Read())
        {
            var year = Field(reader, ordinals.HpmsYear);
            if (!string.Equals(year, request.PeriodStart.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                continue;
            }
            var routeId = Field(reader, ordinals.RouteId);
            var siteNumber = Field(reader, ordinals.SiteNumber);
            if (!filter.IncludesIdentifiers(routeId, siteNumber))
            {
                continue;
            }
            if (reader.Geometry is null || reader.Geometry.IsEmpty)
            {
                missingGeometryCount++;
                continue;
            }
            var projected = reader.Geometry.Centroid.Coordinate;
            var (latitude, longitude) = Nad83UtmZone16N.ToGeographic(projected.X, projected.Y);
            if (!filter.Includes(routeId, siteNumber, latitude, longitude))
            {
                continue;
            }
            var trafficSectionId = Field(reader, ordinals.TrafficSectionId);
            var eventId = Field(reader, ordinals.EventId).Trim('{', '}');
            var stableComponent = string.IsNullOrWhiteSpace(eventId)
                ? $"{siteNumber}-{trafficSectionId}"
                : eventId;
            if (string.IsNullOrWhiteSpace(stableComponent))
            {
                throw new InvalidDataException("An INDOT AADT record has neither an event ID nor a site/section identifier.");
            }
            if (!TryPositiveNumber(reader, ordinals.Aadt, out var aadt))
            {
                invalidAadtCount++;
                continue;
            }
            var sourceCountDate = Field(reader, ordinals.FromDate);
            var comment = Field(reader, ordinals.Comment);
            var notes = string.Join(
                "; ",
                new[]
                {
                    string.IsNullOrWhiteSpace(trafficSectionId) ? null : $"Traffic section {trafficSectionId}",
                    string.IsNullOrWhiteSpace(siteNumber) ? null : $"site {siteNumber}",
                    string.IsNullOrWhiteSpace(sourceCountDate) ? null : $"source count date {sourceCountDate}",
                    string.IsNullOrWhiteSpace(comment) ? null : comment
                }.Where(value => value is not null));

            rows.Add(new TrafficCorridorObservationImportRow(
                $"USA-IN-INDOT-AADT-{stableComponent.ToLowerInvariant()}",
                routeId,
                "US-IN",
                latitude,
                longitude,
                request.PeriodStart,
                request.PeriodEnd,
                aadt,
                DateTime.IsLeapYear(request.PeriodStart.Year) ? 366 : 365,
                "INDOT published annual average daily traffic for a roadway count zone",
                "Two-way total unless the source traffic-section definition indicates otherwise.",
                notes.Length == 0 ? null : notes));
        }
        var warnings = new List<string>();
        if (missingGeometryCount > 0)
        {
            warnings.Add($"Skipped {missingGeometryCount} INDOT AADT records with missing roadway geometry.");
        }
        if (invalidAadtCount > 0)
        {
            warnings.Add($"Skipped {invalidAadtCount} INDOT AADT records with missing or nonpositive AADT.");
        }
        return new IndotAadtArchiveResult(rows, warnings);
    }

    private static string Field(IDataRecord reader, int ordinal)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }
        return reader.GetValue(ordinal) switch
        {
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            var value => value.ToString() ?? string.Empty
        };
    }

    private static bool TryPositiveNumber(IDataRecord reader, int ordinal, out double value)
    {
        var raw = Field(reader, ordinal);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value) && value > 0;
    }

    private sealed class IndotFieldOrdinals
    {
        public IndotFieldOrdinals(IDataRecord reader)
        {
            RouteId = Required(reader, "ROUTE_ID");
            TrafficSectionId = Required(reader, "TRAFFIC_SE");
            SiteNumber = Required(reader, "SITE_NO");
            Aadt = Required(reader, "AADT");
            HpmsYear = Required(reader, "HPMS_YEAR");
            FromDate = Optional(reader, "FROM_DATE");
            EventId = Required(reader, "EVENT_ID");
            Comment = Optional(reader, "COMMENT_");
        }

        public int RouteId { get; }
        public int TrafficSectionId { get; }
        public int SiteNumber { get; }
        public int Aadt { get; }
        public int HpmsYear { get; }
        public int FromDate { get; }
        public int EventId { get; }
        public int Comment { get; }

        private static int Required(IDataRecord reader, string name)
        {
            var ordinal = Optional(reader, name);
            return ordinal >= 0
                ? ordinal
                : throw new InvalidDataException($"The INDOT shapefile is missing required field '{name}'.");
        }

        private static int Optional(IDataRecord reader, string name)
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                if (string.Equals(reader.GetName(index), name, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return -1;
        }
    }
}

internal sealed record IndotAadtArchiveResult(
    IReadOnlyList<TrafficCorridorObservationImportRow> Rows,
    IReadOnlyList<string> Warnings);

internal static class Nad83UtmZone16N
{
    private const double SemiMajorAxis = 6_378_137.0;
    private const double InverseFlattening = 298.257222101;
    private const double ScaleFactor = 0.9996;
    private const double CentralMeridianDegrees = -87.0;

    public static (double Latitude, double Longitude) ToGeographic(double easting, double northing)
    {
        if (!double.IsFinite(easting) || !double.IsFinite(northing) ||
            easting is < 100_000 or > 900_000 || northing is < 0 or > 10_000_000)
        {
            throw new InvalidDataException(
                $"INDOT NAD83 / UTM zone 16N coordinate ({easting}, {northing}) is outside valid UTM bounds.");
        }

        var flattening = 1.0 / InverseFlattening;
        var eccentricitySquared = flattening * (2.0 - flattening);
        var eccentricityPrimeSquared = eccentricitySquared / (1.0 - eccentricitySquared);
        var root = Math.Sqrt(1.0 - eccentricitySquared);
        var e1 = (1.0 - root) / (1.0 + root);
        var meridionalArc = northing / ScaleFactor;
        var mu = meridionalArc /
                 (SemiMajorAxis *
                  (1.0 - eccentricitySquared / 4.0 -
                   3.0 * Math.Pow(eccentricitySquared, 2) / 64.0 -
                   5.0 * Math.Pow(eccentricitySquared, 3) / 256.0));
        var footprintLatitude =
            mu +
            (3.0 * e1 / 2.0 - 27.0 * Math.Pow(e1, 3) / 32.0) * Math.Sin(2.0 * mu) +
            (21.0 * Math.Pow(e1, 2) / 16.0 - 55.0 * Math.Pow(e1, 4) / 32.0) * Math.Sin(4.0 * mu) +
            (151.0 * Math.Pow(e1, 3) / 96.0) * Math.Sin(6.0 * mu) +
            (1097.0 * Math.Pow(e1, 4) / 512.0) * Math.Sin(8.0 * mu);

        var sin = Math.Sin(footprintLatitude);
        var cos = Math.Cos(footprintLatitude);
        var tan = Math.Tan(footprintLatitude);
        var n1 = SemiMajorAxis / Math.Sqrt(1.0 - eccentricitySquared * sin * sin);
        var r1 = SemiMajorAxis * (1.0 - eccentricitySquared) /
                 Math.Pow(1.0 - eccentricitySquared * sin * sin, 1.5);
        var t1 = tan * tan;
        var c1 = eccentricityPrimeSquared * cos * cos;
        var d = (easting - 500_000.0) / (n1 * ScaleFactor);

        var latitude = footprintLatitude - (n1 * tan / r1) *
            (d * d / 2.0 -
             (5.0 + 3.0 * t1 + 10.0 * c1 - 4.0 * c1 * c1 - 9.0 * eccentricityPrimeSquared) * Math.Pow(d, 4) / 24.0 +
             (61.0 + 90.0 * t1 + 298.0 * c1 + 45.0 * t1 * t1 -
              252.0 * eccentricityPrimeSquared - 3.0 * c1 * c1) * Math.Pow(d, 6) / 720.0);
        var longitudeOffset =
            (d -
             (1.0 + 2.0 * t1 + c1) * Math.Pow(d, 3) / 6.0 +
             (5.0 - 2.0 * c1 + 28.0 * t1 - 3.0 * c1 * c1 +
              8.0 * eccentricityPrimeSquared + 24.0 * t1 * t1) * Math.Pow(d, 5) / 120.0) / cos;

        return (
            latitude * 180.0 / Math.PI,
            CentralMeridianDegrees + longitudeOffset * 180.0 / Math.PI);
    }
}

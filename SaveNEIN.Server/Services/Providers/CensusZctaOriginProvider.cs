using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace SaveNEIN.Server.Services.Providers;

public sealed class CensusZctaOriginProviderOptions
{
    public const string ConfigurationSection = "CensusZctaOrigins";

    public string ArchiveUrl { get; set; } =
        "https://www2.census.gov/geo/tiger/GENZ2020/shp/cb_2020_us_zcta520_500k.zip";
    public string PublicationUrl { get; set; } =
        "https://www.census.gov/geographies/mapping-files/time-series/geo/cartographic-boundary.2020.html";
    public string CountyRelationshipUrl { get; set; } =
        "https://www2.census.gov/geo/docs/maps-data/data/rel2020/zcta520/tab20_zcta520_county20_natl.txt";
    public string RelationshipPublicationUrl { get; set; } =
        "https://www.census.gov/geographies/reference-files/time-series/geo/relationship-files.2020.html";
}

/// <summary>
/// Provides explicit Census ZCTA origins. It does not label or expose these statistical
/// areas as USPS ZIP Codes.
/// </summary>
public sealed class CensusZctaOriginProvider(
    HttpClient http,
    IOptions<CensusZctaOriginProviderOptions> options) : IOriginGeographyProvider
{
    private const int GeographyYear = 2020;
    private const string TransformVersion = "census-2020-zcta520-cartographic-500k-county-dominant-v2";

    public string ProviderKey => "census-zcta520-cartographic-origins";

    public async Task<ProviderDataset<OriginZoneImportRow>> FetchAsync(
        ProviderFetchRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireRequest(request);
        var marketUniverse = ZctaMarketUniverse.Require(request.Options);
        var configured = options.Value;
        var archiveUri = new Uri(configured.ArchiveUrl);
        var retrievedAt = DateTime.UtcNow;
        using var response = await http.GetAsync(archiveUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var relationshipUri = new Uri(configured.CountyRelationshipUrl);
        using var relationshipResponse = await http.GetAsync(
            relationshipUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        relationshipResponse.EnsureSuccessStatusCode();
        var relationshipBytes = await relationshipResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        var archiveHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var relationshipHash = Convert.ToHexString(SHA256.HashData(relationshipBytes)).ToLowerInvariant();
        var sourceHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{archiveHash}\n{relationshipHash}")))
            .ToLowerInvariant();
        var parsed = CensusZctaArchiveReader.Read(bytes, marketUniverse);
        var selectedCodes = parsed.Rows.Select(row => row.GeographyCode).ToHashSet(StringComparer.Ordinal);
        var missingCodes = marketUniverse.ExplicitCodes?.Except(selectedCodes, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray() ?? [];
        if (missingCodes.Length > 0)
        {
            throw new KeyNotFoundException(
                $"The Census 2020 ZCTA cartographic boundary file is missing requested code(s): {string.Join(", ", missingCodes)}.");
        }
        var dominantCounties = CensusZctaCountyRelationshipReader.Read(relationshipBytes, selectedCodes);
        var rows = parsed.Rows.Select(row =>
        {
            var relationship = dominantCounties[row.GeographyCode];
            return row with
            {
                StateOrTerritoryCode = CensusStateCodes.FromFips(relationship.CountyGeoid[..2]),
                CountyEquivalentCode = relationship.CountyGeoid
            };
        }).ToArray();

        var checksum = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{sourceHash}\n{TransformVersion}\n{marketUniverse.CanonicalDescriptor}")))
            .ToLowerInvariant();
        var warnings = parsed.Warnings
            .Append(
                "The 1:500,000 cartographic boundaries are generalized for regional modeling and reporting; " +
                "they are not parcel-level boundaries and they do not make Census ZCTAs equivalent to USPS ZIP Codes.")
            .Append(
                "State and county attributes use the county with the largest 2020 Census land-area overlap for each ZCTA; " +
                "cross-county and cross-state ZCTAs remain single computational origins and are not proportionally allocated by this field.")
            .Append(marketUniverse.SelectionWarning)
            .ToArray();

        return new ProviderDataset<OriginZoneImportRow>(
            new RegisterDataSourceRequest(
                "2020 Census ZCTA5 cartographic boundaries (1:500,000)",
                "United States Census Bureau",
                archiveUri.ToString(),
                "federal-cartographic-boundary-shapefile",
                marketUniverse.SourceScope,
                GeographyYear.ToString(CultureInfo.InvariantCulture),
                retrievedAt,
                sourceHash,
                true,
                "Census public-use terms apply.",
                $"Boundary publication index: {configured.PublicationUrl}. County relationship file: {relationshipUri}. " +
                $"Relationship publication index: {configured.RelationshipPublicationUrl}. " +
                "ZCTAs are Census statistical geographies, not USPS ZIP Codes."),
            DatasetSnapshotKinds.OriginGeography,
            GeographyYear.ToString(CultureInfo.InvariantCulture),
            request.PeriodStart,
            request.PeriodEnd,
            checksum,
            TransformVersion,
            rows,
            warnings);
    }

    private static void RequireRequest(ProviderFetchRequest request)
    {
        if (!string.Equals(request.GeographicCoverage, "US-ZCTA", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Census ZCTA origins require GeographicCoverage 'US-ZCTA'.");
        }
        if (request.PeriodStart != new DateOnly(GeographyYear, 1, 1) ||
            request.PeriodEnd != new DateOnly(GeographyYear, 12, 31))
        {
            throw new NotSupportedException(
                $"This provider exposes the complete {GeographyYear} Census ZCTA geography vintage only.");
        }
    }
}

internal sealed record CensusZctaDominantCounty(string CountyGeoid, long LandAreaPart, long WaterAreaPart);

internal static class CensusZctaCountyRelationshipReader
{
    public static IReadOnlyDictionary<string, CensusZctaDominantCounty> Read(
        byte[] bytes,
        IReadOnlySet<string> requestedCodes)
    {
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        using var reader = new StringReader(text);
        var header = reader.ReadLine()?.Split('|')
            ?? throw new InvalidDataException("The Census ZCTA-to-county relationship file is empty.");
        var codeIndex = RequiredColumn(header, "GEOID_ZCTA5_20");
        var countyIndex = RequiredColumn(header, "GEOID_COUNTY_20");
        var landIndex = RequiredColumn(header, "AREALAND_PART");
        var waterIndex = RequiredColumn(header, "AREAWATER_PART");
        var candidates = new Dictionary<string, List<CensusZctaDominantCounty>>(StringComparer.Ordinal);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.TrimEnd('\r').Split('|');
            if (fields.Length != header.Length)
            {
                throw new InvalidDataException("A Census ZCTA-to-county relationship row has an unexpected column count.");
            }
            var code = fields[codeIndex].Trim();
            if (!requestedCodes.Contains(code))
            {
                continue;
            }
            var countyGeoid = fields[countyIndex].Trim();
            if (countyGeoid.Length != 5 || !countyGeoid.All(char.IsAsciiDigit) ||
                !long.TryParse(fields[landIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var land) ||
                !long.TryParse(fields[waterIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var water))
            {
                throw new InvalidDataException($"Census ZCTA '{code}' has an invalid county relationship row.");
            }
            if (!candidates.TryGetValue(code, out var relationships))
            {
                relationships = [];
                candidates.Add(code, relationships);
            }
            relationships.Add(new CensusZctaDominantCounty(countyGeoid, land, water));
        }

        var missing = requestedCodes.Except(candidates.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            throw new KeyNotFoundException(
                $"The Census 2020 ZCTA-to-county relationship file is missing requested code(s): {string.Join(", ", missing)}.");
        }
        return candidates.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .OrderByDescending(item => item.LandAreaPart)
                .ThenByDescending(item => item.WaterAreaPart)
                .ThenBy(item => item.CountyGeoid, StringComparer.Ordinal)
                .First(),
            StringComparer.Ordinal);
    }

    private static int RequiredColumn(IReadOnlyList<string> header, string name)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (string.Equals(header[index], name, StringComparison.Ordinal))
            {
                return index;
            }
        }
        throw new InvalidDataException($"The Census ZCTA-to-county relationship file is missing column '{name}'.");
    }
}

internal static class CensusStateCodes
{
    private static readonly IReadOnlyDictionary<string, string> ByFips = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["01"] = "AL", ["02"] = "AK", ["04"] = "AZ", ["05"] = "AR", ["06"] = "CA",
        ["08"] = "CO", ["09"] = "CT", ["10"] = "DE", ["11"] = "DC", ["12"] = "FL",
        ["13"] = "GA", ["15"] = "HI", ["16"] = "ID", ["17"] = "IL", ["18"] = "IN",
        ["19"] = "IA", ["20"] = "KS", ["21"] = "KY", ["22"] = "LA", ["23"] = "ME",
        ["24"] = "MD", ["25"] = "MA", ["26"] = "MI", ["27"] = "MN", ["28"] = "MS",
        ["29"] = "MO", ["30"] = "MT", ["31"] = "NE", ["32"] = "NV", ["33"] = "NH",
        ["34"] = "NJ", ["35"] = "NM", ["36"] = "NY", ["37"] = "NC", ["38"] = "ND",
        ["39"] = "OH", ["40"] = "OK", ["41"] = "OR", ["42"] = "PA", ["44"] = "RI",
        ["45"] = "SC", ["46"] = "SD", ["47"] = "TN", ["48"] = "TX", ["49"] = "UT",
        ["50"] = "VT", ["51"] = "VA", ["53"] = "WA", ["54"] = "WV", ["55"] = "WI",
        ["56"] = "WY", ["60"] = "AS", ["66"] = "GU", ["69"] = "MP", ["72"] = "PR",
        ["74"] = "UM", ["78"] = "VI"
    };

    public static string FromFips(string fips) => ByFips.TryGetValue(fips, out var code)
        ? code
        : throw new InvalidDataException($"Unsupported Census state FIPS code '{fips}'.");
}

internal sealed record ZctaMarketUniverse(
    IReadOnlySet<string>? ExplicitCodes,
    double? CenterLatitude,
    double? CenterLongitude,
    double? RadiusMiles)
{
    public string CanonicalDescriptor => ExplicitCodes is not null
        ? $"zcta-codes={string.Join(',', ExplicitCodes.Order(StringComparer.Ordinal))}"
        : FormattableString.Invariant($"center={CenterLatitude:F6},{CenterLongitude:F6};radius-miles={RadiusMiles:F3}");

    public string SourceScope => ExplicitCodes is not null
        ? "Explicit requested United States ZCTAs"
        : FormattableString.Invariant(
            $"United States ZCTAs within a {RadiusMiles:F1}-mile representative-point radius of {CenterLatitude:F6}, {CenterLongitude:F6}");

    public string SelectionWarning => ExplicitCodes is not null
        ? "The origin snapshot contains the caller's explicit ZCTA market universe."
        : "The origin snapshot uses a broad representative-point Haversine radius only to prefilter the candidate study region; " +
          "the model must use persisted Valhalla travel times, not this radius, for travel friction and final route reachability.";

    public bool Includes(string code, Geometry geometry)
    {
        if (ExplicitCodes is not null)
        {
            return ExplicitCodes.Contains(code);
        }
        var point = geometry.InteriorPoint;
        return HaversineMiles(CenterLatitude!.Value, CenterLongitude!.Value, point.Y, point.X) <= RadiusMiles!.Value;
    }

    public static ZctaMarketUniverse Require(IReadOnlyDictionary<string, string>? options)
    {
        var explicitCodes = ZctaCodeFilter.Optional(options);
        var radialKeys = new[] { "center-latitude", "center-longitude", "radius-miles" };
        var suppliedRadialCount = options is null ? 0 : radialKeys.Count(options.ContainsKey);
        if (explicitCodes is not null)
        {
            if (suppliedRadialCount > 0)
            {
                throw new ArgumentException("Specify either 'zcta-codes' or center/radius options, not both.");
            }
            return new ZctaMarketUniverse(explicitCodes, null, null, null);
        }
        if (suppliedRadialCount != radialKeys.Length ||
            !double.TryParse(options!["center-latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
            !double.TryParse(options["center-longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) ||
            !double.TryParse(options["radius-miles"], NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusMiles) ||
            !double.IsFinite(latitude) || latitude is < -90 or > 90 ||
            !double.IsFinite(longitude) || longitude is < -180 or > 180 ||
            !double.IsFinite(radiusMiles) || radiusMiles is <= 0 or > 500)
        {
            throw new ArgumentException(
                "Provide a nonempty 'zcta-codes' option or valid 'center-latitude', 'center-longitude', and 'radius-miles' options; radius must be greater than zero and no more than 500 miles.");
        }
        return new ZctaMarketUniverse(null, latitude, longitude, radiusMiles);
    }

    private static double HaversineMiles(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMiles = 3958.7613;
        static double Radians(double degrees) => degrees * Math.PI / 180d;
        var latitudeDelta = Radians(latitude2 - latitude1);
        var longitudeDelta = Radians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2) +
                Math.Cos(Radians(latitude1)) * Math.Cos(Radians(latitude2)) *
                Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        a = Math.Clamp(a, 0d, 1d);
        return earthRadiusMiles * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
    }
}

internal static class ZctaCodeFilter
{
    public static IReadOnlySet<string> Require(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || !options.TryGetValue("zcta-codes", out var raw))
        {
            throw new ArgumentException(
                "A nonempty 'zcta-codes' request option is required so a model snapshot contains an explicit market universe.");
        }
        var codes = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (codes.Count == 0 || codes.Any(code => code.Length != 5 || !code.All(char.IsAsciiDigit)))
        {
            throw new ArgumentException("Every 'zcta-codes' value must be a five-digit Census ZCTA code.");
        }
        return codes;
    }

    public static IReadOnlySet<string>? Optional(IReadOnlyDictionary<string, string>? options) =>
        options is not null && options.ContainsKey("zcta-codes") ? Require(options) : null;
}

internal sealed record CensusZctaArchiveResult(
    IReadOnlyCollection<OriginZoneImportRow> Rows,
    IReadOnlyCollection<string> Warnings);

internal static class CensusZctaArchiveReader
{
    public static CensusZctaArchiveResult Read(byte[] archiveBytes, ZctaMarketUniverse marketUniverse)
    {
        var extractionDirectory = Path.Combine(
            Path.GetTempPath(),
            "savenein-census-zcta",
            Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(extractionDirectory);
        try
        {
            var shapefilePath = ExtractShapefile(archiveBytes, extractionDirectory);
            return ReadShapefile(shapefilePath, marketUniverse);
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
            ?? throw new InvalidDataException("The Census ZCTA archive contains no .shp file.");
        var sourceBaseName = Path.GetFileNameWithoutExtension(shapeEntry.Name);
        var targetBasePath = Path.Combine(extractionDirectory, "census-zcta");
        foreach (var extension in new[] { ".shp", ".shx", ".dbf", ".cpg", ".prj" })
        {
            var entry = archive.Entries.SingleOrDefault(candidate =>
                string.Equals(Path.GetFileNameWithoutExtension(candidate.Name), sourceBaseName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(candidate.Name), extension, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                if (extension is ".shp" or ".shx" or ".dbf")
                {
                    throw new InvalidDataException($"The Census ZCTA archive is missing its required '{extension}' component.");
                }
                continue;
            }
            using var source = entry.Open();
            using var target = File.Create(targetBasePath + extension);
            source.CopyTo(target);
        }
        return targetBasePath + ".shp";
    }

    private static CensusZctaArchiveResult ReadShapefile(
        string shapefilePath,
        ZctaMarketUniverse marketUniverse)
    {
        var rows = new List<OriginZoneImportRow>(marketUniverse.ExplicitCodes?.Count ?? 512);
        using var reader = new ShapefileDataReader(
            shapefilePath,
            new GeometryFactory(new PrecisionModel(), 4326),
            Encoding.UTF8);
        var codeOrdinal = RequiredOrdinal(reader, "ZCTA5CE20");
        var wktWriter = new WKTWriter();
        while (reader.Read())
        {
            var code = Field(reader, codeOrdinal);
            var geometry = reader.Geometry;
            if (geometry is not Polygon and not MultiPolygon || geometry.IsEmpty || !geometry.IsValid)
            {
                throw new InvalidDataException($"Census ZCTA '{code}' has invalid or empty polygon geometry.");
            }
            if (!marketUniverse.Includes(code, geometry))
            {
                continue;
            }
            geometry.SRID = 4326;
            var representativePoint = geometry.InteriorPoint;
            rows.Add(new OriginZoneImportRow(
                $"USA-ZCTA-{code}",
                "zcta",
                code,
                "USA",
                null,
                null,
                null,
                null,
                representativePoint.Y,
                representativePoint.X,
                wktWriter.Write(geometry)));
        }
        if (rows.Count == 0)
        {
            throw new KeyNotFoundException("The selected ZCTA market universe contains no Census ZCTA polygons.");
        }
        var warnings = new[]
        {
            "Representative coordinates are deterministic geometry interior points computed from the generalized " +
            "cartographic polygons; this source file does not publish TIGER internal-point attributes."
        };
        return new CensusZctaArchiveResult(rows, warnings);
    }

    private static int RequiredOrdinal(IDataRecord reader, string name)
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            if (string.Equals(reader.GetName(index), name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }
        throw new InvalidDataException($"The Census ZCTA shapefile is missing required field '{name}'.");
    }

    private static string Field(IDataRecord reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? string.Empty
            : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
}

// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SaveNEIN Advanced Economic Modeling Subsystem
// Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors
// Governed by PolyForm Noncommercial License 1.0.0 (LICENSE-MODEL.md)

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;

namespace SaveNEIN.Server.Services.Reports;

public interface IHtmlReportRenderer
{
    string Render(CasinoImpactReportModel model, ReportPresentationOptions options);
}

public interface IPdfReportRenderer
{
    byte[] Render(CasinoImpactReportModel model, ReportPresentationOptions options);
}

public interface ICsvReportRenderer
{
    string Render(CasinoImpactReportModel model);
}

public interface IReportArtifactService
{
    Task<ModelRunReportArtifact> GetOrCreateAsync(
        Guid modelRunId,
        ReportPresentationOptions options,
        CancellationToken cancellationToken = default);
}

internal sealed record ReportWarningDigest(
    int CalibrationNoticeCount,
    IReadOnlyList<string> DecisionWarnings)
{
    public string Summary =>
        $"This provisional run contains {CalibrationNoticeCount:N0} parameter calibration notice(s) and {DecisionWarnings.Count:N0} other disclosed warning(s). " +
        "Decision-use warnings are shown below; complete parameter status remains in the parameter appendix and machine-readable exports.";
}

internal static class ReportDisclosure
{
    private const string CalibrationNoticeMarker = "does not have a completed calibration designation.";

    public static ReportWarningDigest DigestWarnings(IReadOnlyCollection<string> warnings) => new(
        warnings.Count(warning => warning.Contains(CalibrationNoticeMarker, StringComparison.OrdinalIgnoreCase)),
        warnings.Where(warning => !warning.Contains(CalibrationNoticeMarker, StringComparison.OrdinalIgnoreCase)).ToArray());

    public static string ParameterSets(CasinoImpactReportModel model) =>
        model.Scenario.ParameterSets.Count == 0 ? "None stored" : string.Join("; ", model.Scenario.ParameterSets);

    public static string UserOverrides(CasinoImpactReportModel model)
    {
        var overrides = model.Parameters.Where(parameter => parameter.UserOverrideValue is not null)
            .Select(parameter => $"{parameter.Key}={parameter.UserOverrideValue:G7} {parameter.Units}")
            .ToArray();
        return overrides.Length == 0 ? "None" : string.Join("; ", overrides);
    }

    public static string SourceVintages(CasinoImpactReportModel model) =>
        model.DataSources.Count == 0
            ? "None stored"
            : string.Join("; ", model.DataSources.Select(source =>
                $"{source.Role}:{source.ReferenceKey}={source.DatasetKey}@{source.Period} [{ShortHash(source.Checksum)}]"));

    private static string ShortHash(string value) => value.Length <= 12 ? value : value[..12];
}

public sealed class ReportArtifactService(
    AppDbContext db,
    ICasinoImpactReportModelFactory reportModelFactory,
    IHtmlReportRenderer htmlRenderer,
    IPdfReportRenderer pdfRenderer,
    ICsvReportRenderer csvRenderer) : IReportArtifactService
{
    public const string TemplateVersion = "professional-v5";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ModelRunReportArtifact> GetOrCreateAsync(
        Guid modelRunId,
        ReportPresentationOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);
        var normalizedOptions = options with
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? null : options.Title.Trim(),
            PreparedFor = string.IsNullOrWhiteSpace(options.PreparedFor) ? null : options.PreparedFor.Trim(),
            CurrencyCode = options.CurrencyCode.Trim().ToUpperInvariant()
        };
        var optionsJson = JsonSerializer.Serialize(normalizedOptions, JsonOptions);
        var optionsHash = Hash(Encoding.UTF8.GetBytes(optionsJson));
        var existing = await db.ModelRunReportArtifacts.AsNoTracking()
            .SingleOrDefaultAsync(artifact => artifact.ModelRunId == modelRunId &&
                                              artifact.TemplateVersion == TemplateVersion &&
                                              artifact.PresentationOptionsHash == optionsHash,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var generatedAtUtc = DateTime.UtcNow;
        var model = await reportModelFactory.BuildAsync(
            modelRunId,
            TemplateVersion,
            generatedAtUtc,
            normalizedOptions,
            cancellationToken);
        var modelJson = JsonSerializer.Serialize(model, JsonOptions);
        var html = htmlRenderer.Render(model, normalizedOptions);
        var pdf = pdfRenderer.Render(model, normalizedOptions);
        var csv = csvRenderer.Render(model);
        var artifact = new ModelRunReportArtifact
        {
            ModelRunId = modelRunId,
            TemplateVersion = TemplateVersion,
            PresentationOptionsJson = optionsJson,
            PresentationOptionsHash = optionsHash,
            ReportModelJson = modelJson,
            ReportModelHash = Hash(Encoding.UTF8.GetBytes(modelJson)),
            HtmlContent = html,
            HtmlContentHash = Hash(Encoding.UTF8.GetBytes(html)),
            PdfContent = pdf,
            PdfContentHash = Hash(pdf),
            CsvContent = csv,
            CsvContentHash = Hash(Encoding.UTF8.GetBytes(csv)),
            GeneratedAtUtc = generatedAtUtc,
            IsImmutable = true
        };
        db.ModelRunReportArtifacts.Add(artifact);
        await db.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void ValidateOptions(ReportPresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TopOriginCount is < 5 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Top origin count must be between 5 and 100.");
        }
        if (options.Title?.Length > 200 || options.PreparedFor?.Length > 200)
        {
            throw new ArgumentException("Report title and prepared-for label are limited to 200 characters.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.CurrencyCode) || options.CurrencyCode.Trim().Length != 3)
        {
            throw new ArgumentException("A three-letter currency code is required.", nameof(options));
        }
    }
}

internal static class ReportExhibitBuilder
{
    private sealed record MapPoint(
        string Label,
        double Latitude,
        double Longitude,
        string Kind,
        double Weight);

    public static string CoordinateMapSvg(CasinoImpactReportModel model, bool includeOrigins)
    {
        var points = new List<MapPoint>();
        if (includeOrigins)
        {
            points.AddRange(model.Origins
                .Where(origin => ValidCoordinate(origin.Latitude, origin.Longitude))
                .Select(origin => new MapPoint(
                    origin.StableOriginId,
                    origin.Latitude,
                    origin.Longitude,
                    "origin",
                    Math.Max(0, origin.ShareOfProposedResidentGgr))));
            points.AddRange(model.Facilities
                .Where(facility => facility.IsProposedFacility && ValidCoordinate(facility.Latitude, facility.Longitude))
                .Select(facility => new MapPoint(facility.FacilityName, facility.Latitude, facility.Longitude, "proposed", 1)));
        }
        else
        {
            points.AddRange(model.Facilities
                .Where(facility => ValidCoordinate(facility.Latitude, facility.Longitude))
                .Select(facility => new MapPoint(
                    facility.FacilityName,
                    facility.Latitude,
                    facility.Longitude,
                    facility.IsProposedFacility ? "proposed" : "incumbent",
                    facility.IsProposedFacility ? 1 : Math.Max(0.05, facility.NormalizedAttraction))));
        }

        if (points.Count == 0)
        {
            return string.Empty;
        }

        const double width = 760;
        const double height = 360;
        const double left = 55;
        const double right = 25;
        const double top = 24;
        const double bottom = 42;
        var minimumLongitude = points.Min(point => point.Longitude);
        var maximumLongitude = points.Max(point => point.Longitude);
        var minimumLatitude = points.Min(point => point.Latitude);
        var maximumLatitude = points.Max(point => point.Latitude);
        var longitudePadding = Math.Max((maximumLongitude - minimumLongitude) * 0.12, 0.08);
        var latitudePadding = Math.Max((maximumLatitude - minimumLatitude) * 0.12, 0.08);
        minimumLongitude -= longitudePadding;
        maximumLongitude += longitudePadding;
        minimumLatitude -= latitudePadding;
        maximumLatitude += latitudePadding;
        var plotWidth = width - left - right;
        var plotHeight = height - top - bottom;
        var maximumOriginWeight = Math.Max(
            points.Where(point => point.Kind == "origin").Select(point => point.Weight).DefaultIfEmpty(0).Max(),
            double.Epsilon);

        double X(double longitude) => left + (longitude - minimumLongitude) / (maximumLongitude - minimumLongitude) * plotWidth;
        double Y(double latitude) => top + (maximumLatitude - latitude) / (maximumLatitude - minimumLatitude) * plotHeight;

        var svg = new StringBuilder(12_000);
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 760 360\" role=\"img\" aria-label=\"")
            .Append(includeOrigins ? "Patron-origin intensity map" : "Proposed site and competitive facilities map")
            .Append("\"><rect x=\"0\" y=\"0\" width=\"760\" height=\"360\" fill=\"#f8fafc\"/>");
        for (var index = 0; index <= 4; index++)
        {
            var gridX = left + plotWidth * index / 4;
            var gridY = top + plotHeight * index / 4;
            var longitude = minimumLongitude + (maximumLongitude - minimumLongitude) * index / 4;
            var latitude = maximumLatitude - (maximumLatitude - minimumLatitude) * index / 4;
            svg.Append("<line x1=\"").Append(F(gridX)).Append("\" y1=\"").Append(F(top)).Append("\" x2=\"")
                .Append(F(gridX)).Append("\" y2=\"").Append(F(top + plotHeight)).Append("\" stroke=\"#dbe2ea\" stroke-width=\"1\"/>")
                .Append("<line x1=\"").Append(F(left)).Append("\" y1=\"").Append(F(gridY)).Append("\" x2=\"")
                .Append(F(left + plotWidth)).Append("\" y2=\"").Append(F(gridY)).Append("\" stroke=\"#dbe2ea\" stroke-width=\"1\"/>")
                .Append("<text x=\"").Append(F(gridX)).Append("\" y=\"348\" text-anchor=\"middle\" font-size=\"11\" fill=\"#526174\">")
                .Append(F(longitude, 2)).Append("°</text>")
                .Append("<text x=\"48\" y=\"").Append(F(gridY + 4)).Append("\" text-anchor=\"end\" font-size=\"11\" fill=\"#526174\">")
                .Append(F(latitude, 2)).Append("°</text>");
        }

        foreach (var point in points.OrderBy(point => point.Kind == "origin" ? 0 : 1).ThenBy(point => point.Label, StringComparer.Ordinal))
        {
            var x = X(point.Longitude);
            var y = Y(point.Latitude);
            var title = WebUtility.HtmlEncode(
                $"{point.Label} · {point.Latitude:F6}, {point.Longitude:F6}" +
                (point.Kind == "origin" ? $" · {point.Weight:P2} of proposed resident GGR" : string.Empty));
            if (point.Kind == "origin")
            {
                var radius = 4 + 11 * Math.Sqrt(point.Weight / maximumOriginWeight);
                svg.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"")
                    .Append(F(radius)).Append("\" fill=\"#256c8f\" fill-opacity=\"0.42\" stroke=\"#164e63\" stroke-width=\"1\"><title>")
                    .Append(title).Append("</title></circle>");
            }
            else if (point.Kind == "proposed")
            {
                svg.Append("<polygon points=\"").Append(F(x)).Append(',').Append(F(y - 9)).Append(' ')
                    .Append(F(x + 9)).Append(',').Append(F(y)).Append(' ').Append(F(x)).Append(',').Append(F(y + 9)).Append(' ')
                    .Append(F(x - 9)).Append(',').Append(F(y)).Append("\" fill=\"#0f2948\" stroke=\"#ffffff\" stroke-width=\"2\"><title>")
                    .Append(title).Append("</title></polygon>")
                    .Append("<text x=\"").Append(F(x + 12)).Append("\" y=\"").Append(F(y - 7)).Append("\" font-size=\"11\" font-weight=\"700\" fill=\"#0f2948\">")
                    .Append(WebUtility.HtmlEncode(ShortLabel(point.Label))).Append("</text>");
            }
            else
            {
                svg.Append("<rect x=\"").Append(F(x - 5)).Append("\" y=\"").Append(F(y - 5)).Append("\" width=\"10\" height=\"10\" fill=\"#c26b36\" stroke=\"#ffffff\" stroke-width=\"1\"><title>")
                    .Append(title).Append("</title></rect>");
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    public static string PatronOriginChoroplethSvg(CasinoImpactReportModel model)
    {
        var reader = new WKTReader();
        var areas = new List<(ReportOrigin Origin, Geometry Geometry)>();
        foreach (var origin in model.Origins.Where(origin => !string.IsNullOrWhiteSpace(origin.AreaGeometryWkt)))
        {
            try
            {
                var geometry = reader.Read(origin.AreaGeometryWkt!);
                if (!geometry.IsEmpty && geometry is Polygon or MultiPolygon)
                {
                    areas.Add((origin, geometry));
                }
            }
            catch (ParseException)
            {
                // Invalid source geometry is omitted from the presentation exhibit; numeric origin results remain intact.
            }
        }
        if (areas.Count == 0)
        {
            return string.Empty;
        }

        var coordinates = areas.SelectMany(area => area.Geometry.Coordinates)
            .Where(coordinate => ValidCoordinate(coordinate.Y, coordinate.X))
            .Select(coordinate => (Latitude: coordinate.Y, Longitude: coordinate.X))
            .Append((model.Scenario.CandidateLatitude, model.Scenario.CandidateLongitude))
            .ToArray();
        var projection = MapProjection.Create(coordinates);
        var maximumShare = Math.Max(areas.Max(area => area.Origin.ShareOfProposedResidentGgr), double.Epsilon);
        var svg = StartProjectedMap(projection, "Patron-origin contribution choropleth");
        foreach (var area in areas.OrderBy(area => area.Origin.ShareOfProposedResidentGgr))
        {
            var ratio = Math.Clamp(area.Origin.ShareOfProposedResidentGgr / maximumShare, 0, 1);
            var fill = ratio switch
            {
                <= 0.2 => "#dbeafe",
                <= 0.4 => "#93c5fd",
                <= 0.7 => "#3b82f6",
                _ => "#1d4ed8"
            };
            svg.Append("<path d=\"");
            AppendGeometryPath(svg, area.Geometry, projection);
            svg.Append("\" fill=\"").Append(fill)
                .Append("\" fill-rule=\"evenodd\" stroke=\"#ffffff\" stroke-width=\"1\"><title>")
                .Append(WebUtility.HtmlEncode($"{area.Origin.StableOriginId} · {area.Origin.ShareOfProposedResidentGgr.ToString("P2", CultureInfo.InvariantCulture)} · {area.Origin.TotalProposedResidentGgr.ToString("N0", CultureInfo.InvariantCulture)} USD proposed resident GGR"))
                .Append("</title></path>");
        }
        AppendProposedSite(svg, projection, model.Scenario.CandidateLatitude, model.Scenario.CandidateLongitude);
        svg.Append("</svg>");
        return svg.ToString();
    }

    public static string CandidateTravelTimeMapSvg(CasinoImpactReportModel model)
    {
        var origins = model.Origins
            .Where(origin => origin.CandidateRouteFound && origin.CandidateTravelTimeMinutes is >= 0 &&
                             ValidCoordinate(origin.Latitude, origin.Longitude))
            .ToArray();
        if (origins.Length == 0 || !ValidCoordinate(model.Scenario.CandidateLatitude, model.Scenario.CandidateLongitude))
        {
            return string.Empty;
        }

        var coordinates = origins.Select(origin => (origin.Latitude, origin.Longitude))
            .Append((model.Scenario.CandidateLatitude, model.Scenario.CandidateLongitude));
        var projection = MapProjection.Create(coordinates);
        var svg = StartProjectedMap(projection, "Persisted origin-to-candidate routed travel-time map");
        foreach (var origin in origins.OrderByDescending(origin => origin.CandidateTravelTimeMinutes))
        {
            var minutes = origin.CandidateTravelTimeMinutes!.Value;
            var fill = minutes switch
            {
                <= 15 => "#166534",
                <= 30 => "#65a30d",
                <= 60 => "#eab308",
                <= 90 => "#ea580c",
                _ => "#b91c1c"
            };
            var x = projection.X(origin.Longitude);
            var y = projection.Y(origin.Latitude);
            var radius = 5 + 7 * Math.Sqrt(Math.Max(0, origin.ShareOfProposedResidentGgr));
            svg.Append("<circle cx=\"").Append(F(x)).Append("\" cy=\"").Append(F(y)).Append("\" r=\"")
                .Append(F(radius)).Append("\" fill=\"").Append(fill)
                .Append("\" fill-opacity=\"0.82\" stroke=\"#ffffff\" stroke-width=\"1.5\"><title>")
                .Append(WebUtility.HtmlEncode($"{origin.StableOriginId} · {minutes:F1} routed minutes · {origin.CandidateRoutedDistanceMeters.GetValueOrDefault():N0} routed meters"))
                .Append("</title></circle>");
        }
        AppendProposedSite(svg, projection, model.Scenario.CandidateLatitude, model.Scenario.CandidateLongitude);
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static StringBuilder StartProjectedMap(MapProjection projection, string ariaLabel)
    {
        var svg = new StringBuilder(20_000);
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 760 360\" role=\"img\" aria-label=\"")
            .Append(WebUtility.HtmlEncode(ariaLabel))
            .Append("\"><rect x=\"0\" y=\"0\" width=\"760\" height=\"360\" fill=\"#f8fafc\"/>");
        for (var index = 0; index <= 4; index++)
        {
            var gridX = projection.Left + projection.PlotWidth * index / 4;
            var gridY = projection.Top + projection.PlotHeight * index / 4;
            var longitude = projection.MinimumLongitude + (projection.MaximumLongitude - projection.MinimumLongitude) * index / 4;
            var latitude = projection.MaximumLatitude - (projection.MaximumLatitude - projection.MinimumLatitude) * index / 4;
            svg.Append("<line x1=\"").Append(F(gridX)).Append("\" y1=\"").Append(F(projection.Top)).Append("\" x2=\"")
                .Append(F(gridX)).Append("\" y2=\"").Append(F(projection.Top + projection.PlotHeight)).Append("\" stroke=\"#dbe2ea\" stroke-width=\"1\"/>")
                .Append("<line x1=\"").Append(F(projection.Left)).Append("\" y1=\"").Append(F(gridY)).Append("\" x2=\"")
                .Append(F(projection.Left + projection.PlotWidth)).Append("\" y2=\"").Append(F(gridY)).Append("\" stroke=\"#dbe2ea\" stroke-width=\"1\"/>")
                .Append("<text x=\"").Append(F(gridX)).Append("\" y=\"348\" text-anchor=\"middle\" font-size=\"11\" fill=\"#526174\">")
                .Append(F(longitude, 2)).Append("°</text>")
                .Append("<text x=\"48\" y=\"").Append(F(gridY + 4)).Append("\" text-anchor=\"end\" font-size=\"11\" fill=\"#526174\">")
                .Append(F(latitude, 2)).Append("°</text>");
        }
        return svg;
    }

    private static void AppendGeometryPath(StringBuilder svg, Geometry geometry, MapProjection projection)
    {
        if (geometry is Polygon polygon)
        {
            AppendPolygonPath(svg, polygon, projection);
            return;
        }
        if (geometry is MultiPolygon multiPolygon)
        {
            for (var index = 0; index < multiPolygon.NumGeometries; index++)
            {
                AppendPolygonPath(svg, (Polygon)multiPolygon.GetGeometryN(index), projection);
            }
        }
    }

    private static void AppendPolygonPath(StringBuilder svg, Polygon polygon, MapProjection projection)
    {
        AppendRingPath(svg, polygon.ExteriorRing.Coordinates, projection);
        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            AppendRingPath(svg, polygon.GetInteriorRingN(index).Coordinates, projection);
        }
    }

    private static void AppendRingPath(StringBuilder svg, IReadOnlyList<Coordinate> coordinates, MapProjection projection)
    {
        for (var index = 0; index < coordinates.Count; index++)
        {
            svg.Append(index == 0 ? 'M' : 'L')
                .Append(F(projection.X(coordinates[index].X))).Append(',')
                .Append(F(projection.Y(coordinates[index].Y)));
        }
        svg.Append('Z');
    }

    private static void AppendProposedSite(StringBuilder svg, MapProjection projection, double latitude, double longitude)
    {
        var x = projection.X(longitude);
        var y = projection.Y(latitude);
        svg.Append("<polygon points=\"").Append(F(x)).Append(',').Append(F(y - 9)).Append(' ')
            .Append(F(x + 9)).Append(',').Append(F(y)).Append(' ').Append(F(x)).Append(',').Append(F(y + 9)).Append(' ')
            .Append(F(x - 9)).Append(',').Append(F(y)).Append("\" fill=\"#0f2948\" stroke=\"#ffffff\" stroke-width=\"2\"><title>Proposed site</title></polygon>");
    }

    private sealed record MapProjection(
        double MinimumLongitude,
        double MaximumLongitude,
        double MinimumLatitude,
        double MaximumLatitude)
    {
        public double Left => 55;
        public double Top => 24;
        public double PlotWidth => 680;
        public double PlotHeight => 294;

        public double X(double longitude) => Left + (longitude - MinimumLongitude) / (MaximumLongitude - MinimumLongitude) * PlotWidth;
        public double Y(double latitude) => Top + (MaximumLatitude - latitude) / (MaximumLatitude - MinimumLatitude) * PlotHeight;

        public static MapProjection Create(IEnumerable<(double Latitude, double Longitude)> coordinates)
        {
            var values = coordinates.Where(value => ValidCoordinate(value.Latitude, value.Longitude)).ToArray();
            var minimumLongitude = values.Min(value => value.Longitude);
            var maximumLongitude = values.Max(value => value.Longitude);
            var minimumLatitude = values.Min(value => value.Latitude);
            var maximumLatitude = values.Max(value => value.Latitude);
            var longitudePadding = Math.Max((maximumLongitude - minimumLongitude) * 0.12, 0.02);
            var latitudePadding = Math.Max((maximumLatitude - minimumLatitude) * 0.12, 0.02);
            return new MapProjection(
                minimumLongitude - longitudePadding,
                maximumLongitude + longitudePadding,
                minimumLatitude - latitudePadding,
                maximumLatitude + latitudePadding);
        }
    }

    private static bool ValidCoordinate(double latitude, double longitude) =>
        double.IsFinite(latitude) && double.IsFinite(longitude) && latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private static string ShortLabel(string value) => value.Length <= 36 ? value : value[..33] + "…";
    private static string F(double value, int decimals = 1) => value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
}

public sealed class HtmlReportRenderer : IHtmlReportRenderer
{
    public string Render(CasinoImpactReportModel model, ReportPresentationOptions options)
    {
        var html = new StringBuilder(64_000);
        var title = options.Title ?? $"Casino Revenue and Economic-Impact Analysis — {model.Scenario.Name}";
        var warningDigest = ReportDisclosure.DigestWarnings(model.Warnings);
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(E(title)).Append("</title>")
            .Append("<style>")
            .Append("@page{size:letter;margin:.65in}body{font:15px/1.5 'Public Sans',Arial,sans-serif;color:#172033;margin:0;background:#fff}")
            .Append("main{max-width:1050px;margin:auto;padding:36px}h1{font-size:34px;line-height:1.15;color:#0f2948;margin:0 0 12px}h2{font-size:23px;color:#0f2948;border-bottom:2px solid #cbd5e1;padding-bottom:7px;margin-top:36px}h3{font-size:18px;color:#244d74;margin-top:25px}")
            .Append(".eyebrow{font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:#2c6b8f}.subtitle{font-size:18px;color:#526174}.meta,.note{color:#526174}.metrics{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px}.metric{border:1px solid #cbd5e1;border-radius:8px;padding:14px}.metric strong{display:block;font-size:20px;color:#0f2948}")
            .Append("table{width:100%;border-collapse:collapse;margin:14px 0 24px;font-size:13px}th{text-align:left;background:#e8eef4;color:#0f2948}th,td{padding:8px;border-bottom:1px solid #dbe2ea;vertical-align:top}.number{text-align:right;font-variant-numeric:tabular-nums}.warning{border-left:4px solid #b45309;background:#fff7ed;padding:9px 12px;margin:8px 0}.repro{background:#eef3f7;padding:14px;border-radius:8px;font-family:ui-monospace,monospace;font-size:12px;overflow-wrap:anywhere}.chart,.map-exhibit{border:1px solid #dbe2ea;border-radius:8px;padding:14px;margin:14px 0 24px}.chart-row{display:grid;grid-template-columns:minmax(160px,1.2fr) 3fr minmax(100px,.8fr);gap:10px;align-items:center;margin:8px 0}.chart-track{height:12px;background:#e8eef4;border-radius:3px;overflow:hidden}.chart-bar{height:100%;background:#2c6b8f}.chart-value{text-align:right;font-variant-numeric:tabular-nums;font-weight:600}.map-exhibit svg{display:block;width:100%;height:auto;background:#f8fafc}.map-caption{display:flex;gap:18px;flex-wrap:wrap;color:#526174;font-size:12px}.legend-dot{display:inline-block;width:10px;height:10px;border-radius:50%;margin-right:5px}.bridge-row{display:grid;grid-template-columns:minmax(190px,1.3fr) 3fr minmax(100px,.8fr);gap:10px;align-items:center;margin:8px 0}.bridge-track{position:relative;height:16px;background:linear-gradient(90deg,#f1f5f9 0 49.6%,#94a3b8 49.6% 50.4%,#f1f5f9 50.4% 100%);border-radius:3px}.tornado-track{position:relative;height:22px;background:linear-gradient(90deg,#f1f5f9 0 49.6%,#94a3b8 49.6% 50.4%,#f1f5f9 50.4% 100%);border-radius:3px}.bridge-bar{position:absolute;top:2px;height:12px;border-radius:2px}.tornado-low,.tornado-high{position:absolute;height:8px;border-radius:2px}.tornado-low{top:2px;background:#c26b36}.tornado-high{top:12px;background:#256c8f}.bridge-bar.positive{background:#256c8f}.bridge-bar.negative{background:#c26b36}.bridge-total{border-top:1px solid #94a3b8;padding-top:8px;font-weight:700}.tornado-row{display:grid;grid-template-columns:minmax(190px,1.3fr) 3fr minmax(150px,1fr);gap:10px;align-items:center;margin:9px 0}")
            .Append("@media print{main{padding:0}.page-break{break-before:page}a{color:inherit;text-decoration:none}}@media(max-width:700px){main{padding:20px}.metrics{grid-template-columns:1fr}table{display:block;overflow:auto}}")
            .Append("</style></head><body><main>");
        html.Append("<header><div class=\"eyebrow\">Stored model-run report</div><h1>").Append(E(title)).Append("</h1>")
            .Append("<p class=\"subtitle\">Dynamic gravity, revenue, fiscal, employment, displacement, and social-cost analysis</p>");
        if (!string.IsNullOrWhiteSpace(options.PreparedFor))
        {
            html.Append("<p><strong>Prepared for:</strong> ").Append(E(options.PreparedFor)).Append("</p>");
        }
        html.Append("<p class=\"meta\">Run ").Append(model.Identity.ModelRunId).Append(" · ")
            .Append(E(model.Identity.JurisdictionName)).Append(" · generated ")
            .Append(model.Identity.GeneratedAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Append("</p></header>");

        Section(html, "Executive summary");
        html.Append("<div class=\"metrics\">");
        Metric(html, "Stabilized GGR", Money(model.Revenue.StabilizedTotalGgr));
        Metric(html, "Resident GGR", Money(model.Revenue.TotalResidentGgr));
        Metric(html, "Tourism + traffic GGR", Money(model.Revenue.TourismGgr + model.Revenue.TrafficGgr));
        Metric(html, "Net permanent jobs", model.Employment?.NetPermanentJobs.ToString("N1", CultureInfo.InvariantCulture) ?? "Not available");
        Metric(html, "Net host-local impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostLocalImpact));
        Metric(html, "Net host-state impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostStateImpact));
        html.Append("</div>");
        if (model.Warnings.Count > 0)
        {
            Subsection(html, "Run warning summary");
            html.Append("<div class=\"warning\"><strong>").Append(E(warningDigest.Summary)).Append("</strong></div>");
            foreach (var warning in warningDigest.DecisionWarnings.Take(6))
            {
                html.Append("<div class=\"warning\">").Append(E(warning)).Append("</div>");
            }
            if (warningDigest.DecisionWarnings.Count > 6)
            {
                html.Append("<p class=\"note\">").Append(warningDigest.DecisionWarnings.Count - 6)
                    .Append(" additional decision-use warning(s) are listed in the methodology disclosure.</p>");
            }
        }

        Section(html, "Proposed development and site");
        TwoColumnTable(html,
        [
            ("Scenario", model.Scenario.Name),
            ("Site coordinates", $"{model.Scenario.CandidateLatitude:F6}, {model.Scenario.CandidateLongitude:F6}"),
            ("Development program", $"{model.DevelopmentProgram.Name} v{model.DevelopmentProgram.Version}"),
            ("Slots / VLT positions", model.DevelopmentProgram.SlotOrVltPositions.ToString("N0")),
            ("Table games", model.DevelopmentProgram.TableGameCount.ToString("N0")),
            ("Hotel rooms", model.DevelopmentProgram.HotelRoomCount.ToString("N0")),
            ("Demand specification", model.Scenario.DemandSpecification),
            ("Attraction specification", model.Scenario.AttractionSpecification),
            ("Travel friction", model.Scenario.FrictionForm),
            ("Computational origin", model.Scenario.ComputationalOriginType),
            ("Impact geography", $"{model.Scenario.ImpactScopeKind}: {model.Scenario.ImpactScopeCode}")
        ]);

        Section(html, "Study area and market definition");
        TwoColumnTable(html,
        [
            ("Impact scope", $"{model.Scenario.ImpactScopeKind}: {model.Scenario.ImpactScopeCode}"),
            ("Computational origin type", model.Scenario.ComputationalOriginType),
            ("Modeled origins", model.Origins.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("States / territories represented", model.OriginStates.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("Counties / parishes represented", model.OriginCounties.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("Competitive facilities represented", model.Facilities.Count.ToString("N0", CultureInfo.InvariantCulture))
        ]);
        CoordinateMap(html, "Proposed site and competitive facilities", model, includeOrigins: false);

        Section(html, "Demographics, eligible population, and income");
        html.Append("<p>Eligible population and income are resolved by the stored demand specification and pinned source snapshots. The renderer reports the resulting resident gaming-demand base without re-estimating it.</p>");
        TwoColumnTable(html,
        [
            ("Demand specification", model.Scenario.DemandSpecification),
            ("Resident demand represented", Money(model.Revenue.TotalResidentDemand)),
            ("Origin records represented", model.Origins.Count.ToString("N0", CultureInfo.InvariantCulture)),
            ("Pinned dataset snapshots", model.DataSources.Count.ToString("N0", CultureInfo.InvariantCulture))
        ]);

        Section(html, "Competitive gaming supply");
        Table(html, ["Facility", "Kind", "Proposed", "Normalized attraction", "Baseline resident GGR"],
            model.Facilities.Select(row => new[]
            {
                row.FacilityName, row.FacilityKind, row.IsProposedFacility ? "Yes" : "No",
                Number(row.NormalizedAttraction), Money(row.BaselineResidentGgr)
            }));

        Section(html, "Gravity model methodology");
        html.Append("<p>The finalized run allocates each origin's stored demand among incumbent facilities, the proposed facility, and the outside option using routed travel impedance and normalized facility attraction. The baseline and with-project systems are solved separately; this report reads their persisted allocations.</p>");
        TwoColumnTable(html,
        [
            ("Demand specification", model.Scenario.DemandSpecification),
            ("Attraction specification", model.Scenario.AttractionSpecification),
            ("Travel-friction form", model.Scenario.FrictionForm),
            ("Routing graph hash(es)", string.Join(", ", model.Identity.RoutingGraphHashes)),
            ("Costing profile(s)", string.Join(", ", model.Identity.CostingProfiles))
        ]);

        Section(html, "Gaming revenue projection");
        TwoColumnTable(html,
        [
            ("Resident demand represented", Money(model.Revenue.TotalResidentDemand)),
            ("Redistributed resident GGR", Money(model.Revenue.RedistributedResidentGgr)),
            ("Accessibility-induced resident GGR", Money(model.Revenue.InducedResidentGgr)),
            ("Tourism GGR", Money(model.Revenue.TourismGgr)),
            ("Through-traffic GGR", Money(model.Revenue.TrafficGgr)),
            ("Stabilized total GGR", Money(model.Revenue.StabilizedTotalGgr))
        ]);
        BarChart(html, "Stabilized GGR composition",
        [
            ("Redistributed resident GGR", model.Revenue.RedistributedResidentGgr),
            ("Accessibility-induced resident GGR", model.Revenue.InducedResidentGgr),
            ("Tourism GGR", model.Revenue.TourismGgr),
            ("Through-traffic GGR", model.Revenue.TrafficGgr)
        ]);
        WaterfallChart(html, "Revenue composition waterfall",
        [
            ("Redistributed resident", model.Revenue.RedistributedResidentGgr),
            ("Accessibility-induced", model.Revenue.InducedResidentGgr),
            ("Tourism", model.Revenue.TourismGgr),
            ("Through traffic", model.Revenue.TrafficGgr)
        ], model.Revenue.StabilizedTotalGgr);
        if (model.Capacity is not null)
        {
            html.Append("<p class=\"note\">Capacity diagnostic range: ").Append(Money(model.Capacity.PlausibleCapacityMinimum))
                .Append(" to ").Append(Money(model.Capacity.PlausibleCapacityMaximum))
                .Append(". Capacity status: ").Append(E(model.Capacity.Status))
                .Append(". Capacity is diagnostic and did not cap modeled GGR.</p>");
        }
        Table(html, ["Calendar year", "Operating year", "Ramp factor", "Projected GGR", "Stabilized"],
            model.Ramp.Select(row => new[]
            {
                row.CalendarYear.ToString(), row.OperatingYearNumber.ToString(), Percent(row.StabilizationShare),
                Money(row.ProjectedGgr), row.PeriodKind
            }));

        Section(html, "Patron-origin analysis");
        CoordinateMap(html, "Patron-origin intensity", model, includeOrigins: true);
        PatronOriginChoropleth(html, model);
        CandidateTravelTimeMap(html, model);
        Subsection(html, "State composition");
        Table(html, ["State", "Origins", "Redistributed GGR", "Induced GGR", "Total", "Share"],
            model.OriginStates.Select(row => new[]
            {
                row.GeographyCode, row.OriginCount.ToString(), Money(row.RedistributedResidentGgr),
                Money(row.InducedResidentGgr), Money(row.TotalProposedResidentGgr), Percent(row.ShareOfProposedResidentGgr)
            }));
        BarChart(html, "State / territory origin contribution",
            model.OriginStates.Take(12).Select(row => (row.GeographyCode, row.TotalProposedResidentGgr)));
        Subsection(html, "County/parish composition");
        Table(html, ["County/parish", "Origins", "Redistributed GGR", "Induced GGR", "Total", "Share"],
            model.OriginCounties.Select(row => new[]
            {
                row.GeographyCode, row.OriginCount.ToString(), Money(row.RedistributedResidentGgr),
                Money(row.InducedResidentGgr), Money(row.TotalProposedResidentGgr), Percent(row.ShareOfProposedResidentGgr)
            }));
        Subsection(html, $"Top {Math.Min(options.TopOriginCount, model.Origins.Count)} contributing origins");
        Table(html, ["Origin", "Type", "State", "County", "Redistributed GGR", "Induced GGR", "Total", "Share"],
            model.Origins.Take(options.TopOriginCount).Select(row => new[]
            {
                row.StableOriginId, row.OriginType, row.StateCode, row.CountyCode ?? "—",
                Money(row.RedistributedResidentGgr), Money(row.InducedResidentGgr),
                Money(row.TotalProposedResidentGgr), Percent(row.ShareOfProposedResidentGgr)
            }));

        Section(html, "Competitive impact and cannibalization");
        Table(html, ["Facility", "Kind", "Baseline resident GGR", "With-project resident GGR", "Change", "Stabilized total"],
            model.Facilities.Select(row => new[]
            {
                row.FacilityName, row.IsProposedFacility ? "Proposed" : "Incumbent", Money(row.BaselineResidentGgr),
                Money(row.WithProjectResidentGgr), Money(row.ChangeInResidentGgr), Money(row.StabilizedTotalGgr)
            }));
        BarChart(html, "Baseline vs with-project resident GGR",
            model.Facilities.Where(row => !row.IsProposedFacility)
                .SelectMany(row => new[]
                {
                    ($"{row.FacilityName} — baseline", row.BaselineResidentGgr),
                    ($"{row.FacilityName} — with project", row.WithProjectResidentGgr)
                }));
        if (model.GeographicAccounting is not null)
        {
            Subsection(html, "Geographic accounting bridge");
            TwoColumnTable(html,
            [
                ("Host-jurisdiction cannibalization", Money(model.GeographicAccounting.HostJurisdictionCannibalization)),
                ("Cross-jurisdiction capture", Money(model.GeographicAccounting.CrossJurisdictionCapture)),
                ("Outside / unmodeled leakage capture", Money(model.GeographicAccounting.OutsideOrUnmodeledLeakageCapture)),
                ("Market expansion and imported GGR", Money(model.GeographicAccounting.MarketExpansionAndImportGgr)),
                ("Transfer-effect GGR", Money(model.GeographicAccounting.TransferEffectGgr))
            ]);
        }

        Section(html, "Tourism and through-traffic demand");
        TwoColumnTable(html,
        [
            ("Tourism GGR", Money(model.Revenue.TourismGgr)),
            ("Through-traffic GGR", Money(model.Revenue.TrafficGgr)),
            ("Stored tourism / traffic demand components", model.DemandComponents.Count(component =>
                component.ComponentType.Contains("tour", StringComparison.OrdinalIgnoreCase) ||
                component.ComponentType.Contains("traffic", StringComparison.OrdinalIgnoreCase)).ToString("N0", CultureInfo.InvariantCulture))
        ]);
        Table(html, ["Component", "Source record", "Input", "Eligible", "Participating", "Captured", "GGR"],
            model.DemandComponents.Where(component =>
                    component.ComponentType.Contains("tour", StringComparison.OrdinalIgnoreCase) ||
                    component.ComponentType.Contains("traffic", StringComparison.OrdinalIgnoreCase))
                .Select(component => new[]
                {
                    component.ComponentType, component.SourceRecordKey, Money(component.InputQuantity),
                    Money(component.EligibleQuantity), Money(component.ParticipatingQuantity),
                    Money(component.CapturedQuantity), Money(component.Ggr)
                }));

        Section(html, "Repatriation and cross-jurisdiction capture");
        if (model.GeographicAccounting is not null)
        {
            TwoColumnTable(html,
            [
                ("Host-jurisdiction cannibalization", Money(model.GeographicAccounting.HostJurisdictionCannibalization)),
                ("Cross-jurisdiction capture", Money(model.GeographicAccounting.CrossJurisdictionCapture)),
                ("Outside / unmodeled leakage capture", Money(model.GeographicAccounting.OutsideOrUnmodeledLeakageCapture)),
                ("Transfer-effect GGR", Money(model.GeographicAccounting.TransferEffectGgr)),
                ("Market expansion and imported GGR", Money(model.GeographicAccounting.MarketExpansionAndImportGgr))
            ]);
        }
        else
        {
            html.Append("<p class=\"note\">Geographic accounting was not stored for this run.</p>");
        }

        Section(html, "Local spending displacement");
        Table(html, ["Sector", "Weight", "Eligible base", "Coefficient", "Displaced sales", "Fiscal loss", "Jobs displaced"],
            model.SectorDisplacement.Select(row => new[]
            {
                row.SectorKey, Percent(row.NormalizedWeight), Money(row.DisplacementEligibleBase),
                Percent(row.DisplacementCoefficient), Money(row.DisplacedSales),
                Money(row.SalesTaxLoss + row.BusinessIncomeTaxLoss), row.DisplacedJobs.ToString("N2")
            }));
        BarChart(html, "Displaced local sales by sector",
            model.SectorDisplacement.Select(row => (row.SectorKey, row.DisplacedSales)));

        Section(html, "Employment and labor-market effects");
        if (model.Employment is not null)
        {
            Subsection(html, "Employment");
            TwoColumnTable(html,
            [
                ("Direct casino jobs", model.Employment.DirectCasinoJobs.ToString("N1")),
                ("Indirect and induced jobs", model.Employment.IndirectAndInducedJobs.ToString("N1")),
                ("Displaced-sector jobs", model.Employment.DisplacedSectorJobs.ToString("N1")),
                ("Incumbent casino jobs lost", model.Employment.IncumbentCasinoJobsLost.ToString("N1")),
                ("Net permanent jobs", model.Employment.NetPermanentJobs.ToString("N1")),
                ("Construction job-years", model.Employment.ConstructionJobYears.ToString("N1"))
            ]);
        }
        Section(html, "Fiscal impact");
        if (model.Fiscal is not null)
        {
            Subsection(html, "Fiscal bridge");
            TwoColumnTable(html,
            [
                ("Gross gaming tax", Money(model.Fiscal.GrossGamingTax)),
                ("Host-local gross public revenue", Money(model.Fiscal.HostLocalGrossPublicRevenue)),
                ("Host-state gross public revenue", Money(model.Fiscal.HostStateGrossPublicRevenue)),
                ("Displaced local fiscal loss", Money(model.Fiscal.DisplacedLocalFiscalLoss)),
                ("Host incumbent gaming-tax loss", Money(model.Fiscal.HostIncumbentGamingTaxLoss)),
                ("Net host-local fiscal impact", Money(model.Fiscal.NetHostLocalFiscalImpact)),
                ("Net host-state fiscal impact", Money(model.Fiscal.NetHostStateFiscalImpact))
            ]);
        }

        Section(html, "Social and downstream costs");
        Table(html, ["Domain", "Incremental cases", "Per-case cost", "Annual cost", "Low", "High", "Included"],
            model.SocialCosts.Select(row => new[]
            {
                row.DomainKey, row.IncrementalCases.ToString("N2"), Money(row.PerCaseCost), Money(row.AnnualCost),
                Money(row.LowAnnualCost), Money(row.HighAnnualCost), row.Included ? "Yes" : "No"
            }));
        WaterfallChart(html, "Social-cost bridge",
            model.SocialCosts.Where(row => row.Included).Select(row => (row.DomainKey, -row.AnnualCost)),
            -model.SocialCosts.Where(row => row.Included).Sum(row => row.AnnualCost));

        Section(html, "Net economic impact");
        if (model.NetImpact is not null)
        {
            TwoColumnTable(html,
            [
                ("Gross property GGR", Money(model.NetImpact.GrossPropertyGgr)),
                ("Transfer-effect GGR", Money(model.NetImpact.TransferEffectGgr)),
                ("Cross-jurisdiction imported GGR", Money(model.NetImpact.CrossJurisdictionImportedGgr)),
                ("Induced resident GGR", Money(model.NetImpact.InducedResidentGgr)),
                ("Tourism and traffic imported GGR", Money(model.NetImpact.TourismAndTrafficImportGgr)),
                ("Local discretionary displacement", Money(model.NetImpact.LocalDiscretionaryDisplacement)),
                ("Gross social cost", Money(model.NetImpact.GrossSocialCost)),
                ("Net host-local impact", Money(model.NetImpact.NetHostLocalImpact)),
                ("Net host-state impact", Money(model.NetImpact.NetHostStateImpact))
            ]);
            WaterfallChart(html, "Net host-local impact waterfall",
            [
                ("Cross-jurisdiction imported GGR", model.NetImpact.CrossJurisdictionImportedGgr),
                ("Induced resident GGR", model.NetImpact.InducedResidentGgr),
                ("Tourism and traffic imported GGR", model.NetImpact.TourismAndTrafficImportGgr),
                ("Local discretionary displacement", -model.NetImpact.LocalDiscretionaryDisplacement),
                ("Net host-local fiscal impact", model.NetImpact.NetHostLocalFiscalImpact),
                ("Gross social cost", -model.NetImpact.GrossSocialCost)
            ], model.NetImpact.NetHostLocalImpact);
        }

        if (model.Sensitivity is not null)
        {
            Section(html, "One-at-a-time sensitivity");
            html.Append("<p class=\"note\">Each point is a complete stored model run. Analysis ")
                .Append(E(model.Sensitivity.Name)).Append(" · ").Append(model.Sensitivity.SensitivityAnalysisId)
                .Append(" · output metric ").Append(E(model.Sensitivity.OutputMetric)).Append(".</p>");
            TornadoChart(html, model.Sensitivity);
            Table(html,
                ["Parameter", "Low input", "Low result", "Baseline input", "Baseline result", "High input", "High result", "Full range", "Point run IDs"],
                model.Sensitivity.Rows.Select(row => new[]
                {
                    row.ParameterKey,
                    Number(row.LowParameterValue), SensitivityValue(row.LowMetricValue, model.Sensitivity.OutputUnits),
                    Number(row.BaseParameterValue), SensitivityValue(row.BaseMetricValue, model.Sensitivity.OutputUnits),
                    Number(row.HighParameterValue), SensitivityValue(row.HighMetricValue, model.Sensitivity.OutputUnits),
                    SensitivityValue(row.TotalRange, model.Sensitivity.OutputUnits),
                    $"low={row.LowModelRunId:D}; high={row.HighModelRunId:D}"
                }));
        }

        if (model.Benchmarks.Count > 0)
        {
            Section(html, "Benchmark and validation reconciliation");
            Table(html, ["Case", "Market", "Partition", "Observed", "Predicted", "Residual", "Source"],
                model.Benchmarks.Select(row => new[]
                {
                    row.CaseName, row.MarketCode, row.DatasetPartition, Money(row.ObservedRevenue),
                    Money(row.PredictedRevenue), Money(row.Residual), row.ConsultantOrSource ?? "Stored validation case"
                }));
        }

        Section(html, "Methodology, limitations, and disclosure");
        html.Append("<p>The report presents persisted outputs from the finalized gravity run. It does not recompute gravity allocation, revenue, fiscal effects, employment, displacement, or social costs.</p><ul>");
        foreach (var limitation in model.Limitations)
        {
            html.Append("<li>").Append(E(limitation)).Append("</li>");
        }
        html.Append("</ul>");
        if (model.Warnings.Count > 0)
        {
            Subsection(html, "Complete decision-use warning disclosure");
            html.Append("<p>").Append(E(warningDigest.Summary)).Append("</p>");
            foreach (var warning in warningDigest.DecisionWarnings)
            {
                html.Append("<div class=\"warning\">").Append(E(warning)).Append("</div>");
            }
        }
        Section(html, "Technical appendices");
        Subsection(html, "Model parameters and overrides");
        Table(html, ["Parameter", "Category", "Units", "Default", "Scenario", "User override", "Final", "Source", "Recommended range", "Warning"],
            model.Parameters.Select(row => new[]
            {
                row.DisplayName + " (" + row.Key + ")", row.Category, row.Units,
                Number(row.DefaultValue), NullableNumber(row.ScenarioValue), NullableNumber(row.UserOverrideValue),
                Number(row.FinalValue), row.SourceLayer,
                $"{NullableNumber(row.RecommendedMinimum)} – {NullableNumber(row.RecommendedMaximum)}",
                row.WarningText ?? (row.IsOutsideRecommendedRange ? "Outside recommended range" : "")
            }));
        Subsection(html, "Data sources and vintages");
        Table(html, ["Role", "Dataset", "Period", "Validation", "Transform", "Source", "Checksum"],
            model.DataSources.Select(row => new[]
            {
                row.Role + ":" + row.ReferenceKey, row.DatasetKey, row.Period, row.ValidationState,
                row.TransformVersion, row.SourceName, row.Checksum
            }));

        Section(html, "Reproducibility statement");
        html.Append("<div class=\"repro\">")
            .Append("Model version: ").Append(E(model.Identity.ModelVersion)).Append("<br>")
            .Append("Report template: ").Append(E(model.Identity.TemplateVersion)).Append("<br>")
            .Append("Run UUID: ").Append(model.Identity.ModelRunId).Append("<br>")
            .Append("Generated timestamp: ").Append(model.Identity.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("<br>")
            .Append("Run created/finalized: ").Append(model.Identity.RunCreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append(" / ")
            .Append(model.Identity.RunFinalizedAtUtc.ToString("O", CultureInfo.InvariantCulture)).Append("<br>")
            .Append("Jurisdiction profile/version: ").Append(E($"{model.Identity.JurisdictionCode} / {model.Identity.JurisdictionProfileVersion}")).Append("<br>")
            .Append("Parameter-set version(s): ").Append(E(ReportDisclosure.ParameterSets(model))).Append("<br>")
            .Append("User overrides: ").Append(E(ReportDisclosure.UserOverrides(model))).Append("<br>")
            .Append("Source data vintages: ").Append(E(ReportDisclosure.SourceVintages(model))).Append("<br>")
            .Append("Candidate: ").Append(model.Scenario.CandidateLatitude.ToString("F6", CultureInfo.InvariantCulture)).Append(", ")
            .Append(model.Scenario.CandidateLongitude.ToString("F6", CultureInfo.InvariantCulture)).Append("<br>")
            .Append("Routing graph hash(es): ").Append(E(string.Join(", ", model.Identity.RoutingGraphHashes))).Append("<br>")
            .Append("Costing profile(s): ").Append(E(string.Join(", ", model.Identity.CostingProfiles))).Append("<br>")
            .Append("Development program: ").Append(E($"{model.DevelopmentProgram.StableProgramId}@{model.DevelopmentProgram.Version}"))
            .Append(model.Sensitivity is null
                ? ""
                : $"<br>Sensitivity analysis: {model.Sensitivity.SensitivityAnalysisId:D}")
            .Append("</div></main></body></html>");
        return html.ToString();
    }

    private static void Section(StringBuilder html, string title) => html.Append("<h2>").Append(E(title)).Append("</h2>");
    private static void Subsection(StringBuilder html, string title) => html.Append("<h3>").Append(E(title)).Append("</h3>");
    private static void Metric(StringBuilder html, string label, string value) => html.Append("<div class=\"metric\"><span>").Append(E(label)).Append("</span><strong>").Append(E(value)).Append("</strong></div>");
    private static void TwoColumnTable(StringBuilder html, IEnumerable<(string Label, string Value)> rows) =>
        Table(html, ["Measure", "Value"], rows.Select(row => new[] { row.Label, row.Value }));

    private static void BarChart(StringBuilder html, string title, IEnumerable<(string Label, decimal Value)> rows)
    {
        var values = rows.ToArray();
        if (values.Length == 0)
        {
            return;
        }
        var maximum = values.Max(row => Math.Abs(row.Value));
        html.Append("<div class=\"chart\"><strong>").Append(E(title)).Append("</strong>");
        foreach (var row in values)
        {
            var width = maximum == 0 ? 0 : Math.Abs(row.Value) / maximum * 100;
            html.Append("<div class=\"chart-row\"><span>").Append(E(row.Label))
                .Append("</span><div class=\"chart-track\"><div class=\"chart-bar\" style=\"width:")
                .Append(width.ToString("F2", CultureInfo.InvariantCulture)).Append("%\"></div></div><span class=\"chart-value\">")
                .Append(E(Money(row.Value))).Append("</span></div>");
        }
        html.Append("</div>");
    }

    private static void CoordinateMap(
        StringBuilder html,
        string title,
        CasinoImpactReportModel model,
        bool includeOrigins)
    {
        var svg = ReportExhibitBuilder.CoordinateMapSvg(model, includeOrigins);
        if (string.IsNullOrWhiteSpace(svg))
        {
            return;
        }

        html.Append("<figure class=\"map-exhibit\"><strong>").Append(E(title)).Append("</strong>")
            .Append(svg)
            .Append("<figcaption class=\"map-caption\">")
            .Append("<span><i class=\"legend-dot\" style=\"background:#0f2948\"></i>Proposed site</span>");
        if (includeOrigins)
        {
            html.Append("<span><i class=\"legend-dot\" style=\"background:#256c8f\"></i>Origin contribution (proportional symbol)</span>");
        }
        else
        {
            html.Append("<span><i class=\"legend-dot\" style=\"background:#c26b36\"></i>Incumbent facility</span>");
        }
        html.Append("<span>WGS84 coordinates · schematic equirectangular exhibit · no basemap</span></figcaption></figure>");
    }

    private static void PatronOriginChoropleth(StringBuilder html, CasinoImpactReportModel model)
    {
        var svg = ReportExhibitBuilder.PatronOriginChoroplethSvg(model);
        if (string.IsNullOrWhiteSpace(svg))
        {
            return;
        }
        html.Append("<figure class=\"map-exhibit\"><strong>Patron-origin contribution choropleth</strong>")
            .Append(svg)
            .Append("<figcaption class=\"map-caption\"><span><i class=\"legend-dot\" style=\"background:#dbeafe\"></i>Lower contribution</span>")
            .Append("<span><i class=\"legend-dot\" style=\"background:#1d4ed8\"></i>Higher contribution</span>")
            .Append("<span>Top-N source polygons · contribution-scaled color · geometry simplified 0.002° for presentation · WGS84 · no basemap</span></figcaption></figure>");
    }

    private static void CandidateTravelTimeMap(StringBuilder html, CasinoImpactReportModel model)
    {
        var svg = ReportExhibitBuilder.CandidateTravelTimeMapSvg(model);
        if (string.IsNullOrWhiteSpace(svg))
        {
            return;
        }
        html.Append("<figure class=\"map-exhibit\"><strong>Origin-to-candidate routed travel-time map</strong>")
            .Append(svg)
            .Append("<figcaption class=\"map-caption\"><span><i class=\"legend-dot\" style=\"background:#166534\"></i>≤15 min</span>")
            .Append("<span><i class=\"legend-dot\" style=\"background:#65a30d\"></i>≤30</span>")
            .Append("<span><i class=\"legend-dot\" style=\"background:#eab308\"></i>≤60</span>")
            .Append("<span><i class=\"legend-dot\" style=\"background:#ea580c\"></i>≤90</span>")
            .Append("<span><i class=\"legend-dot\" style=\"background:#b91c1c\"></i>&gt;90</span>")
            .Append("<span>Persisted Valhalla auto minutes at origin representative points · not interpolated isochrones · WGS84 · no basemap</span></figcaption></figure>");
    }

    private static void WaterfallChart(
        StringBuilder html,
        string title,
        IEnumerable<(string Label, decimal Value)> rows,
        decimal storedTotal)
    {
        var values = rows.ToArray();
        if (values.Length == 0)
        {
            return;
        }
        var maximum = Math.Max(values.Select(row => Math.Abs(row.Value)).Append(Math.Abs(storedTotal)).Max(), 1m);
        var running = 0m;
        html.Append("<div class=\"chart\"><strong>").Append(E(title)).Append("</strong>");
        foreach (var row in values)
        {
            running += row.Value;
            var width = Math.Abs(row.Value) / maximum * 50;
            var left = row.Value < 0 ? 50 - width : 50;
            html.Append("<div class=\"bridge-row\"><span>").Append(E(row.Label))
                .Append("</span><div class=\"bridge-track\" role=\"img\" aria-label=\"")
                .Append(E($"{row.Label}: {Money(row.Value)}; running total {Money(running)}"))
                .Append("\"><div class=\"bridge-bar ").Append(row.Value < 0 ? "negative" : "positive")
                .Append("\" style=\"left:").Append(left.ToString("F2", CultureInfo.InvariantCulture))
                .Append("%;width:").Append(width.ToString("F2", CultureInfo.InvariantCulture))
                .Append("%\"></div></div><span class=\"chart-value\">")
                .Append(E(Money(row.Value))).Append("</span></div>");
        }
        html.Append("<div class=\"bridge-row bridge-total\"><span>Stored total</span><span>Exact persisted output</span><span class=\"chart-value\">")
            .Append(E(Money(storedTotal))).Append("</span></div></div>");
    }

    private static void TornadoChart(StringBuilder html, ReportSensitivityAnalysis sensitivity)
    {
        if (sensitivity.Rows.Count == 0)
        {
            return;
        }
        var maximumDelta = Math.Max(
            sensitivity.Rows.SelectMany(row => new[] { Math.Abs(row.LowDelta), Math.Abs(row.HighDelta) }).Max(),
            1m);
        html.Append("<div class=\"chart\"><strong>Sensitivity tornado — ").Append(E(sensitivity.OutputMetric)).Append("</strong>");
        foreach (var row in sensitivity.Rows.OrderByDescending(row => row.TotalRange))
        {
            var lowWidth = Math.Abs(row.LowDelta) / maximumDelta * 50;
            var highWidth = Math.Abs(row.HighDelta) / maximumDelta * 50;
            var lowLeft = row.LowDelta < 0 ? 50 - lowWidth : 50;
            var highLeft = row.HighDelta < 0 ? 50 - highWidth : 50;
            html.Append("<div class=\"tornado-row\"><span>").Append(E(row.ParameterKey))
                .Append("</span><div class=\"tornado-track\" role=\"img\" aria-label=\"")
                .Append(E($"{row.ParameterKey}: low {SensitivityValue(row.LowMetricValue, sensitivity.OutputUnits)}, baseline {SensitivityValue(row.BaseMetricValue, sensitivity.OutputUnits)}, high {SensitivityValue(row.HighMetricValue, sensitivity.OutputUnits)}"))
                .Append("\"><div class=\"tornado-low\" style=\"left:")
                .Append(lowLeft.ToString("F2", CultureInfo.InvariantCulture)).Append("%;width:")
                .Append(lowWidth.ToString("F2", CultureInfo.InvariantCulture)).Append("%\"></div><div class=\"tornado-high\" style=\"left:")
                .Append(highLeft.ToString("F2", CultureInfo.InvariantCulture)).Append("%;width:")
                .Append(highWidth.ToString("F2", CultureInfo.InvariantCulture)).Append("%\"></div></div><span class=\"chart-value\">")
                .Append(E($"{SensitivityValue(row.LowMetricValue, sensitivity.OutputUnits)} – {SensitivityValue(row.HighMetricValue, sensitivity.OutputUnits)}"))
                .Append("</span></div>");
        }
        html.Append("<p class=\"note\">Orange = low parameter setting; blue = high parameter setting. Bar side shows the signed output change from baseline.</p></div>");
    }

    private static void Table(StringBuilder html, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        html.Append("<table><thead><tr>");
        foreach (var header in headers)
        {
            html.Append("<th>").Append(E(header)).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            for (var index = 0; index < row.Count; index++)
            {
                html.Append(index > 0 ? "<td class=\"number\">" : "<td>").Append(E(row[index])).Append("</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table>");
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Money(decimal value) => value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    private static string Money(decimal? value) => value?.ToString("C0", CultureInfo.GetCultureInfo("en-US")) ?? "not evaluated";
    private static string Percent(double value) => value.ToString("P1", CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("G8", CultureInfo.InvariantCulture);
    private static string NullableNumber(double? value) => value?.ToString("G8", CultureInfo.InvariantCulture) ?? "—";
    private static string SensitivityValue(decimal value, string units) => units == "jobs"
        ? value.ToString("N1", CultureInfo.InvariantCulture)
        : Money(value);
}

public sealed class PdfReportRenderer : IPdfReportRenderer
{
    public byte[] Render(CasinoImpactReportModel model, ReportPresentationOptions options)
    {
        var title = options.Title ?? $"Casino Revenue and Economic-Impact Analysis — {model.Scenario.Name}";
        var warningDigest = ReportDisclosure.DigestWarnings(model.Warnings);
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0.6f, Unit.Inch);
                page.DefaultTextStyle(style => style.FontSize(9).FontColor(Colors.BlueGrey.Darken4));
                page.Header().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Text(title).SemiBold().FontColor(Colors.Blue.Darken3);
                    row.ConstantItem(150).AlignRight().Text(model.Identity.ModelRunId.ToString()).FontSize(7).FontColor(Colors.Grey.Darken1);
                });
                page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text($"{model.Identity.ModelVersion} · {model.Identity.TemplateVersion}").FontSize(7);
                    row.AutoItem().DefaultTextStyle(style => style.FontSize(7)).Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Text(title).FontSize(23).Bold().FontColor(Colors.Blue.Darken3);
                    column.Item().Text($"Stored finalized run {model.Identity.ModelRunId:D} · {model.Identity.JurisdictionName}")
                        .FontSize(11).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(options.PreparedFor))
                    {
                        column.Item().Text($"Prepared for: {options.PreparedFor}").SemiBold();
                    }
                    column.Item().Text($"Generated {model.Identity.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC").FontSize(8);

                    Heading(column, "Executive summary");
                    KeyValues(column,
                    [
                        ("Stabilized GGR", Money(model.Revenue.StabilizedTotalGgr)),
                        ("Resident GGR", Money(model.Revenue.TotalResidentGgr)),
                        ("Tourism + traffic GGR", Money(model.Revenue.TourismGgr + model.Revenue.TrafficGgr)),
                        ("Net permanent jobs", model.Employment?.NetPermanentJobs.ToString("N1") ?? "Not available"),
                        ("Net host-local impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostLocalImpact)),
                        ("Net host-state impact", model.NetImpact is null ? "Not available" : Money(model.NetImpact.NetHostStateImpact))
                    ]);
                    if (model.Warnings.Count > 0)
                    {
                        column.Item().BorderLeft(3).BorderColor(Colors.Orange.Darken2).Background(Colors.Orange.Lighten5)
                            .Padding(6).Text(warningDigest.Summary).SemiBold().FontSize(8);
                        foreach (var warning in warningDigest.DecisionWarnings.Take(6))
                        {
                            column.Item().BorderLeft(3).BorderColor(Colors.Orange.Darken2).Background(Colors.Orange.Lighten5)
                                .Padding(6).Text(warning).FontSize(8);
                        }
                        if (warningDigest.DecisionWarnings.Count > 6)
                        {
                            column.Item().Text($"{warningDigest.DecisionWarnings.Count - 6:N0} additional decision-use warning(s) are listed in the methodology disclosure.")
                                .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                        }
                    }

                    Heading(column, "Proposed development and site");
                    KeyValues(column,
                    [
                        ("Scenario", model.Scenario.Name),
                        ("Candidate coordinates", $"{model.Scenario.CandidateLatitude:F6}, {model.Scenario.CandidateLongitude:F6}"),
                        ("Development program", $"{model.DevelopmentProgram.Name} v{model.DevelopmentProgram.Version}"),
                        ("Slots / VLT positions", model.DevelopmentProgram.SlotOrVltPositions.ToString("N0")),
                        ("Table games / hotel rooms", $"{model.DevelopmentProgram.TableGameCount:N0} / {model.DevelopmentProgram.HotelRoomCount:N0}"),
                        ("Demand / attraction", $"{model.Scenario.DemandSpecification} / {model.Scenario.AttractionSpecification}"),
                        ("Travel friction", model.Scenario.FrictionForm),
                        ("Origin / report geography", $"{model.Scenario.ComputationalOriginType} / {model.Scenario.ImpactScopeKind}:{model.Scenario.ImpactScopeCode}")
                    ]);

                    Heading(column, "Study area and market definition");
                    KeyValues(column,
                    [
                        ("Impact scope", $"{model.Scenario.ImpactScopeKind}: {model.Scenario.ImpactScopeCode}"),
                        ("Computational origin type", model.Scenario.ComputationalOriginType),
                        ("Modeled origins", model.Origins.Count.ToString("N0")),
                        ("States / counties represented", $"{model.OriginStates.Count:N0} / {model.OriginCounties.Count:N0}"),
                        ("Competitive facilities", model.Facilities.Count.ToString("N0"))
                    ]);
                    CoordinateMap(column, "Proposed site and competitive facilities", model, includeOrigins: false);

                    Heading(column, "Demographics, eligible population, and income");
                    column.Item().Text("Eligible population and income are resolved by the stored demand specification and pinned source snapshots. The renderer reports the resulting resident gaming-demand base without re-estimating it.")
                        .FontSize(8);
                    KeyValues(column,
                    [
                        ("Demand specification", model.Scenario.DemandSpecification),
                        ("Resident demand represented", Money(model.Revenue.TotalResidentDemand)),
                        ("Origin records", model.Origins.Count.ToString("N0")),
                        ("Pinned dataset snapshots", model.DataSources.Count.ToString("N0"))
                    ]);

                    Heading(column, "Competitive gaming supply");
                    SimpleTable(column,
                        ["Facility", "Kind", "Proposed", "Attraction", "Baseline resident GGR"],
                        model.Facilities.Select(row => new[]
                        {
                            row.FacilityName, row.FacilityKind, row.IsProposedFacility ? "Yes" : "No",
                            row.NormalizedAttraction.ToString("G7"), Money(row.BaselineResidentGgr)
                        }));

                    Heading(column, "Gravity model methodology");
                    column.Item().Text("The finalized run allocates each origin's stored demand among incumbent facilities, the proposed facility, and the outside option using routed travel impedance and normalized facility attraction. Baseline and with-project systems are solved separately; this report reads their persisted allocations.")
                        .FontSize(8);
                    KeyValues(column,
                    [
                        ("Demand / attraction", $"{model.Scenario.DemandSpecification} / {model.Scenario.AttractionSpecification}"),
                        ("Travel-friction form", model.Scenario.FrictionForm),
                        ("Routing graph hash(es)", string.Join(", ", model.Identity.RoutingGraphHashes)),
                        ("Costing profile(s)", string.Join(", ", model.Identity.CostingProfiles))
                    ]);

                    Heading(column, "Gaming revenue projection");
                    KeyValues(column,
                    [
                        ("Resident demand represented", Money(model.Revenue.TotalResidentDemand)),
                        ("Redistributed resident GGR", Money(model.Revenue.RedistributedResidentGgr)),
                        ("Accessibility-induced resident GGR", Money(model.Revenue.InducedResidentGgr)),
                        ("Tourism GGR", Money(model.Revenue.TourismGgr)),
                        ("Through-traffic GGR", Money(model.Revenue.TrafficGgr)),
                        ("Stabilized total GGR", Money(model.Revenue.StabilizedTotalGgr))
                    ]);
                    BarChart(column, "Stabilized GGR composition",
                    [
                        ("Redistributed resident", model.Revenue.RedistributedResidentGgr),
                        ("Accessibility-induced", model.Revenue.InducedResidentGgr),
                        ("Tourism", model.Revenue.TourismGgr),
                        ("Through-traffic", model.Revenue.TrafficGgr)
                    ]);
                    WaterfallChart(column, "Revenue composition waterfall",
                    [
                        ("Redistributed resident", model.Revenue.RedistributedResidentGgr),
                        ("Accessibility-induced", model.Revenue.InducedResidentGgr),
                        ("Tourism", model.Revenue.TourismGgr),
                        ("Through traffic", model.Revenue.TrafficGgr)
                    ], model.Revenue.StabilizedTotalGgr);
                    SimpleTable(column,
                        ["Year", "Operating year", "Ramp", "Projected GGR"],
                        model.Ramp.Select(row => new[]
                        {
                            row.CalendarYear.ToString(), row.OperatingYearNumber.ToString(),
                            row.StabilizationShare.ToString("P1"), Money(row.ProjectedGgr)
                        }));

                    Heading(column, "Patron-origin analysis");
                    CoordinateMap(column, "Patron-origin intensity", model, includeOrigins: true);
                    PatronOriginChoropleth(column, model);
                    CandidateTravelTimeMap(column, model);
                    SimpleTable(column,
                        ["State", "Origins", "Resident GGR", "Share"],
                        model.OriginStates.Select(row => new[]
                        {
                            row.GeographyCode, row.OriginCount.ToString(), Money(row.TotalProposedResidentGgr),
                            row.ShareOfProposedResidentGgr.ToString("P1")
                        }));
                    BarChart(column, "State / territory origin contribution",
                        model.OriginStates.Take(12).Select(row => (row.GeographyCode, row.TotalProposedResidentGgr)));
                    column.Item().Text("County/parish composition").SemiBold();
                    SimpleTable(column,
                        ["County/parish", "Origins", "Resident GGR", "Share"],
                        model.OriginCounties.Select(row => new[]
                        {
                            row.GeographyCode, row.OriginCount.ToString(), Money(row.TotalProposedResidentGgr),
                            row.ShareOfProposedResidentGgr.ToString("P1")
                        }));
                    column.Item().Text($"Top {Math.Min(options.TopOriginCount, model.Origins.Count)} contributing origins").SemiBold();
                    SimpleTable(column,
                        ["Origin", "Type", "State / county", "Resident GGR", "Share"],
                        model.Origins.Take(options.TopOriginCount).Select(row => new[]
                        {
                            row.StableOriginId, row.OriginType, $"{row.StateCode} / {row.CountyCode ?? "—"}",
                            Money(row.TotalProposedResidentGgr), row.ShareOfProposedResidentGgr.ToString("P1")
                        }));

                    Heading(column, "Competitive impact and cannibalization");
                    SimpleTable(column,
                        ["Facility", "Kind", "Baseline", "With project", "Change"],
                        model.Facilities.Select(row => new[]
                        {
                            row.FacilityName, row.IsProposedFacility ? "Proposed" : "Incumbent",
                            Money(row.BaselineResidentGgr), Money(row.WithProjectResidentGgr), Money(row.ChangeInResidentGgr)
                        }));
                    BarChart(column, "Baseline vs with-project resident GGR",
                        model.Facilities.Where(row => !row.IsProposedFacility)
                            .SelectMany(row => new[]
                            {
                                ($"{row.FacilityName} — baseline", row.BaselineResidentGgr),
                                ($"{row.FacilityName} — with project", row.WithProjectResidentGgr)
                            }));

                    Heading(column, "Tourism and through-traffic demand");
                    KeyValues(column,
                    [
                        ("Tourism GGR", Money(model.Revenue.TourismGgr)),
                        ("Through-traffic GGR", Money(model.Revenue.TrafficGgr)),
                        ("Stored demand components", model.DemandComponents.Count(component =>
                            component.ComponentType.Contains("tour", StringComparison.OrdinalIgnoreCase) ||
                            component.ComponentType.Contains("traffic", StringComparison.OrdinalIgnoreCase)).ToString("N0"))
                    ]);

                    Heading(column, "Repatriation and cross-jurisdiction capture");
                    if (model.GeographicAccounting is not null)
                    {
                        KeyValues(column,
                        [
                            ("Host-jurisdiction cannibalization", Money(model.GeographicAccounting.HostJurisdictionCannibalization)),
                            ("Cross-jurisdiction capture", Money(model.GeographicAccounting.CrossJurisdictionCapture)),
                            ("Outside / unmodeled leakage capture", Money(model.GeographicAccounting.OutsideOrUnmodeledLeakageCapture)),
                            ("Transfer-effect GGR", Money(model.GeographicAccounting.TransferEffectGgr)),
                            ("Market expansion and imported GGR", Money(model.GeographicAccounting.MarketExpansionAndImportGgr))
                        ]);
                    }
                    else
                    {
                        column.Item().Text("Geographic accounting was not stored for this run.").Italic().FontSize(8);
                    }

                    Heading(column, "Local spending displacement");
                    SimpleTable(column,
                        ["Sector", "Weight", "Displaced sales", "Fiscal loss", "Jobs"],
                        model.SectorDisplacement.Select(row => new[]
                        {
                            row.SectorKey, row.NormalizedWeight.ToString("P1"), Money(row.DisplacedSales),
                            Money(row.SalesTaxLoss + row.BusinessIncomeTaxLoss), row.DisplacedJobs.ToString("N2")
                        }));
                    BarChart(column, "Displaced local sales by sector",
                        model.SectorDisplacement.Select(row => (row.SectorKey, row.DisplacedSales)));

                    Heading(column, "Employment and labor-market effects");
                    if (model.Employment is not null)
                    {
                        KeyValues(column,
                        [
                            ("Direct casino jobs", model.Employment.DirectCasinoJobs.ToString("N1")),
                            ("Indirect and induced jobs", model.Employment.IndirectAndInducedJobs.ToString("N1")),
                            ("Displaced + incumbent jobs lost", (model.Employment.DisplacedSectorJobs + model.Employment.IncumbentCasinoJobsLost).ToString("N1")),
                            ("Net permanent jobs", model.Employment.NetPermanentJobs.ToString("N1"))
                        ]);
                    }
                    Heading(column, "Fiscal impact");
                    if (model.Fiscal is not null)
                    {
                        KeyValues(column,
                        [
                            ("Gross gaming tax", Money(model.Fiscal.GrossGamingTax)),
                            ("Host-local gross public revenue", Money(model.Fiscal.HostLocalGrossPublicRevenue)),
                            ("Displaced local fiscal loss", Money(model.Fiscal.DisplacedLocalFiscalLoss)),
                            ("Net host-local fiscal impact", Money(model.Fiscal.NetHostLocalFiscalImpact)),
                            ("Net host-state fiscal impact", Money(model.Fiscal.NetHostStateFiscalImpact))
                        ]);
                    }

                    Heading(column, "Social and downstream costs");
                    SimpleTable(column,
                        ["Domain", "Cases", "Annual", "Low", "High"],
                        model.SocialCosts.Select(row => new[]
                        {
                            row.DomainKey, row.IncrementalCases.ToString("N2"), Money(row.AnnualCost),
                            Money(row.LowAnnualCost), Money(row.HighAnnualCost)
                        }));
                    WaterfallChart(column, "Social-cost bridge",
                        model.SocialCosts.Where(row => row.Included).Select(row => (row.DomainKey, -row.AnnualCost)),
                        -model.SocialCosts.Where(row => row.Included).Sum(row => row.AnnualCost));

                    Heading(column, "Net economic impact");
                    if (model.NetImpact is not null)
                    {
                        KeyValues(column,
                        [
                            ("Gross property GGR", Money(model.NetImpact.GrossPropertyGgr)),
                            ("Transfer-effect GGR", Money(model.NetImpact.TransferEffectGgr)),
                            ("Imported + induced + tourism/traffic GGR", Money(model.NetImpact.CrossJurisdictionImportedGgr + model.NetImpact.InducedResidentGgr + model.NetImpact.TourismAndTrafficImportGgr)),
                            ("Local discretionary displacement", Money(model.NetImpact.LocalDiscretionaryDisplacement)),
                            ("Gross social cost", Money(model.NetImpact.GrossSocialCost)),
                            ("Net host-local impact", Money(model.NetImpact.NetHostLocalImpact)),
                            ("Net host-state impact", Money(model.NetImpact.NetHostStateImpact))
                        ]);
                        WaterfallChart(column, "Net host-local impact waterfall",
                        [
                            ("Cross-jurisdiction imported GGR", model.NetImpact.CrossJurisdictionImportedGgr),
                            ("Induced resident GGR", model.NetImpact.InducedResidentGgr),
                            ("Tourism and traffic imported GGR", model.NetImpact.TourismAndTrafficImportGgr),
                            ("Local discretionary displacement", -model.NetImpact.LocalDiscretionaryDisplacement),
                            ("Net host-local fiscal impact", model.NetImpact.NetHostLocalFiscalImpact),
                            ("Gross social cost", -model.NetImpact.GrossSocialCost)
                        ], model.NetImpact.NetHostLocalImpact);
                    }

                    if (model.Sensitivity is not null)
                    {
                        Heading(column, "One-at-a-time sensitivity");
                        column.Item().Text($"{model.Sensitivity.Name} · analysis {model.Sensitivity.SensitivityAnalysisId:D} · {model.Sensitivity.OutputMetric}. Every point is a complete stored model run.")
                            .FontSize(8);
                        TornadoChart(column, model.Sensitivity);
                        SimpleTable(column,
                            ["Parameter", "Low input / result", "Base input / result", "High input / result", "Range"],
                            model.Sensitivity.Rows.Select(row => new[]
                            {
                                row.ParameterKey,
                                $"{row.LowParameterValue:G7} / {SensitivityValue(row.LowMetricValue, model.Sensitivity.OutputUnits)}",
                                $"{row.BaseParameterValue:G7} / {SensitivityValue(row.BaseMetricValue, model.Sensitivity.OutputUnits)}",
                                $"{row.HighParameterValue:G7} / {SensitivityValue(row.HighMetricValue, model.Sensitivity.OutputUnits)}",
                                SensitivityValue(row.TotalRange, model.Sensitivity.OutputUnits)
                            }));
                    }

                    if (model.Benchmarks.Count > 0)
                    {
                        Heading(column, "Benchmark reconciliation");
                        SimpleTable(column,
                            ["Case", "Partition", "Observed", "Predicted", "Residual"],
                            model.Benchmarks.Select(row => new[]
                            {
                                row.CaseName, row.DatasetPartition, Money(row.ObservedRevenue),
                                Money(row.PredictedRevenue), Money(row.Residual)
                            }));
                    }

                    Heading(column, "Methodology and limitations");
                    column.Item().Text("All economic values in this report are read from the finalized stored model run; the renderer performs presentation aggregation only and does not execute a second model.");
                    foreach (var limitation in model.Limitations)
                    {
                        column.Item().PaddingLeft(8).Text("• " + limitation).FontSize(8);
                    }
                    if (model.Warnings.Count > 0)
                    {
                        column.Item().Text("Complete decision-use warning disclosure").SemiBold();
                        column.Item().Text(warningDigest.Summary).FontSize(8);
                        foreach (var warning in warningDigest.DecisionWarnings)
                        {
                            column.Item().PaddingLeft(8).Text("• " + warning).FontSize(8);
                        }
                    }

                    Heading(column, "Technical appendices");
                    Heading(column, "Parameter and override appendix");
                    SimpleTable(column,
                        ["Parameter", "Default", "Override", "Final", "Source / warning"],
                        model.Parameters.Select(row => new[]
                        {
                            row.DisplayName + " (" + row.Key + ")", row.DefaultValue.ToString("G7"),
                            row.UserOverrideValue?.ToString("G7") ?? "—", row.FinalValue.ToString("G7"),
                            row.SourceLayer + (string.IsNullOrWhiteSpace(row.WarningText) ? "" : " · " + row.WarningText)
                        }));

                    Heading(column, "Data and reproducibility appendix");
                    SimpleTable(column,
                        ["Role", "Dataset / period", "Source", "Transform", "Checksum"],
                        model.DataSources.Select(row => new[]
                        {
                            row.Role + ":" + row.ReferenceKey, row.DatasetKey + " / " + row.Period,
                            row.SourceName, row.TransformVersion, row.Checksum
                        }));
                    KeyValues(column,
                    [
                        ("Model version", model.Identity.ModelVersion),
                        ("Report template", model.Identity.TemplateVersion),
                        ("Run UUID", model.Identity.ModelRunId.ToString()),
                        ("Generated timestamp", model.Identity.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                        ("Run created / finalized", $"{model.Identity.RunCreatedAtUtc:O} / {model.Identity.RunFinalizedAtUtc:O}"),
                        ("Jurisdiction profile/version", $"{model.Identity.JurisdictionCode} / {model.Identity.JurisdictionProfileVersion}"),
                        ("Parameter-set version(s)", ReportDisclosure.ParameterSets(model)),
                        ("User overrides", ReportDisclosure.UserOverrides(model)),
                        ("Source data vintages", ReportDisclosure.SourceVintages(model)),
                        ("Route graph hash(es)", string.Join(", ", model.Identity.RoutingGraphHashes)),
                        ("Costing profile(s)", string.Join(", ", model.Identity.CostingProfiles)),
                        ("Candidate coordinates", $"{model.Scenario.CandidateLatitude:F6}, {model.Scenario.CandidateLongitude:F6}"),
                        ("Development program", $"{model.DevelopmentProgram.StableProgramId}@{model.DevelopmentProgram.Version}"),
                        ("Sensitivity analysis", model.Sensitivity?.SensitivityAnalysisId.ToString() ?? "Not included")
                    ]);
                });
            });
        });

        using var stream = new MemoryStream();
        document.GeneratePdf(stream);
        return stream.ToArray();
    }

    private static void Heading(ColumnDescriptor column, string title) =>
        column.Item().PaddingTop(8).BorderBottom(1.5f).BorderColor(Colors.Blue.Darken2)
            .PaddingBottom(3).Text(title).FontSize(15).Bold().FontColor(Colors.Blue.Darken3);

    private static void KeyValues(ColumnDescriptor column, IReadOnlyCollection<(string Label, string Value)> values) =>
        SimpleTable(column, ["Measure", "Value"], values.Select(value => new[] { value.Label, value.Value }));

    private static void BarChart(ColumnDescriptor column, string title, IEnumerable<(string Label, decimal Value)> rows)
    {
        var values = rows.ToArray();
        if (values.Length == 0)
        {
            return;
        }
        var maximum = values.Max(row => Math.Abs(row.Value));
        column.Item().Text(title).SemiBold().FontSize(8);
        foreach (var rowValue in values)
        {
            var ratio = maximum == 0 ? 0 : Convert.ToSingle(Math.Abs(rowValue.Value) / maximum);
            column.Item().Row(row =>
            {
                row.ConstantItem(135).Text(rowValue.Label).FontSize(7);
                row.ConstantItem(210).Height(8).Layers(layers =>
                {
                    layers.PrimaryLayer().Background(Colors.BlueGrey.Lighten4);
                    if (ratio > 0)
                    {
                        layers.Layer().Width(210 * ratio).Background(Colors.Blue.Darken1);
                    }
                });
                row.RelativeItem().AlignRight().Text(Money(rowValue.Value)).FontSize(7).SemiBold();
            });
        }
    }

    private static void CoordinateMap(
        ColumnDescriptor column,
        string title,
        CasinoImpactReportModel model,
        bool includeOrigins)
    {
        var svg = ReportExhibitBuilder.CoordinateMapSvg(model, includeOrigins);
        if (string.IsNullOrWhiteSpace(svg))
        {
            return;
        }
        column.Item().PaddingTop(4).Text(title).SemiBold().FontSize(8);
        column.Item().Height(235).Svg(svg);
        column.Item().Text(includeOrigins
                ? "WGS84 coordinates · proportional symbols show stored origin contribution · schematic equirectangular exhibit · no basemap."
                : "WGS84 coordinates · proposed site and incumbent facilities · schematic equirectangular exhibit · no basemap.")
            .FontSize(6).FontColor(Colors.Grey.Darken1);
    }

    private static void PatronOriginChoropleth(ColumnDescriptor column, CasinoImpactReportModel model)
    {
        var svg = ReportExhibitBuilder.PatronOriginChoroplethSvg(model);
        if (string.IsNullOrWhiteSpace(svg))
        {
            return;
        }
        column.Item().ShowEntire().Column(exhibit =>
        {
            exhibit.Item().PaddingTop(4).Text("Patron-origin contribution choropleth").SemiBold().FontSize(8);
            exhibit.Item().Height(190).Svg(svg);
            exhibit.Item().Text("Top-N source polygons · contribution-scaled color · geometry simplified 0.002° for presentation · WGS84 · no basemap.")
                .FontSize(6).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void CandidateTravelTimeMap(ColumnDescriptor column, CasinoImpactReportModel model)
    {
        var svg = ReportExhibitBuilder.CandidateTravelTimeMapSvg(model);
        if (string.IsNullOrWhiteSpace(svg))
        {
            return;
        }
        column.Item().ShowEntire().Column(exhibit =>
        {
            exhibit.Item().PaddingTop(4).Text("Origin-to-candidate routed travel-time map").SemiBold().FontSize(8);
            exhibit.Item().Height(190).Svg(svg);
            exhibit.Item().Text("Green ≤15/30 min · yellow ≤60 · orange ≤90 · red >90 · persisted Valhalla auto minutes at representative points · not interpolated isochrones · WGS84 · no basemap.")
                .FontSize(6).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void WaterfallChart(
        ColumnDescriptor column,
        string title,
        IEnumerable<(string Label, decimal Value)> rows,
        decimal storedTotal)
    {
        var values = rows.ToArray();
        if (values.Length == 0)
        {
            return;
        }
        var maximum = Math.Max(values.Select(row => Math.Abs(row.Value)).Append(Math.Abs(storedTotal)).Max(), 1m);
        var running = 0m;
        column.Item().PaddingTop(4).Text(title).SemiBold().FontSize(8);
        foreach (var value in values)
        {
            running += value.Value;
            var ratio = Convert.ToSingle(Math.Abs(value.Value) / maximum);
            column.Item().Row(row =>
            {
                row.ConstantItem(145).Text(value.Label).FontSize(7);
                row.ConstantItem(90).Height(8).Layers(layers =>
                {
                    layers.PrimaryLayer().Background(Colors.BlueGrey.Lighten5);
                    if (value.Value < 0 && ratio > 0)
                    {
                        layers.Layer().AlignRight().Width(90 * ratio).Background(Colors.Orange.Darken1);
                    }
                });
                row.ConstantItem(1).Height(8).Background(Colors.BlueGrey.Darken1);
                row.ConstantItem(90).Height(8).Layers(layers =>
                {
                    layers.PrimaryLayer().Background(Colors.BlueGrey.Lighten5);
                    if (value.Value >= 0 && ratio > 0)
                    {
                        layers.Layer().Width(90 * ratio).Background(Colors.Blue.Darken1);
                    }
                });
                row.RelativeItem().AlignRight().Text($"{Money(value.Value)} · running {Money(running)}").FontSize(6.5f);
            });
        }
        column.Item().BorderTop(0.75f).BorderColor(Colors.BlueGrey.Lighten2).PaddingTop(2).Row(row =>
        {
            row.RelativeItem().Text("Stored total").SemiBold().FontSize(7);
            row.AutoItem().Text(Money(storedTotal)).SemiBold().FontSize(7);
        });
    }

    private static void TornadoChart(ColumnDescriptor column, ReportSensitivityAnalysis sensitivity)
    {
        if (sensitivity.Rows.Count == 0)
        {
            return;
        }
        var maximumDelta = Math.Max(
            sensitivity.Rows.SelectMany(row => new[] { Math.Abs(row.LowDelta), Math.Abs(row.HighDelta) }).Max(),
            1m);
        column.Item().PaddingTop(4).Text($"Sensitivity tornado — {sensitivity.OutputMetric}").SemiBold().FontSize(8);
        foreach (var value in sensitivity.Rows.OrderByDescending(row => row.TotalRange))
        {
            var lowRatio = Convert.ToSingle(Math.Abs(value.LowDelta) / maximumDelta);
            var highRatio = Convert.ToSingle(Math.Abs(value.HighDelta) / maximumDelta);
            column.Item().Row(row =>
            {
                row.ConstantItem(145).Text(value.ParameterKey).FontSize(7);
                row.ConstantItem(90).Height(12).Layers(layers =>
                {
                    layers.PrimaryLayer().Background(Colors.BlueGrey.Lighten5);
                    if (value.LowDelta < 0 && lowRatio > 0)
                    {
                        layers.Layer().AlignRight().Width(90 * lowRatio).Height(5).Background(Colors.Orange.Darken1);
                    }
                    if (value.HighDelta < 0 && highRatio > 0)
                    {
                        layers.Layer().AlignBottom().AlignRight().Width(90 * highRatio).Height(5).Background(Colors.Blue.Darken1);
                    }
                });
                row.ConstantItem(1).Height(12).Background(Colors.BlueGrey.Darken1);
                row.ConstantItem(90).Height(12).Layers(layers =>
                {
                    layers.PrimaryLayer().Background(Colors.BlueGrey.Lighten5);
                    if (value.LowDelta >= 0 && lowRatio > 0)
                    {
                        layers.Layer().Width(90 * lowRatio).Height(5).Background(Colors.Orange.Darken1);
                    }
                    if (value.HighDelta >= 0 && highRatio > 0)
                    {
                        layers.Layer().AlignBottom().Width(90 * highRatio).Height(5).Background(Colors.Blue.Darken1);
                    }
                });
                row.RelativeItem().AlignRight().Text(
                        $"{SensitivityValue(value.LowMetricValue, sensitivity.OutputUnits)} – {SensitivityValue(value.HighMetricValue, sensitivity.OutputUnits)}")
                    .FontSize(6.5f);
            });
        }
        column.Item().PaddingTop(2).Text("Orange = low parameter setting; blue = high parameter setting. Bar side shows the signed output change from baseline.")
            .FontSize(6.5f).FontColor(Colors.BlueGrey.Darken2);
    }

    private static void SimpleTable(
        ColumnDescriptor column,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows)
    {
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in headers)
                {
                    columns.RelativeColumn();
                }
            });
            table.Header(header =>
            {
                foreach (var value in headers)
                {
                    header.Cell().Element(HeaderCell).Text(value).SemiBold().FontSize(7);
                }
            });
            foreach (var row in rows)
            {
                foreach (var value in row)
                {
                    table.Cell().Element(BodyCell).Text(value).FontSize(7);
                }
            }
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.BlueGrey.Lighten4).BorderBottom(1).BorderColor(Colors.BlueGrey.Lighten2).Padding(4);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);

    private static string Money(decimal value) => value.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
    private static string SensitivityValue(decimal value, string units) => units == "jobs"
        ? value.ToString("N1", CultureInfo.InvariantCulture)
        : Money(value);
}

public sealed class CsvReportRenderer : ICsvReportRenderer
{
    public string Render(CasinoImpactReportModel model)
    {
        var csv = new StringBuilder(32_000);
        Row(csv, "table", "group", "key", "value", "unit_or_note");
        Row(csv, "identity", "run", "model_run_id", model.Identity.ModelRunId, "uuid");
        Row(csv, "identity", "run", "model_version", model.Identity.ModelVersion, "");
        Row(csv, "identity", "run", "template_version", model.Identity.TemplateVersion, "");
        Row(csv, "scenario", "site", "candidate_latitude", model.Scenario.CandidateLatitude, "degrees");
        Row(csv, "scenario", "site", "candidate_longitude", model.Scenario.CandidateLongitude, "degrees");
        Row(csv, "revenue", "stabilized", "redistributed_resident_ggr", model.Revenue.RedistributedResidentGgr, "USD");
        Row(csv, "revenue", "stabilized", "induced_resident_ggr", model.Revenue.InducedResidentGgr, "USD");
        Row(csv, "revenue", "stabilized", "resident_ggr", model.Revenue.TotalResidentGgr, "USD");
        Row(csv, "revenue", "stabilized", "tourism_ggr", model.Revenue.TourismGgr, "USD");
        Row(csv, "revenue", "stabilized", "traffic_ggr", model.Revenue.TrafficGgr, "USD");
        Row(csv, "revenue", "stabilized", "total_ggr", model.Revenue.StabilizedTotalGgr, "USD");
        foreach (var county in model.OriginCounties)
        {
            Row(csv, "origin_county", county.GeographyCode, "origin_count", county.OriginCount, "origins");
            Row(csv, "origin_county", county.GeographyCode, "total_proposed_resident_ggr", county.TotalProposedResidentGgr, "USD");
            Row(csv, "origin_county", county.GeographyCode, "share_of_proposed_resident_ggr", county.ShareOfProposedResidentGgr, "share");
        }
        foreach (var origin in model.Origins)
        {
            Row(csv, "origin", origin.StableOriginId, "state_code", origin.StateCode, origin.OriginType);
            Row(csv, "origin", origin.StableOriginId, "latitude", origin.Latitude, "degrees");
            Row(csv, "origin", origin.StableOriginId, "longitude", origin.Longitude, "degrees");
            Row(csv, "origin", origin.StableOriginId, "candidate_route_found", origin.CandidateRouteFound, "boolean");
            Row(csv, "origin", origin.StableOriginId, "candidate_travel_time", origin.CandidateTravelTimeMinutes, "minutes");
            Row(csv, "origin", origin.StableOriginId, "candidate_routed_distance", origin.CandidateRoutedDistanceMeters, "meters");
            Row(csv, "origin", origin.StableOriginId, "presentation_area_geometry", origin.AreaGeometryWkt, "WKT EPSG:4326 simplified 0.002 degrees");
            Row(csv, "origin", origin.StableOriginId, "redistributed_resident_ggr", origin.RedistributedResidentGgr, "USD");
            Row(csv, "origin", origin.StableOriginId, "induced_resident_ggr", origin.InducedResidentGgr, "USD");
            Row(csv, "origin", origin.StableOriginId, "total_proposed_resident_ggr", origin.TotalProposedResidentGgr, "USD");
        }
        foreach (var facility in model.Facilities)
        {
            Row(csv, "facility", facility.FacilityKey, "facility_name", facility.FacilityName, facility.FacilityKind);
            Row(csv, "facility", facility.FacilityKey, "latitude", facility.Latitude, "degrees");
            Row(csv, "facility", facility.FacilityKey, "longitude", facility.Longitude, "degrees");
            Row(csv, "facility", facility.FacilityKey, "baseline_resident_ggr", facility.BaselineResidentGgr, "USD");
            Row(csv, "facility", facility.FacilityKey, "with_project_resident_ggr", facility.WithProjectResidentGgr, "USD");
            Row(csv, "facility", facility.FacilityKey, "change_in_resident_ggr", facility.ChangeInResidentGgr, "USD");
        }
        foreach (var sector in model.SectorDisplacement)
        {
            Row(csv, "displacement", sector.SectorKey, "normalized_weight", sector.NormalizedWeight, "share");
            Row(csv, "displacement", sector.SectorKey, "displaced_sales", sector.DisplacedSales, "USD");
            Row(csv, "displacement", sector.SectorKey, "displaced_jobs", sector.DisplacedJobs, "jobs");
        }
        foreach (var cost in model.SocialCosts)
        {
            Row(csv, "social_cost", cost.DomainKey, "incremental_cases", cost.IncrementalCases, "cases");
            Row(csv, "social_cost", cost.DomainKey, "annual_cost", cost.AnnualCost, "USD");
            Row(csv, "social_cost", cost.DomainKey, "low_annual_cost", cost.LowAnnualCost, "USD");
            Row(csv, "social_cost", cost.DomainKey, "high_annual_cost", cost.HighAnnualCost, "USD");
            Row(csv, "social_cost", cost.DomainKey, "included", cost.Included, "boolean");
        }
        if (model.NetImpact is not null)
        {
            Row(csv, "net_impact", "host", "cross_jurisdiction_imported_ggr", model.NetImpact.CrossJurisdictionImportedGgr, "USD");
            Row(csv, "net_impact", "host", "induced_resident_ggr", model.NetImpact.InducedResidentGgr, "USD");
            Row(csv, "net_impact", "host", "tourism_and_traffic_import_ggr", model.NetImpact.TourismAndTrafficImportGgr, "USD");
            Row(csv, "net_impact", "host", "local_discretionary_displacement", model.NetImpact.LocalDiscretionaryDisplacement, "USD");
            Row(csv, "net_impact", "host", "net_host_local_fiscal_impact", model.NetImpact.NetHostLocalFiscalImpact, "USD");
            Row(csv, "net_impact", "host", "net_host_local_impact", model.NetImpact.NetHostLocalImpact, "USD");
            Row(csv, "net_impact", "host", "net_host_state_impact", model.NetImpact.NetHostStateImpact, "USD");
            Row(csv, "net_impact", "host", "gross_social_cost", model.NetImpact.GrossSocialCost, "USD");
        }
        if (model.Sensitivity is not null)
        {
            Row(csv, "sensitivity", "analysis", "sensitivity_analysis_id", model.Sensitivity.SensitivityAnalysisId, "uuid");
            Row(csv, "sensitivity", "analysis", "baseline_model_run_id", model.Sensitivity.BaselineModelRunId, "uuid");
            Row(csv, "sensitivity", "analysis", "output_metric", model.Sensitivity.OutputMetric, model.Sensitivity.OutputUnits);
            Row(csv, "sensitivity", "analysis", "baseline_metric_value", model.Sensitivity.BaselineMetricValue, model.Sensitivity.OutputUnits);
            foreach (var sensitivity in model.Sensitivity.Rows)
            {
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_parameter_value", sensitivity.LowParameterValue, "parameter units");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "base_parameter_value", sensitivity.BaseParameterValue, "parameter units");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_parameter_value", sensitivity.HighParameterValue, "parameter units");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_model_run_id", sensitivity.LowModelRunId, "uuid");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_model_run_id", sensitivity.HighModelRunId, "uuid");
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_metric_value", sensitivity.LowMetricValue, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "base_metric_value", sensitivity.BaseMetricValue, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_metric_value", sensitivity.HighMetricValue, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "low_delta", sensitivity.LowDelta, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "high_delta", sensitivity.HighDelta, model.Sensitivity.OutputUnits);
                Row(csv, "sensitivity", sensitivity.ParameterKey, "total_range", sensitivity.TotalRange, model.Sensitivity.OutputUnits);
            }
        }
        foreach (var parameter in model.Parameters)
        {
            Row(csv, "parameter", parameter.Key, "default_value", parameter.DefaultValue, parameter.Units);
            Row(csv, "parameter", parameter.Key, "scenario_value", parameter.ScenarioValue, parameter.Units);
            Row(csv, "parameter", parameter.Key, "user_override_value", parameter.UserOverrideValue, parameter.Units);
            Row(csv, "parameter", parameter.Key, "final_value", parameter.FinalValue, parameter.Units);
        }
        foreach (var source in model.DataSources)
        {
            Row(csv, "data_source", source.Role + ":" + source.ReferenceKey, "dataset", source.DatasetKey, source.Period);
            Row(csv, "data_source", source.Role + ":" + source.ReferenceKey, "source_url", source.SourceUrl, source.Checksum);
        }
        return csv.ToString();
    }

    private static void Row(StringBuilder csv, params object?[] values)
    {
        csv.AppendJoin(',', values.Select(value => Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")));
        csv.Append('\n');
    }

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : '"' + value.Replace("\"", "\"\"") + '"';
}

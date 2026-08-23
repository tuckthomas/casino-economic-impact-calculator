using System.Text.Json;
using SaveNEIN.Client.Models;
using SkiaSharp;

namespace SaveNEIN.Server.Services;

public interface IFactCheckShareImageService
{
    bool TryGetImage(string slug, out byte[] image);
}

public sealed class FactCheckShareImageService : IFactCheckShareImageService
{
    private const int Width = 1200;
    private const int Height = 630;
    private readonly IReadOnlyDictionary<string, FactCheckClaim> _claims;

    public FactCheckShareImageService()
    {
        using var stream = typeof(SaveNEIN.Client.App).Assembly
            .GetManifestResourceStream("SaveNEIN.Client.Data.fact-checks.json")
            ?? throw new InvalidOperationException("Embedded fact-check content could not be loaded.");

        var document = JsonSerializer.Deserialize<FactCheckDocument>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Embedded fact-check content is invalid.");

        _claims = document.FactChecks.ToDictionary(claim => claim.Slug, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetImage(string slug, out byte[] image)
    {
        if (!_claims.TryGetValue(slug, out var claim))
        {
            image = Array.Empty<byte>();
            return false;
        }

        // Share graphics are small and must always reflect the current claim
        // content. Rendering on request avoids serving a stale image after a
        // development hot reload or a content update.
        image = Render(claim);
        return true;
    }

    private static byte[] Render(FactCheckClaim claim)
    {
        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var cardPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var borderPaint = new SKPaint { Color = SKColor.Parse("#CBD5E1"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var redPaint = new SKPaint { Color = SKColor.Parse("#C91D25"), IsAntialias = true };
        using var navyPaint = new SKPaint { Color = SKColor.Parse("#0B1221"), IsAntialias = true };
        using var mutedPaint = new SKPaint { Color = SKColor.Parse("#64748B"), IsAntialias = true };

        var card = new SKRoundRect(new SKRect(1, 1, Width - 1, Height - 1), 32, 32);
        canvas.DrawRoundRect(card, cardPaint);
        canvas.Save();
        canvas.ClipRoundRect(card, SKClipOperation.Intersect, true);
        // The accent follows the card's round corners instead of ending as a flat stripe.
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(-12, 0, 22, Height), 32, 32), redPaint);
        canvas.Restore();
        canvas.DrawRoundRect(card, borderPaint);

        using var sourceFont = Font(20, SKFontStyleWeight.SemiBold);
        using var panelLabelFont = Font(19, SKFontStyleWeight.ExtraBold);
        using var verdictLabelFont = Font(19, SKFontStyleWeight.ExtraBold);
        using var verdictFont = Font(34, SKFontStyleWeight.ExtraBold);
        using var evidenceLabelFont = Font(19, SKFontStyleWeight.ExtraBold);
        using var ctaFont = Font(22, SKFontStyleWeight.SemiBold, SKFontStyleSlant.Italic);

        using var logo = LoadLogo();
        canvas.DrawText($"CLAIM SOURCE: {claim.Claimant.ToUpperInvariant()}", 58, 73, sourceFont, navyPaint);
        canvas.DrawBitmap(logo, new SKRect(972, 24, 1142, 101));

        const float textRight = 795;
        var factTextSize = FindFactTextSize(claim, textRight);
        using var claimFont = Font(factTextSize, SKFontStyleWeight.Bold);
        using var findingFont = Font(factTextSize, SKFontStyleWeight.Bold);
        using var panelPaint = new SKPaint { Color = SKColor.Parse("#F1F5F9"), IsAntialias = true };
        using var panelBorderPaint = new SKPaint { Color = SKColor.Parse("#E2E8F0"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        var claimLines = Wrap(claim.ClaimText, claimFont, textRight - 164);
        const float claimTextBaseline = 172;
        var claimLineHeight = factTextSize * 1.11f;
        var claimPanelBottom = claimTextBaseline + ((claimLines.Count - 1) * claimLineHeight) + 26;
        var claimPanel = new SKRoundRect(new SKRect(58, 100, textRight, claimPanelBottom), 18, 18);
        canvas.DrawRoundRect(claimPanel, panelPaint);
        canvas.DrawRoundRect(claimPanel, panelBorderPaint);
        canvas.DrawText("CLAIM:", 82, 133, panelLabelFont, redPaint);
        DrawLines(canvas, claimLines, 82, claimTextBaseline, claimFont, navyPaint, claimLineHeight);

        var verdictLabelY = claimPanelBottom + 46;
        var verdictX = DrawRatingLabel(canvas, 58, verdictLabelY, verdictLabelFont, redPaint, navyPaint);
        var verdictText = VerdictLabel(claim.Verdict);
        canvas.DrawText(verdictText, verdictX, verdictLabelY, verdictFont, redPaint);
        using var verdictUnderline = new SKPaint { Color = SKColor.Parse("#C91D25"), IsAntialias = true, StrokeWidth = 2 };
        canvas.DrawLine(verdictX, verdictLabelY + 7, verdictX + verdictFont.MeasureText(verdictText), verdictLabelY + 7, verdictUnderline);

        var findingLines = Wrap(claim.FindingHeadline, findingFont, textRight - 106);
        var evidencePanelTop = verdictLabelY + 28;
        const float evidenceTextOffset = 74;
        var evidenceLineHeight = factTextSize * 1.11f;
        var evidenceTextBaseline = evidencePanelTop + evidenceTextOffset;
        var evidencePanelBottom = evidenceTextBaseline + ((findingLines.Count - 1) * evidenceLineHeight) + 24;
        var evidencePanel = new SKRoundRect(new SKRect(58, evidencePanelTop, textRight, evidencePanelBottom), 18, 18);
        canvas.DrawRoundRect(evidencePanel, panelPaint);
        canvas.DrawRoundRect(evidencePanel, panelBorderPaint);
        canvas.DrawText("EVIDENCE SUMMARY:", 82, evidencePanelTop + 34, evidenceLabelFont, redPaint);
        DrawLines(canvas, findingLines, 82, evidenceTextBaseline, findingFont, navyPaint, evidenceLineHeight);

        using var divider = new SKPaint { Color = SKColor.Parse("#CBD5E1"), IsAntialias = true, StrokeWidth = 2 };
        var dividerY = Height - 66;
        canvas.DrawLine(58, dividerY, textRight, dividerY, divider);
        canvas.DrawText("Vote NO this November. Learn the facts at SaveNEIN.com", 58, dividerY + 36, ctaFont, navyPaint);

        DrawGauge(canvas, new SKPoint(1002, 333), 130, claim.Verdict);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawGauge(
        SKCanvas canvas,
        SKPoint center,
        float radius,
        FactCheckVerdict verdict)
    {
        using var dial = LoadGaugeFace();
        canvas.DrawBitmap(
            dial,
            new SKRect(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius));

        var needleAngle = NeedleAngle(verdict);
        var needleRadians = needleAngle * MathF.PI / 180;
        var direction = new SKPoint(MathF.Cos(needleRadians), MathF.Sin(needleRadians));
        var perpendicular = new SKPoint(-direction.Y, direction.X);
        var needleEnd = new SKPoint(
            center.X + direction.X * radius * 0.62f,
            center.Y + direction.Y * radius * 0.62f);
        var halfWidth = radius * 0.035f;
        using var needlePath = new SKPath();
        needlePath.MoveTo(center.X + perpendicular.X * halfWidth, center.Y + perpendicular.Y * halfWidth);
        needlePath.LineTo(needleEnd.X - direction.X * radius * 0.055f + perpendicular.X * halfWidth * 0.7f, needleEnd.Y - direction.Y * radius * 0.055f + perpendicular.Y * halfWidth * 0.7f);
        needlePath.LineTo(needleEnd);
        needlePath.LineTo(needleEnd.X - direction.X * radius * 0.055f - perpendicular.X * halfWidth * 0.7f, needleEnd.Y - direction.Y * radius * 0.055f - perpendicular.Y * halfWidth * 0.7f);
        needlePath.LineTo(center.X - perpendicular.X * halfWidth, center.Y - perpendicular.Y * halfWidth);
        needlePath.Close();
        using var needleShader = SKShader.CreateLinearGradient(
            new SKPoint(center.X, center.Y - halfWidth),
            new SKPoint(center.X, center.Y + halfWidth),
            [SKColor.Parse("#334C70"), SKColor.Parse("#14294A"), SKColor.Parse("#020817")],
            null,
            SKShaderTileMode.Clamp);
        using var needlePaint = new SKPaint { IsAntialias = true, Shader = needleShader };
        canvas.DrawPath(needlePath, needlePaint);

        using var hubShader = SKShader.CreateRadialGradient(
            new SKPoint(center.X - 4, center.Y - 5),
            radius * 0.16f,
            [SKColors.White, SKColor.Parse("#DBE4EF"), SKColor.Parse("#94A3B8"), SKColor.Parse("#334155"), SKColor.Parse("#020617")],
            [0f, 0.12f, 0.3f, 0.56f, 1f],
            SKShaderTileMode.Clamp);
        using var hub = new SKPaint { IsAntialias = true, Shader = hubShader };
        using var hubBorder = new SKPaint { Color = SKColor.Parse("#DBE4EF"), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        canvas.DrawCircle(center, radius * 0.095f, hub);
        canvas.DrawCircle(center, radius * 0.095f, hubBorder);

        using var labelFont = Font(18, SKFontStyleWeight.ExtraBold);
        using var labelPaint = new SKPaint
        {
            Color = verdict is FactCheckVerdict.False or FactCheckVerdict.MostlyFalse
                ? SKColor.Parse("#EF4444")
                : SKColors.White,
            IsAntialias = true
        };
        canvas.DrawText(VerdictLabel(verdict), center.X, center.Y + radius * 0.61f, SKTextAlign.Center, labelFont, labelPaint);
    }

    private static float NeedleAngle(FactCheckVerdict verdict) => verdict switch
    {
        FactCheckVerdict.True => -20f,
        FactCheckVerdict.MostlyTrue => -55f,
        FactCheckVerdict.MostlyFalse => -130f,
        FactCheckVerdict.False => -170f,
        _ => -90f
    };

    private static SKBitmap LoadGaugeFace()
    {
        using var stream = typeof(FactCheckShareImageService).Assembly
            .GetManifestResourceStream("SaveNEIN.Server.Assets.fact-check-gauge-face.png")
            ?? throw new InvalidOperationException("Fact-check gauge face asset could not be loaded.");

        return SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException("Fact-check gauge face asset is invalid.");
    }

    private static SKBitmap LoadLogo()
    {
        using var stream = typeof(FactCheckShareImageService).Assembly
            .GetManifestResourceStream("SaveNEIN.Server.Assets.SAVENEIN.png")
            ?? throw new InvalidOperationException("SaveNEIN logo asset could not be loaded.");

        return SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException("SaveNEIN logo asset is invalid.");
    }

    private static float DrawRatingLabel(SKCanvas canvas, float x, float baseline, SKFont font, SKPaint redPaint, SKPaint navyPaint)
    {
        canvas.DrawText("SAVE", x, baseline, font, redPaint);
        var offset = font.MeasureText("SAVE");
        canvas.DrawText("NEIN", x + offset, baseline, font, navyPaint);
        offset += font.MeasureText("NEIN");
        canvas.DrawText(" RATES THIS:", x + offset, baseline, font, redPaint);
        return x + offset + font.MeasureText(" RATES THIS:") + 12;
    }

    private static SKFont Font(float size, SKFontStyleWeight weight, SKFontStyleSlant slant = SKFontStyleSlant.Upright) =>
        new(SKTypeface.FromFamilyName("Arial", weight, SKFontStyleWidth.Normal, slant), size);

    private static float FindFactTextSize(FactCheckClaim claim, float textRight)
    {
        for (var size = 28f; size >= 18f; size -= 1f)
        {
            using var font = Font(size, SKFontStyleWeight.Bold);
            var claimLines = Wrap(claim.ClaimText, font, textRight - 164);
            var findingLines = Wrap(claim.FindingHeadline, font, textRight - 106);
            var lineHeight = size * 1.11f;
            var claimPanelBottom = 172 + ((claimLines.Count - 1) * lineHeight) + 26;
            var verdictLabelY = claimPanelBottom + 46;
            var evidencePanelTop = verdictLabelY + 28;
            var evidencePanelBottom = evidencePanelTop + 74 + ((findingLines.Count - 1) * lineHeight) + 24;
            if (evidencePanelBottom <= Height - 86)
            {
                return size;
            }
        }

        return 18f;
    }

    private static IReadOnlyList<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in words)
        {
            var candidate = string.IsNullOrEmpty(current) ? word : $"{current} {word}";
            if (font.MeasureText(candidate) <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (!string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }

        return lines;
    }

    private static void DrawLines(SKCanvas canvas, IReadOnlyList<string> lines, float x, float firstBaseline, SKFont font, SKPaint paint, float lineHeight)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            canvas.DrawText(lines[index], x, firstBaseline + index * lineHeight, font, paint);
        }
    }

    private static string VerdictLabel(FactCheckVerdict verdict) => verdict switch
    {
        FactCheckVerdict.True => "TRUE",
        FactCheckVerdict.MostlyTrue => "MOSTLY TRUE",
        FactCheckVerdict.MostlyFalse => "MOSTLY FALSE",
        FactCheckVerdict.False => "FALSE",
        _ => "UNRATED"
    };
}

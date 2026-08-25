using System.Net;
using System.Text.RegularExpressions;

namespace SaveNEIN.Server.Services;

internal static partial class ArchiveHtmlRewriter
{
    [GeneratedRegex("(?is)(?<prefix><a\\b[^>]*?\\bhref\\s*=\\s*)(?<quote>[\\\"'])(?<url>.*?)(?:\\k<quote>)")]
    private static partial Regex AnchorHrefRegex();

    [GeneratedRegex("(?is)\\s+target\\s*=\\s*([\\\"']).*?\\1")]
    private static partial Regex TargetAttributeRegex();

    public static string Rewrite(string html, Uri pageUrl, Guid captureId)
    {
        var rewritten = AnchorHrefRegex().Replace(html, match =>
        {
            var rawHref = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
            if (rawHref.Length == 0 || rawHref.StartsWith('#')) return match.Value;
            if (!Uri.TryCreate(pageUrl, rawHref, out var resolved) ||
                (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps))
            {
                return match.Value;
            }

            var fragment = resolved.Fragment;
            var builder = new UriBuilder(resolved) { Fragment = string.Empty };
            var archivedHref = $"/api/web-archives/captures/{captureId}/singlefile?url={Uri.EscapeDataString(builder.Uri.AbsoluteUri)}{fragment}";
            var quote = match.Groups["quote"].Value;
            return $"{match.Groups["prefix"].Value}{quote}{WebUtility.HtmlEncode(archivedHref)}{quote}";
        });

        return TargetAttributeRegex().Replace(rewritten, string.Empty);
    }

    public static string MissingLinkedPage(string targetUrl) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Archived page unavailable</title>
          <style>
            body { max-width: 48rem; margin: 4rem auto; padding: 0 1.5rem; font: 18px/1.6 system-ui, sans-serif; color: #172033; }
            h1 { color: #c81e2a; }
            code { overflow-wrap: anywhere; }
          </style>
        </head>
        <body>
          <h1>Linked page not present in this archive</h1>
          <p>This link was intentionally prevented from opening the mutable live website.</p>
          <p>Requested archived URL: <code>{{WebUtility.HtmlEncode(targetUrl)}}</code></p>
        </body>
        </html>
        """;
}

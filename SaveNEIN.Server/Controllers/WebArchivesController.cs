using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/web-archives")]
public sealed class WebArchivesController(
    IArchiveBoxCaptureService archives,
    IOptions<ArchiveBoxOptions> options,
    IHttpClientFactory httpClientFactory,
    IHostEnvironment hostEnvironment,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("{sourceKey}/latest")]
    public async Task<ActionResult<WebArchiveMetadata>> GetLatest(string sourceKey, CancellationToken cancellationToken)
    {
        var capture = await archives.GetLatestAsync(sourceKey, cancellationToken)
            ?? await GetPublishedCaptureForDevelopmentAsync(sourceKey, cancellationToken);
        return capture is null ? NotFound() : Ok(capture);
    }

    [HttpPost("capture/{sourceKey}")]
    public async Task<ActionResult<WebArchiveMetadata>> Capture(string sourceKey, CancellationToken cancellationToken)
    {
        if (!HasValidAdminToken()) return NotFound();
        try { return Ok(await archives.CaptureAsync(sourceKey, cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException exception) { return Problem(exception.Message, statusCode: StatusCodes.Status422UnprocessableEntity); }
    }

    [HttpGet("captures/{id:guid}/singlefile")]
    public async Task<IActionResult> GetSingleFile(Guid id, CancellationToken cancellationToken)
    {
        var result = await archives.GetSingleFileAsync(id, cancellationToken);
        if (result is null) return NotFound();
        var contentType = Path.GetExtension(result.Value.Path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "text/html; charset=utf-8";
        if (contentType.StartsWith("text/html", StringComparison.Ordinal))
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(result.Value.Path, contentType, enableRangeProcessing: true);
    }

    private bool HasValidAdminToken()
    {
        var expected = options.Value.CaptureAdminToken;
        var supplied = Request.Headers["X-Archive-Capture-Token"].ToString();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(supplied)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
    }

    private async Task<WebArchiveMetadata?> GetPublishedCaptureForDevelopmentAsync(string sourceKey, CancellationToken cancellationToken)
    {
        if (!hostEnvironment.IsDevelopment() ||
            !Uri.TryCreate(configuration["ArchiveRegistry:PublishedBaseUrl"], UriKind.Absolute, out var registryBaseUrl) ||
            registryBaseUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(registryBaseUrl.Host, "savenein.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var requestUrl = new Uri(registryBaseUrl, $"api/web-archives/{Uri.EscapeDataString(sourceKey)}/latest");
            using var response = await httpClientFactory.CreateClient().GetAsync(requestUrl, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<WebArchiveMetadata>(cancellationToken: cancellationToken)
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }
}

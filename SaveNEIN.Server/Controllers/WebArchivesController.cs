using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SaveNEIN.Server.Configuration;
using SaveNEIN.Server.Services;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/web-archives")]
public sealed class WebArchivesController(IArchiveBoxCaptureService archives, IOptions<ArchiveBoxOptions> options) : ControllerBase
{
    [HttpGet("{sourceKey}/latest")]
    public async Task<ActionResult<WebArchiveMetadata>> GetLatest(string sourceKey, CancellationToken cancellationToken)
    {
        var capture = await archives.GetLatestAsync(sourceKey, cancellationToken);
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
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'; font-src data:";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        return PhysicalFile(result.Value.Path, "text/html; charset=utf-8", enableRangeProcessing: true);
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
}

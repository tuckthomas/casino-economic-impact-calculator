using Microsoft.AspNetCore.Mvc;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/client-log")]
public class ClientLogController : ControllerBase
{
    private readonly ILogger<ClientLogController> _logger;

    public ClientLogController(ILogger<ClientLogController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Log([FromBody] LogEntry entry)
    {
        _logger.LogInformation("[CLIENT-LOG] Client message received ({MessageLength} characters)", entry.Message.Length);
        return Ok();
    }

    public class LogEntry
    {
        public string Message { get; set; } = string.Empty;
    }
}

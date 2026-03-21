using Microsoft.AspNetCore.Mvc;

namespace SaveFW.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TileProxyController : ControllerBase
    {
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// Proxies ArcGIS satellite imagery tiles through the app server
        /// so they are same-origin and don't taint the WebGL canvas.
        /// This enables canvas.toDataURL() for PDF map export.
        /// </summary>
        [HttpGet("satellite/{z}/{y}/{x}")]
        [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)] // Cache tiles for 24h
        public async Task<IActionResult> GetSatelliteTile(int z, int y, int x)
        {
            var url = $"https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}";
            
            try
            {
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode);

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return File(bytes, contentType);
            }
            catch (Exception)
            {
                return StatusCode(502);
            }
        }
    }
}

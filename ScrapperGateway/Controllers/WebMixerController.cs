using Microsoft.AspNetCore.Mvc;
using ServicesLayer;
using DataLayer.Models;
using DataLayer.Models.DTOs;

namespace ScrapperGateway.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebMixerController : ControllerBase
    {
        private readonly IWebMixerService _webMixerService;
        private readonly ILogger<WebMixerController> _logger;

        public WebMixerController(IWebMixerService webMixerService, ILogger<WebMixerController> logger)
        {
            _webMixerService = webMixerService ?? throw new ArgumentNullException(nameof(webMixerService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost("Search")]
        public async Task<IActionResult> Search([FromBody] SearchRequestDto request)
        {
            try
            {
                _logger.LogInformation("Received search request with keywords: {Keywords}", request.Keywords);

                var results = await _webMixerService.Search(request);

                if (results == null || !results.Any())
                {
                    return NotFound("No se encontraron resultados.");
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing search request");
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
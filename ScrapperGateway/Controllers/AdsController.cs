using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DataLayer.Models;
using DataLayer.Models.PostGresModels;
using Microsoft.AspNetCore.Authorization;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class AdsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<AdsController> _logger;

        public AdsController(AppDbContext context, ILogger<AdsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetAd(string id)
        {
            try
            {
                var ad = await _context.Ads.FirstOrDefaultAsync(a => a.Id == id);

                if (ad == null)
                {
                    return NotFound(new { message = "Ad not found" });
                }

                return Ok(ad);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving ad with ID: {id}");
                return StatusCode(500, new { message = "An error occurred while retrieving the ad" });
            }
        }
    }
}
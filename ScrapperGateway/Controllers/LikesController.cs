using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DataLayer.Models;
using DataLayer.Models.PostGresModels;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class LikesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<LikesController> _logger;

        public LikesController(AppDbContext context, ILogger<LikesController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpPost("{adId}")]
        public async Task<IActionResult> ToggleLike([FromRoute] string adId)
        {
            try
            {
                _logger.LogInformation($"Attempting to toggle like for ad {adId}");

                if (string.IsNullOrEmpty(adId))
                {
                    _logger.LogWarning("AdId is null or empty");
                    return BadRequest(new { message = "Invalid ad ID" });
                }

                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    _logger.LogWarning("Invalid user identification from token");
                    return Unauthorized(new { message = "Invalid user identification" });
                }

                // Check if ad exists
                var ad = await _context.Ads.FirstOrDefaultAsync(a => a.Id == adId);
                if (ad == null)
                {
                    _logger.LogWarning($"Ad not found with ID: {adId}");
                    return NotFound(new { message = "Ad not found" });
                }

                // Check if like exists
                var existingLike = await _context.Likes
                    .FirstOrDefaultAsync(l => l.UserId == userId && l.AdId == adId);

                if (existingLike != null)
                {
                    _context.Likes.Remove(existingLike);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Like removed for ad {adId} by user {userId}");
                    return Ok(new { liked = false });
                }

                // Create new like
                var like = new Like
                {
                    UserId = userId,
                    AdId = adId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Likes.Add(like);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Like added for ad {adId} by user {userId}");
                return Ok(new { liked = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error toggling like for ad {adId}");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("check/{adId}")]
        public async Task<IActionResult> CheckLike([FromRoute] string adId)
        {
            try
            {
                if (string.IsNullOrEmpty(adId))
                {
                    return BadRequest(new { message = "Invalid ad ID" });
                }

                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { message = "Invalid user identification" });
                }

                var exists = await _context.Likes
                    .AnyAsync(l => l.UserId == userId && l.AdId == adId);

                return Ok(new { liked = exists });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking like status for ad {adId}");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        [HttpGet("user")]
        public async Task<IActionResult> GetUserLikes()
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { message = "Invalid user identification" });
                }

                var likedAds = await _context.Likes
                    .Where(l => l.UserId == userId)
                    .Include(l => l.Ad)
                    .Select(l => l.Ad)
                    .ToListAsync();

                return Ok(likedAds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user likes");
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
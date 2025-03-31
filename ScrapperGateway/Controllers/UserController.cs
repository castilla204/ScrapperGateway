using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using DataLayer.Models.PostGresModels;
using DataLayer.Models;
using Microsoft.EntityFrameworkCore;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserController> _logger;

    public UserController(
        IConfiguration configuration,
        AppDbContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<UserController> logger)
    {
        _configuration = configuration;
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost("google-auth")]
    public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthDto request)
    {
        try
        {
            _logger.LogInformation("Starting Google authentication for email: {Email}", request.Email);

            if (string.IsNullOrEmpty(request.AccessToken))
            {
                return BadRequest(new { message = "Access token is required" });
            }

            // Verify the token with Google
            var httpClient = _httpClientFactory.CreateClient();
            var userInfoResponse = await httpClient.GetAsync(
                $"https://www.googleapis.com/oauth2/v3/userinfo?access_token={request.AccessToken}"
            );

            if (!userInfoResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to verify Google token");
                return BadRequest(new { message = "Invalid Google token" });
            }

            // Find or create user
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.GoogleId == request.GoogleId);

            if (user == null)
            {
                _logger.LogInformation("Creating new user for Google ID: {GoogleId}", request.GoogleId);

                user = new User
                {
                    Name = request.Name?.Trim(),
                    Email = request.Email?.Trim(),
                    GoogleId = request.GoogleId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }

            // Generate JWT token
            var token = GenerateJwtToken(user);

            _logger.LogInformation("Authentication successful for user ID: {UserId}", user.Id);

            return Ok(new
            {
                token,
                user = new
                {
                    user.Id,
                    user.Name,
                    user.Email
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Google authentication");
            return StatusCode(500, new { message = "An error occurred during authentication" });
        }
    }

    private string GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Name)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ??
            throw new InvalidOperationException("JWT Key not found in configuration")));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddDays(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public class GoogleAuthDto
{
    public string AccessToken { get; set; }
    public string Email { get; set; }
    public string Name { get; set; }
    public string GoogleId { get; set; }
}
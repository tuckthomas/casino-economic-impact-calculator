using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaveNEIN.Server.Data;
using SaveNEIN.Server.Data.Entities;
using SaveNEIN.Shared;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace SaveNEIN.Server.Controllers;

[ApiController]
[Route("api/coalition-signups")]
public class CoalitionSignupsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CoalitionSignupsController> _logger;

    public CoalitionSignupsController(AppDbContext context, IMemoryCache cache, ILogger<CoalitionSignupsController> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<CoalitionSignupResponse>> Post([FromBody] CoalitionSignupRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Extremely basic IP-based rate limiting (prevent spamming)
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown_ip";
        var cacheKey = $"signup_limit_{ip}";
        if (_cache.TryGetValue(cacheKey, out int count))
        {
            if (count > 10) // Max 10 signups per IP per hour
            {
                _logger.LogWarning("Rate limit exceeded for IP: {Ip}", ip);
                return StatusCode(429, new { Message = "Too many requests. Please try again later." });
            }
            _cache.Set(cacheKey, count + 1, TimeSpan.FromHours(1));
        }
        else
        {
            _cache.Set(cacheKey, 1, TimeSpan.FromHours(1));
        }

        var normalizedEmail = NormalizeEmail(request.Email);
        if (string.IsNullOrEmpty(normalizedEmail))
        {
            return BadRequest(new { Message = "Invalid email format." });
        }

        try
        {
            var existingSignup = await _context.CoalitionSignups
                .FirstOrDefaultAsync(c => c.NormalizedEmail == normalizedEmail);

            if (existingSignup != null)
            {
                // Update existing record
                existingSignup.FirstName = request.FirstName.Trim();
                existingSignup.LastName = request.LastName.Trim();
                existingSignup.Email = request.Email.Trim();
                existingSignup.AddressLine1 = request.AddressLine1.Trim();
                existingSignup.AddressLine2 = request.AddressLine2?.Trim();
                existingSignup.City = request.City.Trim();
                existingSignup.StateProvince = request.StateProvince.Trim();
                existingSignup.PostalCode = request.PostalCode.Trim();

                existingSignup.DisplayYardSign = request.DisplayYardSign;
                existingSignup.WorkEventBooth = request.WorkEventBooth;
                existingSignup.GoDoorToDoor = request.GoDoorToDoor;
                existingSignup.WriteLetterToEditor = request.WriteLetterToEditor;
                existingSignup.ShareSocialMedia = request.ShareSocialMedia;
                existingSignup.WorkPollingSiteElectionDay = request.WorkPollingSiteElectionDay;
                existingSignup.MakePhoneCalls = request.MakePhoneCalls;
                existingSignup.BeListedAsSupporter = request.BeListedAsSupporter;

                existingSignup.UpdatedAtUtc = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Updated coalition signup record ID {SignupId}", existingSignup.Id);

                return Ok(new CoalitionSignupResponse 
                { 
                    Success = true, 
                    Message = "Thanks for getting involved. Your information has been updated." 
                });
            }
            else
            {
                // Create new record
                var newSignup = new CoalitionSignup
                {
                    FirstName = request.FirstName.Trim(),
                    LastName = request.LastName.Trim(),
                    Email = request.Email.Trim(),
                    NormalizedEmail = normalizedEmail,
                    AddressLine1 = request.AddressLine1.Trim(),
                    AddressLine2 = request.AddressLine2?.Trim(),
                    City = request.City.Trim(),
                    StateProvince = request.StateProvince.Trim(),
                    PostalCode = request.PostalCode.Trim(),

                    DisplayYardSign = request.DisplayYardSign,
                    WorkEventBooth = request.WorkEventBooth,
                    GoDoorToDoor = request.GoDoorToDoor,
                    WriteLetterToEditor = request.WriteLetterToEditor,
                    ShareSocialMedia = request.ShareSocialMedia,
                    WorkPollingSiteElectionDay = request.WorkPollingSiteElectionDay,
                    MakePhoneCalls = request.MakePhoneCalls,
                    BeListedAsSupporter = request.BeListedAsSupporter,

                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                _context.CoalitionSignups.Add(newSignup);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created new coalition signup record ID {SignupId}", newSignup.Id);

                return Created("", new CoalitionSignupResponse 
                { 
                    Success = true, 
                    Message = "Thanks for getting involved. Your information has been received." 
                });
            }
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error saving coalition signup.");
            return StatusCode(500, new { Message = "An error occurred while processing your request. Please try again." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing coalition signup.");
            return StatusCode(500, new { Message = "An unexpected error occurred. Please try again." });
        }
    }

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;
        var trimmed = email.Trim().ToUpperInvariant();
        return trimmed;
    }
}

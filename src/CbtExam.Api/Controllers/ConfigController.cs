using CbtExam.Data;
using CbtExam.Shared.DTOs;
using CbtExam.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CbtExam.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigController(AppDbContext db) : ControllerBase
{
    [HttpGet("branding")]
    public async Task<IActionResult> GetBranding()
    {
        try
        {
            var config = await db.AdminConfigs.FirstOrDefaultAsync();
            if (config == null)
            {
                return Ok(new { systemName = (string?)null, schoolLogo = (string?)null });
            }

            return Ok(new { systemName = config.SystemName, schoolLogo = config.SchoolLogo });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching branding: {ex.Message}");
            return Ok(new { systemName = (string?)null, schoolLogo = (string?)null });
        }
    }

    [HttpPut("branding")]
    public async Task<IActionResult> UpdateBranding([FromBody] BrandingUpdateDto dto)
    {
        try
        {
            var config = await db.AdminConfigs.FirstOrDefaultAsync();
            if (config == null)
            {
                // Create new config if it doesn't exist
                config = new AdminConfig
                {
                    AccessCode = "ADMIN",
                    Username = "admin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.AdminConfigs.Add(config);
            }

            config.SystemName = dto.SystemName;
            config.SchoolLogo = dto.SchoolLogo;

            await db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating branding: {ex.Message}");
            return BadRequest(new { success = false, error = ex.Message });
        }
    }
}

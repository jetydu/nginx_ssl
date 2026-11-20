using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Dtos;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/logs")]
[Authorize]
public class LogsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        [FromQuery] string? level = null,
        [FromQuery] string? category = null,
        [FromQuery] int? certificateId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = db.ActivityLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(level) && level != "all")
        {
            query = query.Where(x => x.Level == level);
        }

        if (!string.IsNullOrWhiteSpace(category) && category != "all")
        {
            query = query.Where(x => x.Category == category);
        }

        if (certificateId is int id)
        {
            query = query.Where(x => x.CertificateId == id);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Message.Contains(term) || (x.Domain != null && x.Domain.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ActivityLogDto
            {
                Id = x.Id,
                Timestamp = x.Timestamp,
                Level = x.Level,
                Category = x.Category,
                CertificateId = x.CertificateId,
                Domain = x.Domain,
                Message = x.Message,
                Details = x.Details
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }

    [HttpDelete]
    public async Task<ActionResult> Clear(CancellationToken ct)
    {
        await db.ActivityLogs.ExecuteDeleteAsync(ct);
        return NoContent();
    }
}

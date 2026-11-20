using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Dtos;
using NginxSslManager.Api.Services;
using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/certificates")]
[Authorize]
public class CertificatesController(
    AppDbContext db,
    CertificateManager manager,
    NginxConfigService nginx,
    ILogger<CertificatesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CertificateDto>>> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] bool? autoRenew,
        CancellationToken ct)
    {
        var query = db.Certificates.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = status switch
            {
                "soon" => query.Where(x => x.Status == "expiring" || x.Status == "critical"),
                "healthy" => query.Where(x => x.Status == "valid"),
                _ => query.Where(x => x.Status == status)
            };
        }

        if (autoRenew is bool auto)
        {
            query = query.Where(x => x.AutoRenew == auto);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Domains.Contains(term) || x.PrimaryDomain.Contains(term));
        }

        var records = await query
            .OrderBy(x => x.NotAfter ?? DateTime.MaxValue)
            .ThenBy(x => x.PrimaryDomain)
            .ToListAsync(ct);

        Dictionary<string, int> counts;
        try
        {
            counts = (await nginx.ScanAsync(ct))
                .Where(x => !string.IsNullOrWhiteSpace(x.CertificatePath))
                .GroupBy(x => x.CertificatePath!, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Nginx scan failed while listing certificates");
            counts = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        return Ok(records.Select(record =>
        {
            var count = counts.TryGetValue(record.CertPath, out var value)
                ? value
                : counts.FirstOrDefault(x => PathsEqual(x.Key, record.CertPath)).Value;
            return CertificateManager.ToDto(record, count);
        }).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CertificateDto>> Get(int id, CancellationToken ct)
    {
        var record = await manager.GetRecordAsync(id, ct);
        return Ok(CertificateManager.ToDto(record));
    }

    [HttpPost]
    public async Task<ActionResult<CertificateDto>> Create(CreateCertificateRequest request, CancellationToken ct)
    {
        var record = await manager.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = record.Id }, CertificateManager.ToDto(record));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CertificateDto>> Update(int id, UpdateCertificateRequest request, CancellationToken ct)
    {
        var record = await manager.UpdateAsync(id, request, ct);
        return Ok(CertificateManager.ToDto(record));
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id, CancellationToken ct)
    {
        await manager.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/renew")]
    public async Task<ActionResult<CertificateOperationResult>> Renew(
        int id,
        [FromBody] RenewCertificateRequest? request,
        CancellationToken ct)
    {
        var result = await manager.RenewAsync(id, request?.Force ?? true, ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/dns-challenge")]
    public async Task<ActionResult<DnsChallengeDto>> PrepareDnsChallenge(int id, CancellationToken ct)
    {
        return Ok(await manager.PrepareDnsChallengeAsync(id, ct));
    }

    [HttpPost("{id:int}/dns-challenge/complete")]
    public async Task<ActionResult<CertificateOperationResult>> CompleteDnsChallenge(int id, CancellationToken ct)
    {
        return Ok(await manager.CompleteDnsChallengeAsync(id, ct));
    }

    [HttpDelete("{id:int}/dns-challenge")]
    public ActionResult CancelDnsChallenge(int id)
    {
        manager.CancelDnsChallenge(id);
        return NoContent();
    }

    [HttpPost("{id:int}/sync")]
    public async Task<ActionResult<CertificateDto>> Sync(int id, CancellationToken ct)
    {
        var record = await manager.SyncAsync(id, ct);
        return Ok(CertificateManager.ToDto(record));
    }

    [HttpPost("{id:int}/deploy")]
    public async Task<ActionResult<NginxDeployResult>> Deploy(
        int id,
        [FromBody] DeployCertificateRequest? request,
        CancellationToken ct)
    {
        var result = await manager.DeployAsync(id, request ?? new DeployCertificateRequest(), ct);
        return Ok(result);
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import(CancellationToken ct)
    {
        return Ok(await manager.ImportFromNginxAsync(ct));
    }

    [HttpPost("sync-all")]
    public async Task<ActionResult<object>> SyncAll(CancellationToken ct)
    {
        var changed = await manager.SyncAllAsync(ct);
        return Ok(new { changed });
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.Ordinal);
        }
        catch
        {
            return string.Equals(first, second, StringComparison.Ordinal);
        }
    }
}

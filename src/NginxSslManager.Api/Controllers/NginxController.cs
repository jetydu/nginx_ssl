using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Dtos;
using NginxSslManager.Api.Services;
using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/nginx")]
[Authorize]
public class NginxController(
    AppDbContext db,
    NginxConfigService nginx,
    CertificateManager manager,
    ActivityLogService logs) : ControllerBase
{
    [HttpGet("sites")]
    public async Task<ActionResult<List<NginxSiteDto>>> Sites(CancellationToken ct)
    {
        var sites = await nginx.ScanAsync(ct);
        return Ok(sites.Select(ToDto).ToList());
    }

    [HttpPost("scan")]
    public async Task<ActionResult<object>> Scan(CancellationToken ct)
    {
        var sites = await nginx.ScanAsync(ct);
        await logs.InfoAsync("scan", $"扫描 nginx 配置，发现 {sites.Count} 个 server 块", ct: ct);
        return Ok(new
        {
            sites = sites.Select(ToDto).ToList(),
            count = sites.Count,
            sslCount = sites.Count(x => x.HasSsl)
        });
    }

    [HttpGet("status")]
    public async Task<ActionResult<NginxStatusDto>> Status(CancellationToken ct)
    {
        var status = await nginx.GetStatusAsync(ct);
        return Ok(new NginxStatusDto
        {
            Running = status.Running,
            ConfigOk = status.ConfigOk,
            Message = status.Message,
            Binary = status.Binary,
            ConfigPath = status.ConfigPath,
            Version = status.Version,
            CheckedAt = status.CheckedAt
        });
    }

    [HttpPost("test")]
    public async Task<ActionResult<object>> Test(CancellationToken ct)
    {
        var result = await nginx.TestAsync(ct);
        await logs.WriteAsync(
            result.Success ? "success" : "error",
            "nginx",
            result.Success ? "nginx 配置检查通过" : "nginx 配置检查失败",
            details: result.Output,
            ct: ct);

        return Ok(new { success = result.Success, output = result.Output });
    }

    [HttpPost("reload")]
    public async Task<ActionResult<object>> Reload(CancellationToken ct)
    {
        var result = await nginx.ReloadAsync(testFirst: true, ct);
        return Ok(new { success = result.Success, output = result.Output });
    }

    [HttpGet("config")]
    public async Task<ActionResult<object>> Config([FromQuery] string? path, CancellationToken ct)
    {
        var content = await nginx.ReadConfigAsync(path, ct);
        return Ok(new { path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path), content });
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import(CancellationToken ct)
    {
        var result = await manager.ImportFromNginxAsync(ct);
        return Ok(result);
    }

    [HttpGet("certificates/{id:int}/sites")]
    public async Task<ActionResult<List<NginxSiteDto>>> SitesForCertificate(int id, CancellationToken ct)
    {
        var record = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (record is null)
        {
            return NotFound(new { message = "证书不存在" });
        }

        var sites = await nginx.ScanAsync(ct);
        var matched = sites.Where(site =>
            site.CertificateId == id ||
            (site.CertificatePath is not null && string.Equals(Path.GetFullPath(site.CertificatePath), Path.GetFullPath(record.CertPath), StringComparison.Ordinal)) ||
            site.ServerNames.Any(name => record.DomainList.Any(domain =>
                string.Equals(name, domain, StringComparison.OrdinalIgnoreCase))));

        return Ok(matched.Select(ToDto).ToList());
    }

    private static NginxSiteDto ToDto(NginxSiteInfo site) => new()
    {
        ConfigFile = site.ConfigFile,
        Line = site.Line,
        ServerNames = site.ServerNames,
        Listens = site.Listens,
        HasSsl = site.HasSsl,
        ListensOn80 = site.ListensOn80,
        ListensOn443 = site.ListensOn443,
        CertificatePath = site.CertificatePath,
        CertificateKeyPath = site.CertificateKeyPath,
        HasOwnCertificate = site.HasOwnCertificate,
        HasAcmeLocation = site.HasAcmeLocation,
        CertificateId = site.CertificateId,
        CertificateStatus = site.Certificate?.Status,
        NotAfter = site.Certificate?.NotAfter,
        DaysRemaining = site.Certificate?.DaysRemaining,
        Issuer = site.Certificate?.Issuer,
        Error = site.Error
    };
}

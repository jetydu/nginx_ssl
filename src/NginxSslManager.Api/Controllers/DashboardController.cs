using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Dtos;
using NginxSslManager.Api.Services;
using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(
    AppDbContext db,
    NginxConfigService nginx,
    ILogger<DashboardController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> Get(CancellationToken ct)
    {
        var records = await db.Certificates.AsNoTracking().ToListAsync(ct);
        var now = DateTime.UtcNow;

        var sites = new List<NginxSiteInfo>();
        NginxStatusDto nginxStatus;
        try
        {
            sites = await nginx.ScanAsync(ct);
            var status = await nginx.GetStatusAsync(ct);
            nginxStatus = new NginxStatusDto
            {
                Running = status.Running,
                ConfigOk = status.ConfigOk,
                Message = status.Message,
                Binary = status.Binary,
                ConfigPath = status.ConfigPath,
                Version = status.Version,
                CheckedAt = status.CheckedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard nginx scan failed");
            nginxStatus = new NginxStatusDto
            {
                Running = false,
                ConfigOk = false,
                Message = ex.Message,
                CheckedAt = DateTime.UtcNow
            };
        }

        var siteCounts = sites
            .Where(x => !string.IsNullOrWhiteSpace(x.CertificatePath))
            .GroupBy(x => x.CertificatePath!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);

        int SiteCountFor(string path) =>
            siteCounts.TryGetValue(path, out var count)
                ? count
                : siteCounts.FirstOrDefault(x => PathsEqual(x.Key, path)).Value;

        var upcoming = records
            .OrderBy(x => x.NotAfter ?? DateTime.MaxValue)
            .Take(8)
            .Select(x => CertificateManager.ToDto(x, SiteCountFor(x.CertPath)))
            .ToList();

        var next = records
            .Where(x => x.NotAfter > now)
            .OrderBy(x => x.NotAfter)
            .FirstOrDefault();

        var activity = await db.ActivityLogs.AsNoTracking()
            .OrderByDescending(x => x.Timestamp)
            .Take(12)
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

        var buckets = new[] { "valid", "expiring", "critical", "expired", "error", "pending", "unknown" }
            .Select(status => new StatusBucketDto
            {
                Status = status,
                Count = records.Count(x => string.Equals(x.Status, status, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

        return Ok(new DashboardDto
        {
            TotalCertificates = records.Count,
            ValidCertificates = records.Count(x => x.Status is "valid"),
            ExpiringSoon = records.Count(x => x.Status is "expiring" or "critical"),
            ExpiredCertificates = records.Count(x => x.Status is "expired"),
            ErrorCertificates = records.Count(x => x.Status is "error"),
            AutoRenewEnabled = records.Count(x => x.AutoRenew),
            NginxSites = sites.Count,
            NginxSslSites = sites.Count(x => x.HasSsl),
            RenewedLast30Days = records.Count(x => x.LastRenewedAt >= now.AddDays(-30)),
            NextExpiry = next?.NotAfter,
            NextExpiryDomain = next?.PrimaryDomain,
            Nginx = nginxStatus,
            Upcoming = upcoming,
            RecentActivity = activity,
            StatusBuckets = buckets
        });
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

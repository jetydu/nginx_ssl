using NginxSslManager.Api.Data;
using NginxSslManager.Api.Models;

namespace NginxSslManager.Api.Services;

public class ActivityLogService(AppDbContext db)
{
    public async Task WriteAsync(
        string level,
        string category,
        string message,
        int? certificateId = null,
        string? domain = null,
        string? details = null,
        CancellationToken ct = default)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            CertificateId = certificateId,
            Domain = domain,
            Message = message,
            Details = details
        });

        await db.SaveChangesAsync(ct);
    }

    public Task InfoAsync(string category, string message, int? certificateId = null, string? domain = null, string? details = null, CancellationToken ct = default)
        => WriteAsync("info", category, message, certificateId, domain, details, ct);

    public Task SuccessAsync(string category, string message, int? certificateId = null, string? domain = null, string? details = null, CancellationToken ct = default)
        => WriteAsync("success", category, message, certificateId, domain, details, ct);

    public Task WarningAsync(string category, string message, int? certificateId = null, string? domain = null, string? details = null, CancellationToken ct = default)
        => WriteAsync("warning", category, message, certificateId, domain, details, ct);

    public Task ErrorAsync(string category, string message, int? certificateId = null, string? domain = null, string? details = null, CancellationToken ct = default)
        => WriteAsync("error", category, message, certificateId, domain, details, ct);
}

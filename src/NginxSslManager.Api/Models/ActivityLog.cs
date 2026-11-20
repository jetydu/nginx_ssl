namespace NginxSslManager.Api.Models;

public class ActivityLog
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>info | success | warning | error</summary>
    public string Level { get; set; } = "info";

    /// <summary>issue | renew | deploy | scan | nginx | import | auth | system</summary>
    public string Category { get; set; } = "system";

    public int? CertificateId { get; set; }

    public string? Domain { get; set; }

    public string Message { get; set; } = "";

    public string? Details { get; set; }
}

namespace NginxSslManager.Api.Models;

public class CertificateRecord
{
    public int Id { get; set; }

    /// <summary>Comma separated list of DNS names, primary domain first.</summary>
    public string Domains { get; set; } = "";

    public string PrimaryDomain { get; set; } = "";

    public string Email { get; set; } = "";

    /// <summary>http-01 is currently supported.</summary>
    public string ChallengeType { get; set; } = "http-01";

    public string? WebRootPath { get; set; }

    public string CertPath { get; set; } = "";

    public string KeyPath { get; set; } = "";

    /// <summary>Optional per-certificate ACME directory override.</summary>
    public string? AcmeDirectory { get; set; }

    public bool AutoRenew { get; set; } = true;

    /// <summary>Update nginx configuration and reload after a successful renewal.</summary>
    public bool AutoDeploy { get; set; } = true;

    /// <summary>manual | letsencrypt | imported</summary>
    public string Source { get; set; } = "manual";

    /// <summary>valid | expiring | critical | expired | pending | issuing | error | unknown</summary>
    public string Status { get; set; } = "pending";

    public DateTime? NotBefore { get; set; }

    public DateTime? NotAfter { get; set; }

    public int? DaysRemaining { get; set; }

    public string? Subject { get; set; }

    public string? Issuer { get; set; }

    public string? SerialNumber { get; set; }

    public string? Thumbprint { get; set; }

    public string? LastError { get; set; }

    public DateTime? LastRenewedAt { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public DateTime? LastAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public IReadOnlyList<string> DomainList =>
        Domains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

namespace NginxSslManager.Api.Dtos;

public sealed class CertificateDto
{
    public int Id { get; init; }
    public string PrimaryDomain { get; init; } = "";
    public List<string> Domains { get; init; } = [];
    public string Email { get; init; } = "";
    public string ChallengeType { get; init; } = "http-01";
    public string? WebRootPath { get; init; }
    public string CertPath { get; init; } = "";
    public string KeyPath { get; init; } = "";
    public string? AcmeDirectory { get; init; }
    public bool AutoRenew { get; init; }
    public bool AutoDeploy { get; init; }
    public string Source { get; init; } = "";
    public string Status { get; init; } = "";
    public DateTime? NotBefore { get; init; }
    public DateTime? NotAfter { get; init; }
    public int? DaysRemaining { get; init; }
    public string? Subject { get; init; }
    public string? Issuer { get; init; }
    public string? SerialNumber { get; init; }
    public string? Thumbprint { get; init; }
    public string? LastError { get; init; }
    public DateTime? LastRenewedAt { get; init; }
    public DateTime? LastCheckedAt { get; init; }
    public DateTime? LastAttemptAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public int NginxSiteCount { get; init; }
}

public sealed class CreateCertificateRequest
{
    public List<string>? Domains { get; set; }
    public string? Email { get; set; }
    public string? ChallengeType { get; set; }
    public string? WebRootPath { get; set; }
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public string? AcmeDirectory { get; set; }
    public bool AutoRenew { get; set; } = true;
    public bool AutoDeploy { get; set; } = true;
}

public sealed class UpdateCertificateRequest
{
    public List<string>? Domains { get; set; }
    public string? Email { get; set; }
    public string? ChallengeType { get; set; }
    public string? WebRootPath { get; set; }
    public string? CertPath { get; set; }
    public string? KeyPath { get; set; }
    public string? AcmeDirectory { get; set; }
    public bool? AutoRenew { get; set; }
    public bool? AutoDeploy { get; set; }
}

public sealed class DeployCertificateRequest
{
    public bool UpdateNginxConfig { get; set; } = true;
    public bool Reload { get; set; } = true;
}

public sealed class RenewCertificateRequest
{
    public bool Force { get; set; }
}

public sealed class DnsChallengeRecordDto
{
    public string Domain { get; init; } = "";
    public bool Wildcard { get; init; }
    public string RecordName { get; init; } = "";
    public string RecordValue { get; init; } = "";
}

public sealed class DnsChallengeDto
{
    public int CertificateId { get; init; }
    public string OrderUrl { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public List<DnsChallengeRecordDto> Records { get; init; } = [];
}

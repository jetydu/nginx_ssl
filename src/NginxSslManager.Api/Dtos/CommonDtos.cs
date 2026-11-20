namespace NginxSslManager.Api.Dtos;

public sealed class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class LoginResponse
{
    public string Token { get; init; } = "";
    public string Username { get; init; } = "";
    public DateTime ExpiresAt { get; init; }
    public bool IsDefaultPassword { get; init; }
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class SettingsDto
{
    public string AcmeDirectory { get; init; } = "";
    public string AcmeStagingDirectory { get; init; } = "";
    public string DefaultEmail { get; init; } = "";
    public int RenewDaysBefore { get; init; }
    public int CheckIntervalMinutes { get; init; }
    public bool AutoRenewEnabled { get; init; }
    public string NginxBinary { get; init; } = "";
    public string NginxConfigPath { get; init; } = "";
    public string NginxPrefix { get; init; } = "";
    public string NginxReloadCommand { get; init; } = "";
    public bool NginxTestEnabled { get; init; }
    public string DefaultWebRoot { get; init; } = "";
    public string CertificateRoot { get; init; } = "";
    public string ChallengeMode { get; init; } = "";
    public bool AutoImport { get; init; }
    public string WebhookUrl { get; init; } = "";
    public string AdminUsername { get; init; } = "";
    public bool IsDefaultPassword { get; init; }
    public string LetsEncryptProduction { get; init; } = "";
    public string LetsEncryptStaging { get; init; } = "";
}

public sealed class DashboardDto
{
    public int TotalCertificates { get; init; }
    public int ValidCertificates { get; init; }
    public int ExpiringSoon { get; init; }
    public int ExpiredCertificates { get; init; }
    public int ErrorCertificates { get; init; }
    public int AutoRenewEnabled { get; init; }
    public int NginxSites { get; init; }
    public int NginxSslSites { get; init; }
    public int RenewedLast30Days { get; init; }
    public DateTime? NextExpiry { get; init; }
    public string? NextExpiryDomain { get; init; }
    public NginxStatusDto Nginx { get; init; } = new();
    public List<CertificateDto> Upcoming { get; init; } = [];
    public List<ActivityLogDto> RecentActivity { get; init; } = [];
    public List<StatusBucketDto> StatusBuckets { get; init; } = [];
}

public sealed class NginxStatusDto
{
    public bool Running { get; init; }
    public bool ConfigOk { get; init; }
    public string Message { get; init; } = "";
    public string Binary { get; init; } = "";
    public string ConfigPath { get; init; } = "";
    public string? Version { get; init; }
    public DateTime CheckedAt { get; init; }
}

public sealed class ActivityLogDto
{
    public long Id { get; init; }
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = "";
    public string Category { get; init; } = "";
    public int? CertificateId { get; init; }
    public string? Domain { get; init; }
    public string Message { get; init; } = "";
    public string? Details { get; init; }
}

public sealed class StatusBucketDto
{
    public string Status { get; init; } = "";
    public int Count { get; init; }
}

public sealed class NginxSiteDto
{
    public string ConfigFile { get; init; } = "";
    public int Line { get; init; }
    public List<string> ServerNames { get; init; } = [];
    public List<string> Listens { get; init; } = [];
    public bool HasSsl { get; init; }
    public bool ListensOn80 { get; init; }
    public bool ListensOn443 { get; init; }
    public string? CertificatePath { get; init; }
    public string? CertificateKeyPath { get; init; }
    public bool HasOwnCertificate { get; init; }
    public bool HasAcmeLocation { get; init; }
    public int? CertificateId { get; init; }
    public string? CertificateStatus { get; init; }
    public DateTime? NotAfter { get; init; }
    public int? DaysRemaining { get; init; }
    public string? Issuer { get; init; }
    public string? Error { get; init; }
}

public sealed class ImportResultDto
{
    public int Imported { get; init; }
    public int Skipped { get; init; }
    public List<string> Domains { get; init; } = [];
}

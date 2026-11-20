namespace NginxSslManager.Api.Services;

/// <summary>
/// Column names and default values for persisted application settings.
/// </summary>
public static class SettingKeys
{
    public const string AcmeDirectory = "acme.directory";
    public const string AcmeStagingDirectory = "acme.stagingDirectory";
    public const string DefaultEmail = "acme.email";
    public const string RenewDaysBefore = "renew.daysBefore";
    public const string CheckIntervalMinutes = "renew.checkIntervalMinutes";
    public const string AutoRenewEnabled = "renew.autoRenewEnabled";
    public const string NginxBinary = "nginx.binaryPath";
    public const string NginxConfigPath = "nginx.configPath";
    public const string NginxPrefix = "nginx.prefix";
    public const string NginxReloadCommand = "nginx.reloadCommand";
    public const string NginxTestEnabled = "nginx.testEnabled";
    public const string DefaultWebRoot = "webroot.default";
    public const string CertificateRoot = "paths.certificateRoot";
    public const string ChallengeMode = "challenge.mode";
    public const string AutoImport = "nginx.autoImport";
    public const string WebhookUrl = "notify.webhookUrl";
    public const string AdminUsername = "admin.username";
    public const string AdminPasswordHash = "admin.passwordHash";

    public const string LetsEncryptProduction = "https://acme-v02.api.letsencrypt.org/directory";
    public const string LetsEncryptStaging = "https://acme-staging-v02.api.letsencrypt.org/directory";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [AcmeDirectory] = LetsEncryptProduction,
        [AcmeStagingDirectory] = LetsEncryptStaging,
        [DefaultEmail] = "",
        [RenewDaysBefore] = "30",
        [CheckIntervalMinutes] = "60",
        [AutoRenewEnabled] = "true",
        [NginxBinary] = "/usr/sbin/nginx",
        [NginxConfigPath] = "/etc/nginx/nginx.conf",
        [NginxPrefix] = "/etc/nginx",
        [NginxReloadCommand] = "/usr/sbin/nginx -s reload",
        [NginxTestEnabled] = "true",
        [DefaultWebRoot] = "/var/www/letsencrypt",
        [CertificateRoot] = "/etc/nginx/ssl/letsencrypt",
        [ChallengeMode] = "auto",
        [AutoImport] = "true",
        [WebhookUrl] = "",
        [AdminUsername] = "admin"
    };
}

public sealed record AppSettings(
    string AcmeDirectory,
    string AcmeStagingDirectory,
    string DefaultEmail,
    int RenewDaysBefore,
    int CheckIntervalMinutes,
    bool AutoRenewEnabled,
    string NginxBinary,
    string NginxConfigPath,
    string NginxPrefix,
    string NginxReloadCommand,
    bool NginxTestEnabled,
    string DefaultWebRoot,
    string CertificateRoot,
    string ChallengeMode,
    bool AutoImport,
    string WebhookUrl,
    string AdminUsername);

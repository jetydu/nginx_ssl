using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Common;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Models;

namespace NginxSslManager.Api.Services;

public class SettingsService(AppDbContext db)
{
    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var existing = await db.Settings.Select(x => x.Key).ToListAsync(ct);
        var changed = false;

        foreach (var (key, value) in SettingKeys.Defaults)
        {
            if (existing.Contains(key))
            {
                continue;
            }

            db.Settings.Add(new AppSetting { Key = key, Value = value });
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var setting = await db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key, ct);

        if (setting is not null)
        {
            return setting.Value;
        }

        return SettingKeys.Defaults.TryGetValue(key, out var value) ? value : null;
    }

    public async Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default)
    {
        var rows = await db.Settings.AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        string Value(string key) =>
            rows.TryGetValue(key, out var value) ? value :
            SettingKeys.Defaults.TryGetValue(key, out var fallback) ? fallback : "";

        int IntValue(string key, int fallback) =>
            int.TryParse(Value(key), out var parsed) ? parsed : fallback;

        bool BoolValue(string key, bool fallback) =>
            bool.TryParse(Value(key), out var parsed) ? parsed : fallback;

        return new AppSettings(
            Value(SettingKeys.AcmeDirectory),
            Value(SettingKeys.AcmeStagingDirectory),
            Value(SettingKeys.DefaultEmail),
            IntValue(SettingKeys.RenewDaysBefore, 30),
            IntValue(SettingKeys.CheckIntervalMinutes, 60),
            BoolValue(SettingKeys.AutoRenewEnabled, true),
            Value(SettingKeys.NginxBinary),
            Value(SettingKeys.NginxConfigPath),
            Value(SettingKeys.NginxPrefix),
            Value(SettingKeys.NginxReloadCommand),
            BoolValue(SettingKeys.NginxTestEnabled, true),
            Value(SettingKeys.DefaultWebRoot),
            Value(SettingKeys.CertificateRoot),
            Value(SettingKeys.ChallengeMode),
            BoolValue(SettingKeys.AutoImport, true),
            Value(SettingKeys.WebhookUrl),
            Value(SettingKeys.AdminUsername));
    }

    public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (setting is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }

    public async Task UpdateAsync(UpdateSettingsRequest request, CancellationToken ct = default)
    {
        if (request.RenewDaysBefore is < 1 or > 120)
        {
            throw new ApiException("续期提前天数必须在 1-120 之间");
        }

        if (request.CheckIntervalMinutes is < 5 or > 10080)
        {
            throw new ApiException("检查间隔必须在 5-10080 分钟之间");
        }

        if (request.AcmeDirectory is { Length: > 0 } directory &&
            (!Uri.TryCreate(directory, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ApiException("ACME 目录地址必须是有效的 HTTPS URL");
        }

        if (request.ChallengeMode is not null && request.ChallengeMode is not ("auto" or "webroot"))
        {
            throw new ApiException("验证模式只能是 auto 或 webroot");
        }

        if (request.NginxConfigPath is { Length: > 0 } configPath)
        {
            PathGuard.RequireAbsolutePath(configPath, "Nginx 配置文件");
        }

        if (request.DefaultWebRoot is { Length: > 0 } webRoot)
        {
            PathGuard.RequireAbsolutePath(webRoot, "默认 Webroot");
        }

        var updates = new Dictionary<string, string?>
        {
            [SettingKeys.AcmeDirectory] = request.AcmeDirectory,
            [SettingKeys.AcmeStagingDirectory] = request.AcmeStagingDirectory,
            [SettingKeys.DefaultEmail] = request.DefaultEmail,
            [SettingKeys.RenewDaysBefore] = request.RenewDaysBefore?.ToString(),
            [SettingKeys.CheckIntervalMinutes] = request.CheckIntervalMinutes?.ToString(),
            [SettingKeys.AutoRenewEnabled] = request.AutoRenewEnabled?.ToString(),
            [SettingKeys.NginxBinary] = request.NginxBinary,
            [SettingKeys.NginxConfigPath] = request.NginxConfigPath,
            [SettingKeys.NginxPrefix] = request.NginxPrefix,
            [SettingKeys.NginxReloadCommand] = request.NginxReloadCommand,
            [SettingKeys.NginxTestEnabled] = request.NginxTestEnabled?.ToString(),
            [SettingKeys.DefaultWebRoot] = request.DefaultWebRoot,
            [SettingKeys.CertificateRoot] = request.CertificateRoot,
            [SettingKeys.ChallengeMode] = request.ChallengeMode,
            [SettingKeys.AutoImport] = request.AutoImport?.ToString(),
            [SettingKeys.WebhookUrl] = request.WebhookUrl
        };

        foreach (var (key, value) in updates)
        {
            if (value is not null)
            {
                await SetAsync(key, value, ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

public sealed class UpdateSettingsRequest
{
    public string? AcmeDirectory { get; set; }
    public string? AcmeStagingDirectory { get; set; }
    public string? DefaultEmail { get; set; }
    public int? RenewDaysBefore { get; set; }
    public int? CheckIntervalMinutes { get; set; }
    public bool? AutoRenewEnabled { get; set; }
    public string? NginxBinary { get; set; }
    public string? NginxConfigPath { get; set; }
    public string? NginxPrefix { get; set; }
    public string? NginxReloadCommand { get; set; }
    public bool? NginxTestEnabled { get; set; }
    public string? DefaultWebRoot { get; set; }
    public string? CertificateRoot { get; set; }
    public string? ChallengeMode { get; set; }
    public bool? AutoImport { get; set; }
    public string? WebhookUrl { get; set; }
}

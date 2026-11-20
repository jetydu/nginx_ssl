using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NginxSslManager.Api.Dtos;
using NginxSslManager.Api.Services;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize]
public class SettingsController(
    SettingsService settings,
    AdminAccountService accounts,
    ActivityLogService logs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get(CancellationToken ct)
    {
        var value = await settings.GetAppSettingsAsync(ct);
        return Ok(new SettingsDto
        {
            AcmeDirectory = value.AcmeDirectory,
            AcmeStagingDirectory = value.AcmeStagingDirectory,
            DefaultEmail = value.DefaultEmail,
            RenewDaysBefore = value.RenewDaysBefore,
            CheckIntervalMinutes = value.CheckIntervalMinutes,
            AutoRenewEnabled = value.AutoRenewEnabled,
            NginxBinary = value.NginxBinary,
            NginxConfigPath = value.NginxConfigPath,
            NginxPrefix = value.NginxPrefix,
            NginxReloadCommand = value.NginxReloadCommand,
            NginxTestEnabled = value.NginxTestEnabled,
            DefaultWebRoot = value.DefaultWebRoot,
            CertificateRoot = value.CertificateRoot,
            ChallengeMode = value.ChallengeMode,
            AutoImport = value.AutoImport,
            WebhookUrl = value.WebhookUrl,
            AdminUsername = value.AdminUsername,
            IsDefaultPassword = await accounts.IsDefaultPasswordAsync(ct),
            LetsEncryptProduction = SettingKeys.LetsEncryptProduction,
            LetsEncryptStaging = SettingKeys.LetsEncryptStaging
        });
    }

    [HttpPut]
    public async Task<ActionResult<SettingsDto>> Update(UpdateSettingsRequest request, CancellationToken ct)
    {
        await settings.UpdateAsync(request, ct);
        await logs.InfoAsync("system", "系统设置已更新", ct: ct);
        return await Get(ct);
    }
}

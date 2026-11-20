using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NginxSslManager.Api.Services;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController(SettingsService settings) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<object>> Get(CancellationToken ct)
    {
        var value = await settings.GetAppSettingsAsync(ct);
        return Ok(new
        {
            status = "ok",
            time = DateTime.UtcNow,
            version = typeof(HealthController).Assembly.GetName().Version?.ToString(),
            nginxConfig = value.NginxConfigPath
        });
    }
}

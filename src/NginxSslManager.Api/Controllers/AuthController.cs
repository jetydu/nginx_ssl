using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NginxSslManager.Api.Dtos;
using NginxSslManager.Api.Services;

namespace NginxSslManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    AdminAccountService accounts,
    SettingsService settings,
    JwtService jwt,
    ActivityLogService logs) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "请输入用户名和密码" });
        }

        if (!await accounts.ValidateAsync(request.Username, request.Password, ct))
        {
            await logs.WarningAsync("auth", $"登录失败: {request.Username}", ct: ct);
            return Unauthorized(new { message = "用户名或密码错误" });
        }

        var (token, expiresAt) = jwt.CreateToken(request.Username);
        var adminUsername = await settings.GetAsync(SettingKeys.AdminUsername, ct) ?? request.Username;
        await logs.InfoAsync("auth", $"管理员登录: {adminUsername}", ct: ct);

        return Ok(new LoginResponse
        {
            Token = token,
            Username = adminUsername,
            ExpiresAt = expiresAt,
            IsDefaultPassword = await accounts.IsDefaultPasswordAsync(ct)
        });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<object>> Me(CancellationToken ct)
    {
        var username = await settings.GetAsync(SettingKeys.AdminUsername, ct) ?? "admin";
        return Ok(new
        {
            username,
            isDefaultPassword = await accounts.IsDefaultPasswordAsync(ct)
        });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await accounts.ChangePasswordAsync(request.CurrentPassword, request.NewPassword, ct);
        await logs.InfoAsync("auth", "管理员密码已修改", ct: ct);
        return Ok(new { message = "密码已修改，请使用新密码重新登录" });
    }
}

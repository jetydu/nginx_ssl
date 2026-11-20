using System.Security.Cryptography;

namespace NginxSslManager.Api.Services;

public static class PasswordHasher
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public class AdminAccountService(SettingsService settings)
{
    public const string DefaultPassword = "admin123";

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        var hash = await settings.GetAsync(SettingKeys.AdminPasswordHash, ct);
        if (string.IsNullOrWhiteSpace(hash))
        {
            await settings.SetAsync(SettingKeys.AdminPasswordHash, PasswordHasher.Hash(DefaultPassword), ct);
            await settings.SetAsync(SettingKeys.AdminUsername, "admin", ct);
            await settings.SaveAsync(ct);
        }
    }

    public async Task<bool> ValidateAsync(string username, string password, CancellationToken ct = default)
    {
        var adminUsername = await settings.GetAsync(SettingKeys.AdminUsername, ct) ?? "admin";
        var hash = await settings.GetAsync(SettingKeys.AdminPasswordHash, ct);
        return string.Equals(username, adminUsername, StringComparison.OrdinalIgnoreCase) &&
               PasswordHasher.Verify(password, hash);
    }

    public async Task<bool> IsDefaultPasswordAsync(CancellationToken ct = default)
    {
        var hash = await settings.GetAsync(SettingKeys.AdminPasswordHash, ct);
        return PasswordHasher.Verify(DefaultPassword, hash);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var adminUsername = await settings.GetAsync(SettingKeys.AdminUsername, ct) ?? "admin";
        if (!await ValidateAsync(adminUsername, currentPassword, ct))
        {
            throw new Common.ApiException("当前密码不正确");
        }

        if (newPassword.Length < 8)
        {
            throw new Common.ApiException("新密码至少需要 8 个字符");
        }

        await settings.SetAsync(SettingKeys.AdminPasswordHash, PasswordHasher.Hash(newPassword), ct);
        await settings.SaveAsync(ct);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NginxSslManager.Api.Common;
namespace NginxSslManager.Api.Services;

public class JwtService
{
    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(AppPaths paths, IConfiguration configuration)
    {
        var key = LoadOrCreateKey(paths.JwtKeyPath);
        _key = new SymmetricSecurityKey(key);
        _issuer = configuration["Jwt:Issuer"] ?? "nginx-ssl-manager";
        _audience = configuration["Jwt:Audience"] ?? "nginx-ssl-manager-ui";
    }

    public SymmetricSecurityKey SecurityKey => _key;
    public string Issuer => _issuer;
    public string Audience => _audience;

    public (string Token, DateTime ExpiresAt) CreateToken(string username, int hours = 12)
    {
        var expiresAt = DateTime.UtcNow.AddHours(hours);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            _issuer,
            _audience,
            claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private static byte[] LoadOrCreateKey(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32)
            {
                try
                {
                    return Convert.FromBase64String(existing);
                }
                catch (FormatException)
                {
                    // Replace an unreadable key rather than failing to start.
                }
            }
        }

        var key = RandomNumberGenerator.GetBytes(64);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Convert.ToBase64String(key));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return key;
    }
}

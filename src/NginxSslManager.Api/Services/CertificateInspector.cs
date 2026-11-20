using System.Security.Cryptography.X509Certificates;
using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Api.Services;

public class CertificateInspector
{
    public CertificateSummary? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var info = Read(path);
            return new CertificateSummary
            {
                Path = path,
                Subject = info.Subject,
                Issuer = info.Issuer,
                DnsNames = info.DnsNames,
                NotBefore = info.NotBefore,
                NotAfter = info.NotAfter,
                DaysRemaining = DaysRemaining(info.NotAfter),
                Status = ComputeStatus(info.NotAfter),
                Thumbprint = info.Thumbprint,
                SerialNumber = info.SerialNumber
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public CertificateFileInfo Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"无法读取证书文件 {path}: {ex.Message}", ex);
        }

        if (text.Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
        {
            return ReadPem(text);
        }

        using var certificate = X509CertificateLoader.LoadCertificateFromFile(path);
        return FromCertificate(certificate);
    }

    public static CertificateFileInfo ReadPem(string pem)
    {
        using var certificate = X509Certificate2.CreateFromPem(pem);
        return FromCertificate(certificate);
    }

    private static CertificateFileInfo FromCertificate(X509Certificate2 certificate)
    {
        var san = certificate.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();

        var names = san?.EnumerateDnsNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (names.Count == 0 && !string.IsNullOrWhiteSpace(certificate.GetNameInfo(X509NameType.SimpleName, false)))
        {
            names.Add(certificate.GetNameInfo(X509NameType.SimpleName, false));
        }

        return new CertificateFileInfo
        {
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            DnsNames = names,
            NotBefore = certificate.NotBefore.ToUniversalTime(),
            NotAfter = certificate.NotAfter.ToUniversalTime(),
            Thumbprint = certificate.Thumbprint,
            SerialNumber = certificate.SerialNumber,
            IsSelfSigned = certificate.Subject == certificate.Issuer
        };
    }

    public static int DaysRemaining(DateTime? notAfter)
    {
        if (notAfter is null)
        {
            return 0;
        }

        return (int)Math.Floor((notAfter.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays);
    }

    public static string ComputeStatus(DateTime? notAfter, bool hasError = false)
    {
        if (hasError)
        {
            return "error";
        }

        if (notAfter is null)
        {
            return "unknown";
        }

        var now = DateTime.UtcNow;
        var notAfterUtc = notAfter.Value.ToUniversalTime();
        if (notAfterUtc <= now)
        {
            return "expired";
        }

        var days = (notAfterUtc - now).TotalDays;
        return days switch
        {
            <= 7 => "critical",
            <= 30 => "expiring",
            _ => "valid"
        };
    }
}

public sealed class CertificateFileInfo
{
    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public List<string> DnsNames { get; init; } = [];
    public DateTime NotBefore { get; init; }
    public DateTime NotAfter { get; init; }
    public string Thumbprint { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public bool IsSelfSigned { get; init; }
}

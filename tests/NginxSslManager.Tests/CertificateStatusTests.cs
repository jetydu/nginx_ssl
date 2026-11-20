using NginxSslManager.Api.Services;

namespace NginxSslManager.Tests;

public class CertificateStatusTests
{
    [Fact]
    public void ComputeStatus_HandlesMissingExpiry() =>
        Assert.Equal("unknown", CertificateInspector.ComputeStatus(null));

    [Fact]
    public void ComputeStatus_PrefersErrors() =>
        Assert.Equal("error", CertificateInspector.ComputeStatus(DateTime.UtcNow.AddDays(100), hasError: true));

    [Fact]
    public void ComputeStatus_DetectsExpired() =>
        Assert.Equal("expired", CertificateInspector.ComputeStatus(DateTime.UtcNow.AddDays(-1)));

    [Fact]
    public void ComputeStatus_DetectsCriticalWindow() =>
        Assert.Equal("critical", CertificateInspector.ComputeStatus(DateTime.UtcNow.AddDays(3)));

    [Fact]
    public void ComputeStatus_DetectsExpiringWindow() =>
        Assert.Equal("expiring", CertificateInspector.ComputeStatus(DateTime.UtcNow.AddDays(10)));

    [Fact]
    public void ComputeStatus_DetectsValidCertificate() =>
        Assert.Equal("valid", CertificateInspector.ComputeStatus(DateTime.UtcNow.AddDays(90)));
}

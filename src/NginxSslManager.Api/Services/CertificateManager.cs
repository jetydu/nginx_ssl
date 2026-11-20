using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Common;
using NginxSslManager.Api.Data;
using NginxSslManager.Api.Dtos;
using NginxSslManager.Api.Models;
using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Api.Services;

public sealed class CertificateOperationResult
{
    public CertificateDto Certificate { get; init; } = new();
    public string Message { get; init; } = "";
    public string? Warning { get; init; }
}

public class CertificateManager(
    AppDbContext db,
    SettingsService settingsService,
    CertificateInspector inspector,
    AcmeService acme,
    NginxConfigService nginx,
    ActivityLogService logs,
    AppPaths paths,
    DnsChallengeStore dnsStore,
    ILogger<CertificateManager> logger)
{
    private static readonly SemaphoreSlim OperationLock = new(1, 1);
    private static readonly Regex DomainRegex = new(
        @"^(?=.{1,253}$)(\*\.)?([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static CertificateDto ToDto(CertificateRecord record, int nginxSiteCount = 0) => new()
    {
        Id = record.Id,
        PrimaryDomain = record.PrimaryDomain,
        Domains = record.DomainList.ToList(),
        Email = record.Email,
        ChallengeType = record.ChallengeType,
        WebRootPath = record.WebRootPath,
        CertPath = record.CertPath,
        KeyPath = record.KeyPath,
        AcmeDirectory = record.AcmeDirectory,
        AutoRenew = record.AutoRenew,
        AutoDeploy = record.AutoDeploy,
        Source = record.Source,
        Status = record.Status,
        NotBefore = record.NotBefore,
        NotAfter = record.NotAfter,
        DaysRemaining = record.DaysRemaining,
        Subject = record.Subject,
        Issuer = record.Issuer,
        SerialNumber = record.SerialNumber,
        Thumbprint = record.Thumbprint,
        LastError = record.LastError,
        LastRenewedAt = record.LastRenewedAt,
        LastCheckedAt = record.LastCheckedAt,
        LastAttemptAt = record.LastAttemptAt,
        CreatedAt = record.CreatedAt,
        UpdatedAt = record.UpdatedAt,
        NginxSiteCount = nginxSiteCount
    };

    public async Task<CertificateRecord> CreateAsync(CreateCertificateRequest request, CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var domains = NormalizeDomains(request.Domains);
        var primary = domains[0];
        var email = string.IsNullOrWhiteSpace(request.Email) ? settings.DefaultEmail : request.Email.Trim();

        if (!string.IsNullOrWhiteSpace(email) && (!email.Contains('@') || email.Length > 320))
        {
            throw new ApiException("邮箱地址格式不正确");
        }

        var challengeType = ResolveChallengeType(request.ChallengeType, domains.Any(IsWildcard));
        var webRoot = string.IsNullOrWhiteSpace(request.WebRootPath)
            ? settings.DefaultWebRoot
            : request.WebRootPath;
        webRoot = PathGuard.RequireAbsolutePath(webRoot, "Webroot");

        var certPath = string.IsNullOrWhiteSpace(request.CertPath)
            ? Path.Combine(settings.CertificateRoot, primary, "fullchain.pem")
            : request.CertPath;
        var keyPath = string.IsNullOrWhiteSpace(request.KeyPath)
            ? Path.Combine(settings.CertificateRoot, primary, "privkey.pem")
            : request.KeyPath;

        certPath = PathGuard.RequireAbsolutePath(certPath, "证书路径");
        keyPath = PathGuard.RequireAbsolutePath(keyPath, "私钥路径");
        PathGuard.EnsureDistinct(certPath, keyPath, "证书路径和私钥路径不能相同");

        var duplicate = await db.Certificates
            .FirstOrDefaultAsync(x => x.CertPath == certPath || x.KeyPath == keyPath, ct);
        if (duplicate is not null)
        {
            throw new ApiException($"路径已被证书 {duplicate.PrimaryDomain} 使用");
        }

        var record = new CertificateRecord
        {
            Domains = string.Join(',', domains),
            PrimaryDomain = primary,
            Email = email,
            ChallengeType = challengeType,
            WebRootPath = webRoot,
            CertPath = certPath,
            KeyPath = keyPath,
            AcmeDirectory = string.IsNullOrWhiteSpace(request.AcmeDirectory) ? null : request.AcmeDirectory.Trim(),
            AutoRenew = challengeType == "dns-01" ? false : request.AutoRenew,
            AutoDeploy = request.AutoDeploy,
            Source = "manual",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        RefreshFromFile(record, preserveError: false);
        if (record.NotAfter is null)
        {
            record.Status = "pending";
        }

        db.Certificates.Add(record);
        await db.SaveChangesAsync(ct);
        await logs.InfoAsync("system", $"创建证书配置: {primary}", record.Id, primary, ct: ct);
        return record;
    }

    public async Task<CertificateRecord> UpdateAsync(int id, UpdateCertificateRequest request, CancellationToken ct = default)
    {
        var record = await GetRecordAsync(id, ct);
        if (record.Status == "issuing")
        {
            throw new ApiException("证书正在签发中，暂时不能修改");
        }

        if (request.Domains is { Count: > 0 })
        {
            var domains = NormalizeDomains(request.Domains);
            record.Domains = string.Join(',', domains);
            record.PrimaryDomain = domains[0];
            record.ChallengeType = ResolveChallengeType(record.ChallengeType, domains.Any(IsWildcard));
        }

        if (request.Email is not null)
        {
            record.Email = request.Email.Trim();
        }

        if (request.ChallengeType is not null)
        {
            record.ChallengeType = ResolveChallengeType(request.ChallengeType, record.DomainList.Any(IsWildcard));
        }

        if (record.ChallengeType == "dns-01")
        {
            record.AutoRenew = false;
        }

        if (request.WebRootPath is not null)
        {
            record.WebRootPath = PathGuard.RequireAbsolutePath(request.WebRootPath, "Webroot");
        }

        if (request.CertPath is not null)
        {
            record.CertPath = PathGuard.RequireAbsolutePath(request.CertPath, "证书路径");
        }

        if (request.KeyPath is not null)
        {
            record.KeyPath = PathGuard.RequireAbsolutePath(request.KeyPath, "私钥路径");
        }

        PathGuard.EnsureDistinct(record.CertPath, record.KeyPath, "证书路径和私钥路径不能相同");

        var duplicate = await db.Certificates
            .FirstOrDefaultAsync(x => x.Id != id && (x.CertPath == record.CertPath || x.KeyPath == record.KeyPath), ct);
        if (duplicate is not null)
        {
            throw new ApiException($"路径已被证书 {duplicate.PrimaryDomain} 使用");
        }

        if (request.AcmeDirectory is not null)
        {
            record.AcmeDirectory = string.IsNullOrWhiteSpace(request.AcmeDirectory) ? null : request.AcmeDirectory.Trim();
        }

        if (request.AutoRenew is bool autoRenew)
        {
            record.AutoRenew = record.ChallengeType == "dns-01" ? false : autoRenew;
        }

        if (request.AutoDeploy is bool autoDeploy)
        {
            record.AutoDeploy = autoDeploy;
        }

        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await logs.InfoAsync("system", $"更新证书配置: {record.PrimaryDomain}", record.Id, record.PrimaryDomain, ct: ct);
        return record;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await OperationLock.WaitAsync(ct);
        try
        {
            var record = await GetRecordAsync(id, ct);
            if (record.Status == "issuing")
            {
                throw new ApiException("证书正在签发中，不能删除");
            }

            db.Certificates.Remove(record);
            await db.SaveChangesAsync(ct);
            await logs.WarningAsync("system", $"删除证书记录: {record.PrimaryDomain}（未删除磁盘文件）", null, record.PrimaryDomain, ct: ct);
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<CertificateOperationResult> RenewAsync(int id, bool force, CancellationToken ct = default)
    {
        await OperationLock.WaitAsync(ct);
        try
        {
            var record = await GetRecordAsync(id, ct);
            var settings = await settingsService.GetAppSettingsAsync(ct);
            var wasExisting = record.NotAfter is not null;

            if (!force && record.NotAfter is not null &&
                record.NotAfter.Value.ToUniversalTime() > DateTime.UtcNow.AddDays(settings.RenewDaysBefore))
            {
                return new CertificateOperationResult
                {
                    Certificate = ToDto(record),
                    Message = $"证书距离到期还有 {record.DaysRemaining} 天，未到续期时间"
                };
            }

            if (record.ChallengeType == "dns-01" || record.DomainList.Any(IsWildcard))
            {
                throw new ApiException("通配符证书需要手动 DNS 验证，请在证书页面点击“续期”并按提示添加 TXT 记录");
            }

            record.Status = "issuing";
            record.LastAttemptAt = DateTime.UtcNow;
            record.LastError = null;
            record.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await logs.InfoAsync(
                record.NotAfter is null ? "issue" : "renew",
                $"开始{(wasExisting ? "续期" : "签发")}证书: {record.PrimaryDomain}",
                record.Id,
                record.PrimaryDomain,
                ct: ct);

            try
            {
                var webRoot = string.IsNullOrWhiteSpace(record.WebRootPath) ? settings.DefaultWebRoot : record.WebRootPath;
                var issued = await acme.IssueAsync(
                    record,
                    webRoot,
                    message => logs.InfoAsync("renew", message, record.Id, record.PrimaryDomain, ct: CancellationToken.None),
                    ct);

                var warning = await ApplyIssuedAsync(record, issued, wasExisting, ct);

                return new CertificateOperationResult
                {
                    Certificate = ToDto(record),
                    Message = "证书签发成功",
                    Warning = warning
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Certificate renewal failed for {Domain}", record.PrimaryDomain);
                var message = ex is ApiException ? ex.Message : $"签发失败: {ex.Message}";

                RefreshFromFile(record, preserveError: false);
                if (record.NotAfter is null || record.NotAfter <= DateTime.UtcNow)
                {
                    record.Status = "error";
                }
                else
                {
                    record.Status = CertificateInspector.ComputeStatus(record.NotAfter);
                }

                record.LastError = message;
                record.LastCheckedAt = DateTime.UtcNow;
                record.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                await logs.ErrorAsync("renew", message, record.Id, record.PrimaryDomain, ex.ToString(), CancellationToken.None);
                throw new ApiException(message);
            }
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<DnsChallengeDto> PrepareDnsChallengeAsync(int id, CancellationToken ct = default)
    {
        await OperationLock.WaitAsync(ct);
        try
        {
            var record = await GetRecordAsync(id, ct);
            if (record.Status == "issuing")
            {
                throw new ApiException("证书正在签发中，请稍后再试");
            }

            if (record.ChallengeType != "dns-01" && !record.DomainList.Any(IsWildcard))
            {
                throw new ApiException("该证书不需要 DNS-01 验证，请直接续期");
            }

            record.LastAttemptAt = DateTime.UtcNow;
            record.LastError = null;
            record.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await logs.InfoAsync(
                "issue",
                $"开始 DNS-01 验证: {record.PrimaryDomain}",
                record.Id,
                record.PrimaryDomain,
                ct: ct);

            var pending = await acme.PrepareDnsChallengeAsync(
                record,
                message => logs.InfoAsync("issue", message, record.Id, record.PrimaryDomain, ct: CancellationToken.None),
                ct);

            return new DnsChallengeDto
            {
                CertificateId = record.Id,
                OrderUrl = pending.OrderUrl,
                ExpiresAt = pending.ExpiresAt,
                Records = pending.Records.Select(x => new DnsChallengeRecordDto
                {
                    Domain = x.Domain,
                    Wildcard = x.Wildcard,
                    RecordName = x.RecordName,
                    RecordValue = x.RecordValue
                }).ToList()
            };
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<CertificateOperationResult> CompleteDnsChallengeAsync(int id, CancellationToken ct = default)
    {
        await OperationLock.WaitAsync(ct);
        try
        {
            var record = await GetRecordAsync(id, ct);
            if (record.ChallengeType != "dns-01" && !record.DomainList.Any(IsWildcard))
            {
                throw new ApiException("该证书不需要 DNS-01 验证");
            }

            var wasExisting = record.NotAfter is not null;
            record.Status = "issuing";
            record.LastAttemptAt = DateTime.UtcNow;
            record.LastError = null;
            record.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await logs.InfoAsync(
                wasExisting ? "renew" : "issue",
                $"开始 DNS-01 {(wasExisting ? "续期" : "签发")}证书: {record.PrimaryDomain}",
                record.Id,
                record.PrimaryDomain,
                ct: ct);

            try
            {
                var issued = await acme.CompleteDnsChallengeAsync(
                    record,
                    message => logs.InfoAsync("renew", message, record.Id, record.PrimaryDomain, ct: CancellationToken.None),
                    ct);

                var warning = await ApplyIssuedAsync(record, issued, wasExisting, ct);

                return new CertificateOperationResult
                {
                    Certificate = ToDto(record),
                    Message = "证书签发成功",
                    Warning = warning
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DNS-01 issuance failed for {Domain}", record.PrimaryDomain);
                var message = ex is ApiException ? ex.Message : $"签发失败: {ex.Message}";

                RefreshFromFile(record, preserveError: false);
                if (record.NotAfter is null || record.NotAfter <= DateTime.UtcNow)
                {
                    record.Status = "error";
                }
                else
                {
                    record.Status = CertificateInspector.ComputeStatus(record.NotAfter);
                }

                record.LastError = message;
                record.LastCheckedAt = DateTime.UtcNow;
                record.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
                await logs.ErrorAsync("renew", message, record.Id, record.PrimaryDomain, ex.ToString(), CancellationToken.None);
                throw new ApiException(message);
            }
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public void CancelDnsChallenge(int id) => dnsStore.Remove(id);

    public async Task<CertificateRecord> SyncAsync(int id, CancellationToken ct = default)
    {
        var record = await GetRecordAsync(id, ct);
        RefreshFromFile(record, preserveError: false);
        record.LastCheckedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return record;
    }

    public async Task<int> SyncAllAsync(CancellationToken ct = default)
    {
        var records = await db.Certificates.ToListAsync(ct);
        var changed = 0;
        foreach (var record in records)
        {
            if (record.Status == "issuing")
            {
                continue;
            }

            var previous = (record.Status, record.NotAfter, record.Thumbprint);
            RefreshFromFile(record, preserveError: true);
            record.LastCheckedAt = DateTime.UtcNow;
            if (previous != (record.Status, record.NotAfter, record.Thumbprint))
            {
                changed++;
            }
        }

        await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<ImportResultDto> ImportFromNginxAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var sites = await nginx.ScanAsync(ct);
        var existing = await db.Certificates.Select(x => x.CertPath).ToListAsync(ct);
        var existingSet = existing.Select(SafeFullPath).ToHashSet(StringComparer.Ordinal);

        var imported = 0;
        var skipped = 0;
        var domains = new List<string>();

        foreach (var group in sites
                     .Where(x => !string.IsNullOrWhiteSpace(x.CertificatePath) && !string.IsNullOrWhiteSpace(x.CertificateKeyPath))
                     .GroupBy(x => x.CertificatePath!, StringComparer.Ordinal))
        {
            var certPath = group.Key;
            if (existingSet.Contains(SafeFullPath(certPath)))
            {
                skipped++;
                continue;
            }

            var summary = inspector.TryRead(certPath);
            if (summary is null)
            {
                skipped++;
                continue;
            }

            var names = summary.DnsNames.Count > 0
                ? summary.DnsNames
                : group.SelectMany(x => x.ServerNames).Where(IsValidDomain).ToList();

            if (names.Count == 0)
            {
                skipped++;
                continue;
            }

            var primary = names[0].ToLowerInvariant();
            var keyPath = group.First().CertificateKeyPath!;

            var record = new CertificateRecord
            {
                Domains = string.Join(',', names.Select(x => x.ToLowerInvariant()).Distinct()),
                PrimaryDomain = primary,
                Email = settings.DefaultEmail,
                ChallengeType = "http-01",
                WebRootPath = settings.DefaultWebRoot,
                CertPath = certPath,
                KeyPath = keyPath,
                AutoRenew = false,
                AutoDeploy = true,
                Source = "imported",
                NotBefore = summary.NotBefore,
                NotAfter = summary.NotAfter,
                DaysRemaining = summary.DaysRemaining,
                Subject = summary.Subject,
                Issuer = summary.Issuer,
                Thumbprint = summary.Thumbprint,
                SerialNumber = summary.SerialNumber,
                Status = summary.Status,
                LastCheckedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Certificates.Add(record);
            existingSet.Add(SafeFullPath(certPath));
            imported++;
            domains.Add(primary);
            await logs.InfoAsync("import", $"从 nginx 配置导入证书: {primary}", null, primary, certPath, ct);
        }

        if (imported > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return new ImportResultDto
        {
            Imported = imported,
            Skipped = skipped,
            Domains = domains
        };
    }

    public async Task<List<CertificateRecord>> GetDueForRenewalAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        if (!settings.AutoRenewEnabled)
        {
            return [];
        }

        var limit = DateTime.UtcNow.AddDays(settings.RenewDaysBefore);
        return await db.Certificates
            .Where(x => x.AutoRenew && x.NotAfter != null && x.NotAfter <= limit && x.Status != "issuing")
            .OrderBy(x => x.NotAfter)
            .ToListAsync(ct);
    }

    public async Task<NginxDeployResult> DeployAsync(int id, DeployCertificateRequest request, CancellationToken ct = default)
    {
        await OperationLock.WaitAsync(ct);
        try
        {
            var record = await GetRecordAsync(id, ct);
            var result = await nginx.DeployCertificateAsync(
                record.Id,
                record.DomainList,
                record.CertPath,
                record.KeyPath,
                request.UpdateNginxConfig,
                request.Reload,
                ct);

            record.LastError = null;
            record.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            var record = await db.Certificates.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (record is not null)
            {
                record.LastError = ex.Message;
                record.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
        finally
        {
            OperationLock.Release();
        }
    }

    public async Task<CertificateRecord> GetRecordAsync(int id, CancellationToken ct = default) =>
        await db.Certificates.FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new NotFoundException("证书不存在");

    private async Task<string?> ApplyIssuedAsync(
        CertificateRecord record,
        IssuedCertificate issued,
        bool wasExisting,
        CancellationToken ct)
    {
        await SaveIssuedFilesAsync(record, issued, ct);

        var info = CertificateInspector.ReadPem(issued.CertificatePem);
        record.NotBefore = info.NotBefore;
        record.NotAfter = info.NotAfter;
        record.DaysRemaining = CertificateInspector.DaysRemaining(info.NotAfter);
        record.Subject = info.Subject;
        record.Issuer = info.Issuer;
        record.SerialNumber = info.SerialNumber;
        record.Thumbprint = info.Thumbprint;
        record.Status = CertificateInspector.ComputeStatus(info.NotAfter);
        record.Source = "letsencrypt";
        record.LastRenewedAt = DateTime.UtcNow;
        record.LastCheckedAt = DateTime.UtcNow;
        record.LastError = null;
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await logs.SuccessAsync(
            "renew",
            $"证书已{(wasExisting ? "续期" : "签发")}并保存: {record.PrimaryDomain}",
            record.Id,
            record.PrimaryDomain,
            $"有效期至 {record.NotAfter:yyyy-MM-dd HH:mm} UTC，订单 {issued.OrderUrl}",
            ct);

        string? warning = null;
        if (record.AutoDeploy)
        {
            try
            {
                var deploy = await nginx.DeployCertificateAsync(
                    record.Id,
                    record.DomainList,
                    record.CertPath,
                    record.KeyPath,
                    updateConfig: true,
                    reload: true,
                    ct);

                await logs.SuccessAsync(
                    "deploy",
                    $"已应用证书到 nginx: {record.PrimaryDomain}",
                    record.Id,
                    record.PrimaryDomain,
                    deploy.Output,
                    ct);
            }
            catch (Exception ex)
            {
                warning = $"证书已更新，但部署到 nginx 失败: {ex.Message}";
                record.LastError = warning;
                record.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await logs.ErrorAsync("deploy", warning, record.Id, record.PrimaryDomain, ex.ToString(), ct);
            }
        }

        return warning;
    }

    private async Task SaveIssuedFilesAsync(CertificateRecord record, IssuedCertificate issued, CancellationToken ct)
    {
        var certDir = Path.GetDirectoryName(record.CertPath)!;
        var keyDir = Path.GetDirectoryName(record.KeyPath)!;
        Directory.CreateDirectory(certDir);
        Directory.CreateDirectory(keyDir);

        byte[]? oldCert = File.Exists(record.CertPath) ? await File.ReadAllBytesAsync(record.CertPath, ct) : null;
        byte[]? oldKey = File.Exists(record.KeyPath) ? await File.ReadAllBytesAsync(record.KeyPath, ct) : null;

        if (oldCert is not null)
        {
            await SaveBackupAsync(record.CertPath, oldCert, ct);
        }

        if (oldKey is not null)
        {
            await SaveBackupAsync(record.KeyPath, oldKey, ct);
        }

        try
        {
            await File.WriteAllTextAsync(record.CertPath, issued.CertificatePem, new UTF8Encoding(false), ct);
            await File.WriteAllTextAsync(record.KeyPath, issued.PrivateKeyPem, new UTF8Encoding(false), ct);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(record.CertPath, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                File.SetUnixFileMode(record.KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            if (oldCert is not null)
            {
                await File.WriteAllBytesAsync(record.CertPath, oldCert, ct);
            }

            if (oldKey is not null)
            {
                await File.WriteAllBytesAsync(record.KeyPath, oldKey, ct);
            }

            throw;
        }
    }

    private async Task SaveBackupAsync(string file, byte[] content, CancellationToken ct)
    {
        try
        {
            var name = Regex.Replace(file.TrimStart('/').Replace('/', '_'), @"[^a-zA-Z0-9_.-]", "_");
            var backup = Path.Combine(paths.BackupDirectory, $"{name}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
            await File.WriteAllBytesAsync(backup, content, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to backup {File}", file);
        }
    }

    private void RefreshFromFile(CertificateRecord record, bool preserveError)
    {
        var existingError = preserveError ? record.LastError : null;

        if (!File.Exists(record.CertPath))
        {
            record.Status = "pending";
            record.NotBefore = null;
            record.NotAfter = null;
            record.DaysRemaining = null;
            record.LastError = existingError;
            return;
        }

        try
        {
            var summary = inspector.TryRead(record.CertPath);
            if (summary is null)
            {
                record.Status = "error";
                record.LastError = "证书文件无法解析";
                return;
            }

            record.NotBefore = summary.NotBefore;
            record.NotAfter = summary.NotAfter;
            record.DaysRemaining = summary.DaysRemaining;
            record.Subject = summary.Subject;
            record.Issuer = summary.Issuer;
            record.Thumbprint = summary.Thumbprint;
            record.SerialNumber = summary.SerialNumber;
            record.Status = summary.Status;
            record.LastError = existingError;
        }
        catch (Exception ex)
        {
            record.Status = "error";
            record.LastError = ex.Message;
        }
    }

    private static List<string> NormalizeDomains(IEnumerable<string>? input)
    {
        if (input is null)
        {
            throw new ApiException("请填写域名");
        }

        var domains = input
            .SelectMany(x => x.Split([',', ' ', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim().ToLowerInvariant().TrimEnd('.'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (domains.Count == 0)
        {
            throw new ApiException("请至少填写一个域名");
        }

        foreach (var domain in domains)
        {
            if (!DomainRegex.IsMatch(domain))
            {
                throw new ApiException($"域名格式不正确: {domain}");
            }
        }

        return domains;
    }

    private static string ResolveChallengeType(string? requested, bool hasWildcard)
    {
        var value = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim().ToLowerInvariant();
        if (value is not null && value is not ("http-01" or "dns-01"))
        {
            throw new ApiException("仅支持 HTTP-01 或 DNS-01 验证方式");
        }

        return hasWildcard ? "dns-01" : value ?? "http-01";
    }

    private static bool IsWildcard(string domain) =>
        domain.StartsWith("*.", StringComparison.Ordinal);

    private static bool IsValidDomain(string value) => DomainRegex.IsMatch(value.Trim().TrimEnd('.'));

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}

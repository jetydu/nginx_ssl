using System.Security.Cryptography;
using System.Text;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Directory = System.IO.Directory;
using NginxSslManager.Api.Common;
using NginxSslManager.Api.Models;
using NginxSslManager.Api.Services.Nginx;

namespace NginxSslManager.Api.Services;

public sealed class IssuedCertificate
{
    public required string CertificatePem { get; init; }
    public required string PrivateKeyPem { get; init; }
    public required string DirectoryUrl { get; init; }
    public string? OrderUrl { get; init; }
    public DateTime? NotAfter { get; init; }
}

/// <summary>
/// Thin wrapper around Certes that implements the ACME new-order flow with
/// HTTP-01 (automatic) and DNS-01 (manual TXT records) validation.
/// </summary>
public class AcmeService(
    AppPaths paths,
    SettingsService settingsService,
    NginxConfigService nginx,
    ActivityLogService logs,
    DnsChallengeStore dnsStore,
    ILogger<AcmeService> logger)
{
    public async Task<IssuedCertificate> IssueAsync(
        CertificateRecord certificate,
        string webRoot,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        var target = await ResolveTargetAsync(certificate, ct);
        var domains = target.Domains;

        webRoot = PathGuard.RequireAbsolutePath(webRoot, "Webroot");
        var challengeDirectory = Path.Combine(webRoot, ".well-known", "acme-challenge");
        Directory.CreateDirectory(challengeDirectory);

        await Report(progress, $"连接 ACME 目录 {target.Directory}");
        var acme = await CreateAccountContextAsync(target.DirectoryUri, target.Email, progress, ct);

        await Report(progress, $"为 {string.Join(", ", domains)} 创建证书订单");
        IOrderContext order;
        try
        {
            order = await acme.NewOrder(domains);
        }
        catch (Exception ex) when (ex is AcmeRequestException or AcmeException)
        {
            throw new ApiException($"创建 ACME 订单失败: {ExtractAcmeError(ex)}");
        }

        var authorizations = await order.Authorizations();
        var pending = new List<PendingChallenge>();
        var challengeFiles = new List<string>();

        foreach (var authorization in authorizations)
        {
            var resource = await authorization.Resource();
            var identifier = resource.Identifier?.Value ?? "unknown";

            if (resource.Status == AuthorizationStatus.Valid)
            {
                await Report(progress, $"{DisplayName(identifier, resource.Wildcard)} 的授权仍然有效，跳过验证");
                continue;
            }

            var httpChallenge = await authorization.Http();
            if (httpChallenge is null)
            {
                throw new ApiException($"{identifier} 没有可用的 HTTP-01 验证方式");
            }

            pending.Add(new PendingChallenge
            {
                Domain = DisplayName(identifier, resource.Wildcard),
                Authorization = authorization,
                Challenge = httpChallenge,
                Token = httpChallenge.Token,
                KeyAuthz = httpChallenge.KeyAuthz
            });
        }

        if (pending.Count == 0)
        {
            await Report(progress, "所有域名授权均已有效");
        }
        else
        {
            foreach (var item in pending)
            {
                if (string.IsNullOrWhiteSpace(item.Token) ||
                    item.Token.Contains('/') ||
                    item.Token.Contains('\\') ||
                    item.Token.Contains("..", StringComparison.Ordinal))
                {
                    throw new ApiException($"ACME 返回了非法 token: {item.Token}");
                }

                var file = Path.Combine(challengeDirectory, item.Token);
                await File.WriteAllTextAsync(file, item.KeyAuthz, new UTF8Encoding(false), ct);
                challengeFiles.Add(file);
            }

            await using var session = await nginx.PrepareChallengeAsync(domains, webRoot, ct);
            try
            {
                foreach (var item in pending)
                {
                    await Report(progress, $"请求 Let's Encrypt 验证 {item.Domain}");
                    await item.Challenge.Validate();
                }

                foreach (var item in pending)
                {
                    await WaitForAuthorizationAsync(item, progress, ct, dns: false);
                }
            }
            finally
            {
                foreach (var file in challengeFiles)
                {
                    try
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete challenge file {File}", file);
                    }
                }
            }
        }

        return await FinalizeOrderAsync(certificate, order, target, progress, ct);
    }

    /// <summary>
    /// Creates an ACME order and returns the DNS TXT records the user has to publish.
    /// The order is kept in memory until <see cref="CompleteDnsChallengeAsync"/> is called.
    /// </summary>
    public async Task<PendingDnsOrder> PrepareDnsChallengeAsync(
        CertificateRecord certificate,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        var target = await ResolveTargetAsync(certificate, ct);

        await Report(progress, $"连接 ACME 目录 {target.Directory}");
        var acme = await CreateAccountContextAsync(target.DirectoryUri, target.Email, progress, ct);

        await Report(progress, $"为 {string.Join(", ", target.Domains)} 创建证书订单");
        IOrderContext order;
        try
        {
            order = await acme.NewOrder(target.Domains);
        }
        catch (Exception ex) when (ex is AcmeRequestException or AcmeException)
        {
            throw new ApiException($"创建 ACME 订单失败: {ExtractAcmeError(ex)}");
        }

        var orderUrl = order.Location?.ToString();
        if (string.IsNullOrWhiteSpace(orderUrl))
        {
            throw new ApiException("ACME 没有返回订单地址，无法继续 DNS 验证");
        }

        var records = await BuildDnsRecordsAsync(order, acme, progress, ct);
        var pending = dnsStore.Set(certificate.Id, orderUrl, records);

        if (records.Count == 0)
        {
            await Report(progress, "所有域名授权均已有效，可直接完成签发");
        }
        else
        {
            await Report(progress, "订单已创建，请到 DNS 服务商添加 TXT 记录后继续验证");
        }

        return pending;
    }

    /// <summary>
    /// Validates the DNS-01 challenges of a prepared order and finalizes it.
    /// </summary>
    public async Task<IssuedCertificate> CompleteDnsChallengeAsync(
        CertificateRecord certificate,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        var pending = dnsStore.Get(certificate.Id)
            ?? throw new ApiException("没有待完成的 DNS 验证，请重新发起申请");

        try
        {
            var target = await ResolveTargetAsync(certificate, ct);
            await Report(progress, $"连接 ACME 目录 {target.Directory}");
            var acme = await CreateAccountContextAsync(target.DirectoryUri, target.Email, progress, ct);

            IOrderContext order;
            try
            {
                order = acme.Order(new Uri(pending.OrderUrl));
            }
            catch (Exception ex) when (ex is AcmeRequestException or AcmeException)
            {
                throw new ApiException($"读取 ACME 订单失败: {ExtractAcmeError(ex)}");
            }

            var authorizations = await order.Authorizations();
            var pendingChallenges = new List<PendingChallenge>();

            foreach (var authorization in authorizations)
            {
                var resource = await authorization.Resource();
                var identifier = resource.Identifier?.Value ?? "unknown";

                if (resource.Status == AuthorizationStatus.Valid)
                {
                    await Report(progress, $"{DisplayName(identifier, resource.Wildcard)} 的授权仍然有效，跳过验证");
                    continue;
                }

                var dnsChallenge = await authorization.Dns();
                if (dnsChallenge is null)
                {
                    throw new ApiException($"{identifier} 没有可用的 DNS-01 验证方式");
                }

                pendingChallenges.Add(new PendingChallenge
                {
                    Domain = DisplayName(identifier, resource.Wildcard),
                    Authorization = authorization,
                    Challenge = dnsChallenge
                });
            }

            foreach (var item in pendingChallenges)
            {
                await Report(progress, $"请求 Let's Encrypt 验证 {item.Domain}");
                await item.Challenge.Validate();
            }

            foreach (var item in pendingChallenges)
            {
                await WaitForAuthorizationAsync(item, progress, ct, dns: true);
            }

            var issued = await FinalizeOrderAsync(certificate, order, target, progress, ct);
            dnsStore.Remove(certificate.Id);
            return issued;
        }
        catch
        {
            dnsStore.Remove(certificate.Id);
            throw;
        }
    }

    private async Task<List<DnsTxtRecord>> BuildDnsRecordsAsync(
        IOrderContext order,
        AcmeContext acme,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        var records = new List<DnsTxtRecord>();
        var authorizations = await order.Authorizations();

        foreach (var authorization in authorizations)
        {
            var resource = await authorization.Resource();
            var identifier = resource.Identifier?.Value ?? "unknown";

            if (resource.Status == AuthorizationStatus.Valid)
            {
                await Report(progress, $"{DisplayName(identifier, resource.Wildcard)} 的授权仍然有效，跳过验证");
                continue;
            }

            var dnsChallenge = await authorization.Dns();
            if (dnsChallenge is null || string.IsNullOrWhiteSpace(dnsChallenge.Token))
            {
                throw new ApiException($"{identifier} 没有可用的 DNS-01 验证方式");
            }

            records.Add(new DnsTxtRecord
            {
                Domain = identifier,
                Wildcard = resource.Wildcard ?? false,
                RecordName = $"_acme-challenge.{identifier}",
                RecordValue = acme.AccountKey.DnsTxt(dnsChallenge.Token)
            });
        }

        return records;
    }

    private async Task<IssuedCertificate> FinalizeOrderAsync(
        CertificateRecord certificate,
        IOrderContext order,
        AcmeTarget target,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        await Report(progress, "验证通过，正在签发生成证书");
        var certKey = KeyFactory.NewKey(KeyAlgorithm.RS256);
        CertificateChain chain;
        try
        {
            var primary = target.Domains[0];
            var csr = new CsrInfo
            {
                CommonName = primary.Length <= 64 ? primary : null
            };
            chain = await order.Generate(csr, certKey);
        }
        catch (Exception ex) when (ex is AcmeRequestException or AcmeException)
        {
            throw new ApiException($"签发证书失败: {ExtractAcmeError(ex)}");
        }

        var pem = chain.ToPem();
        var keyPem = certKey.ToPem();

        DateTime? notAfter = null;
        try
        {
            var info = CertificateInspector.ReadPem(pem);
            notAfter = info.NotAfter;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect newly issued certificate");
        }

        await logs.SuccessAsync(
            certificate.Id > 0 ? "renew" : "issue",
            $"证书签发成功: {certificate.PrimaryDomain}",
            certificate.Id > 0 ? certificate.Id : null,
            certificate.PrimaryDomain,
            $"ACME: {target.Directory}",
            ct);

        return new IssuedCertificate
        {
            CertificatePem = pem,
            PrivateKeyPem = keyPem,
            DirectoryUrl = target.Directory,
            OrderUrl = order.Location?.ToString(),
            NotAfter = notAfter
        };
    }

    private async Task<AcmeTarget> ResolveTargetAsync(CertificateRecord certificate, CancellationToken ct)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var domains = certificate.DomainList.ToList();
        if (domains.Count == 0)
        {
            throw new ApiException("至少需要配置一个域名");
        }

        var email = string.IsNullOrWhiteSpace(certificate.Email) ? settings.DefaultEmail : certificate.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ApiException("请先在设置中配置 ACME 联系邮箱，或为证书填写邮箱");
        }

        var directory = string.IsNullOrWhiteSpace(certificate.AcmeDirectory)
            ? settings.AcmeDirectory
            : certificate.AcmeDirectory;

        if (!Uri.TryCreate(directory, UriKind.Absolute, out var directoryUri) || directoryUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ApiException("ACME 目录地址无效");
        }

        return new AcmeTarget(directory, directoryUri, email, domains);
    }

    private async Task<AcmeContext> CreateAccountContextAsync(
        Uri directoryUri,
        string email,
        Func<string, Task>? progress,
        CancellationToken ct)
    {
        var keyPath = AccountKeyPath(directoryUri);
        if (File.Exists(keyPath))
        {
            try
            {
                var existingKey = KeyFactory.FromPem(await File.ReadAllTextAsync(keyPath, ct));
                var existing = new AcmeContext(directoryUri, existingKey);
                await existing.Account();
                return existing;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Stored ACME account key could not be used; a new account will be created");
            }
        }

        await Report(progress, "注册 ACME 账户");
        var acme = new AcmeContext(directoryUri);
        try
        {
            await acme.NewAccount(email, true);
        }
        catch (AcmeRequestException ex)
        {
            throw new ApiException($"注册 ACME 账户失败: {ex.Error?.Detail ?? ex.Message}");
        }

        await File.WriteAllTextAsync(keyPath, acme.AccountKey.ToPem(), new UTF8Encoding(false), ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return acme;
    }

    private string AccountKeyPath(Uri directoryUri)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directoryUri.ToString())))[..24];
        return Path.Combine(paths.AcmeAccountDirectory, $"account-{hash}.pem");
    }

    private async Task WaitForAuthorizationAsync(
        PendingChallenge item,
        Func<string, Task>? progress,
        CancellationToken ct,
        bool dns)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var resource = await item.Authorization.Resource();
            if (resource.Status == AuthorizationStatus.Valid)
            {
                await Report(progress, $"{item.Domain} 验证通过");
                return;
            }

            if (resource.Status is AuthorizationStatus.Invalid or AuthorizationStatus.Deactivated or
                AuthorizationStatus.Expired or AuthorizationStatus.Revoked)
            {
                var challenge = await item.Challenge.Resource();
                var detail = challenge.Error?.Detail;
                if (string.IsNullOrWhiteSpace(detail))
                {
                    detail = challenge.Error?.Type;
                }

                var hint = dns
                    ? "请确认 _acme-challenge TXT 记录已添加并已生效（DNS 传播可能需要几分钟）。"
                    : "请确认域名已解析到本机、80 端口可从公网访问。";

                throw new ApiException(
                    $"{item.Domain} 验证失败{(string.IsNullOrWhiteSpace(detail) ? "" : $": {detail}")}。{hint}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        throw new ApiException($"{item.Domain} 验证超时，请稍后重试");
    }

    private static string DisplayName(string identifier, bool? wildcard) =>
        wildcard == true ? $"*.{identifier}" : identifier;

    private static async Task Report(Func<string, Task>? progress, string message)
    {
        if (progress is not null)
        {
            await progress(message);
        }
    }

    private static string ExtractAcmeError(Exception ex) => ex switch
    {
        AcmeRequestException request when request.Error is not null =>
            string.IsNullOrWhiteSpace(request.Error.Detail) ? request.Error.Type ?? request.Message : request.Error.Detail,
        _ => ex.Message
    };

    private sealed record AcmeTarget(string Directory, Uri DirectoryUri, string Email, List<string> Domains);

    private sealed class PendingChallenge
    {
        public string Domain { get; init; } = "";
        public required IAuthorizationContext Authorization { get; init; }
        public required IChallengeContext Challenge { get; init; }
        public string? Token { get; init; }
        public string KeyAuthz { get; init; } = "";
    }
}

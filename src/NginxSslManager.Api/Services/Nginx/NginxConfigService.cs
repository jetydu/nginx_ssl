using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Common;
using NginxSslManager.Api.Data;

namespace NginxSslManager.Api.Services.Nginx;

public sealed class NginxProcessResult
{
    public bool Success { get; init; }
    public string Output { get; init; } = "";
}

public sealed class NginxStatus
{
    public bool Running { get; init; }
    public bool ConfigOk { get; init; }
    public string Message { get; init; } = "";
    public string Binary { get; init; } = "";
    public string ConfigPath { get; init; } = "";
    public string? Version { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

public sealed class NginxDeployResult
{
    public bool ConfigUpdated { get; init; }
    public bool Reloaded { get; init; }
    public List<string> UpdatedFiles { get; init; } = [];
    public string Message { get; init; } = "";
    public string? Output { get; init; }
}

public class NginxConfigService(
    AppDbContext db,
    SettingsService settingsService,
    NginxConfigLoader loader,
    CertificateInspector inspector,
    ActivityLogService logs,
    AppPaths paths,
    ILogger<NginxConfigService> logger)
{

    public async Task<List<NginxSiteInfo>> ScanAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var tree = await loader.LoadTreeAsync(settings.NginxConfigPath, settings.NginxPrefix, ct);
        var sites = loader.ExtractSites(tree, settings.NginxPrefix);

        var records = await db.Certificates.AsNoTracking().ToListAsync(ct);

        foreach (var site in sites)
        {
            var record = records.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(site.CertificatePath) &&
                string.Equals(Path.GetFullPath(x.CertPath), Path.GetFullPath(site.CertificatePath), StringComparison.Ordinal));

            if (record is not null)
            {
                site.CertificateId = record.Id;
                site.Certificate = new CertificateSummary
                {
                    Path = record.CertPath,
                    Subject = record.Subject ?? "",
                    Issuer = record.Issuer ?? "",
                    NotAfter = record.NotAfter,
                    NotBefore = record.NotBefore,
                    DaysRemaining = record.DaysRemaining,
                    Status = record.Status,
                    Thumbprint = record.Thumbprint,
                    SerialNumber = record.SerialNumber
                };
                continue;
            }

            if (!string.IsNullOrWhiteSpace(site.CertificatePath))
            {
                var summary = inspector.TryRead(site.CertificatePath);
                if (summary is not null)
                {
                    site.Certificate = summary;
                }
                else if (File.Exists(site.CertificatePath))
                {
                    site.Error = "证书文件存在但无法解析";
                }
            }
        }

        return sites;
    }

    public async Task<string> ReadConfigAsync(string? path, CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var target = string.IsNullOrWhiteSpace(path) ? settings.NginxConfigPath : Path.GetFullPath(path);
        var prefix = Path.GetFullPath(settings.NginxPrefix);

        if (!string.Equals(target, Path.GetFullPath(settings.NginxConfigPath), StringComparison.Ordinal) &&
            !target.StartsWith(prefix + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ApiException("只允许读取 nginx 配置目录内的文件");
        }

        if (!File.Exists(target))
        {
            throw new NotFoundException($"配置文件不存在: {target}");
        }

        var info = new FileInfo(target);
        if (info.Length > 1024 * 1024)
        {
            throw new ApiException("配置文件过大，无法预览");
        }

        return await File.ReadAllTextAsync(target, ct);
    }

    public async Task<NginxProcessResult> TestAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var result = await RunNginxAsync(settings.NginxBinary, ["-t", "-c", settings.NginxConfigPath], TimeSpan.FromSeconds(30), ct);
        return result;
    }

    public async Task<NginxProcessResult> ReloadAsync(bool testFirst = true, CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);

        if (testFirst && settings.NginxTestEnabled)
        {
            var test = await TestAsync(ct);
            if (!test.Success)
            {
                throw new NginxException("nginx 配置检查未通过，已取消重载", test.Output);
            }
        }

        var command = string.IsNullOrWhiteSpace(settings.NginxReloadCommand)
            ? $"{settings.NginxBinary} -s reload"
            : settings.NginxReloadCommand
                .Replace("{binary}", settings.NginxBinary, StringComparison.Ordinal)
                .Replace("{config}", settings.NginxConfigPath, StringComparison.Ordinal);

        var parts = SplitCommandLine(command);
        if (parts.Count == 0)
        {
            throw new ApiException("nginx 重载命令为空");
        }

        var result = await RunNginxAsync(parts[0], parts.Skip(1), TimeSpan.FromSeconds(30), ct);
        await logs.WriteAsync(
            result.Success ? "success" : "error",
            "nginx",
            result.Success ? "nginx 已重新加载" : "nginx 重载失败",
            details: Trim(result.Output),
            ct: ct);

        return result;
    }

    public async Task<NginxStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var running = IsNginxRunning();

        var test = await TestAsync(ct);
        var versionResult = await RunNginxAsync(settings.NginxBinary, ["-v"], TimeSpan.FromSeconds(10), ct);

        return new NginxStatus
        {
            Running = running,
            ConfigOk = test.Success,
            Message = Trim(test.Output),
            Binary = settings.NginxBinary,
            ConfigPath = settings.NginxConfigPath,
            Version = string.IsNullOrWhiteSpace(versionResult.Output) ? null : versionResult.Output.Trim()
        };
    }

    public async Task<NginxDeployResult> DeployCertificateAsync(
        int certificateId,
        IReadOnlyList<string> domains,
        string certificatePath,
        string keyPath,
        bool updateConfig,
        bool reload,
        CancellationToken ct = default)
    {
        if (!File.Exists(certificatePath))
        {
            throw new ApiException($"证书文件不存在: {certificatePath}");
        }

        if (!File.Exists(keyPath))
        {
            throw new ApiException($"私钥文件不存在: {keyPath}");
        }

        var sites = await ScanAsync(ct);
        var targets = sites
            .Where(site => site.HasOwnCertificate && site.HasSsl)
            .Where(site => site.ServerNames.Any(name => MatchesAnyDomain(name, domains)))
            .GroupBy(site => site.ConfigFile, StringComparer.Ordinal)
            .Select(group => group.ToList())
            .ToList();

        var edits = new List<FileEdit>();
        var updatedFiles = new HashSet<string>(StringComparer.Ordinal);

        if (updateConfig)
        {
            foreach (var fileSites in targets)
            {
                var edit = await PrepareFileEditAsync(fileSites[0].ConfigFile, ct);
                var changed = false;
                var lines = edit.OriginalContent.Split('\n');

                foreach (var site in fileSites)
                {
                    if (site.CertificateLine is int certLine &&
                        !string.IsNullOrWhiteSpace(site.CertificateRaw) &&
                        !PathsEqual(site.CertificatePath, certificatePath))
                    {
                        if (ApplyLineEdit(lines, certLine, site.CertificateRaw, certificatePath))
                        {
                            changed = true;
                        }
                    }

                    if (site.CertificateKeyLine is int keyLine &&
                        !string.IsNullOrWhiteSpace(site.CertificateKeyRaw) &&
                        !PathsEqual(site.CertificateKeyPath, keyPath))
                    {
                        if (ApplyLineEdit(lines, keyLine, site.CertificateKeyRaw, keyPath))
                        {
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    edit.NewContent = string.Join('\n', lines);
                    edits.Add(edit);
                    updatedFiles.Add(edit.File);
                }
            }
        }

        try
        {
            foreach (var edit in edits)
            {
                await File.WriteAllTextAsync(edit.File, edit.NewContent!, ct);
            }

            if (edits.Count > 0)
            {
                var test = await TestAsync(ct);
                if (!test.Success)
                {
                    await RestoreEditsAsync(edits, ct);
                    throw new NginxException("更新 nginx 配置后检查失败，已自动回滚", test.Output);
                }
            }

            var reloaded = false;
            string? reloadOutput = null;
            if (reload)
            {
                var result = await ReloadAsync(testFirst: edits.Count == 0, ct);
                if (!result.Success)
                {
                    await RestoreEditsAsync(edits, ct);
                    throw new NginxException("nginx 重载失败，配置已回滚", result.Output);
                }

                reloaded = true;
                reloadOutput = result.Output;
            }

            foreach (var edit in edits)
            {
                await SaveBackupAsync(edit, ct);
            }

            var message = edits.Count == 0
                ? (targets.Count == 0 ? "未找到匹配的 nginx 站点，未修改配置" : "证书路径未变化，无需修改配置")
                : $"已更新 {edits.Count} 个 nginx 配置文件";

            await logs.WriteAsync(
                "success",
                "deploy",
                message,
                certificateId,
                domains.FirstOrDefault(),
                details: Trim(reloadOutput),
                ct: ct);

            return new NginxDeployResult
            {
                ConfigUpdated = edits.Count > 0,
                Reloaded = reloaded,
                UpdatedFiles = updatedFiles.ToList(),
                Message = message,
                Output = Trim(reloadOutput)
            };
        }
        catch (NginxException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RestoreEditsAsync(edits, ct);
            throw new ApiException($"部署证书到 nginx 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// Injects a temporary ACME challenge location into matching port 80 server
    /// blocks (or creates a temporary server block), reloads nginx, and restores
    /// everything when the returned session is disposed.
    /// </summary>
    public async Task<NginxChallengeSession> PrepareChallengeAsync(
        IReadOnlyList<string> domains,
        string webRoot,
        CancellationToken ct = default)
    {
        var settings = await settingsService.GetAppSettingsAsync(ct);
        webRoot = PathGuard.RequireAbsolutePath(webRoot, "Webroot");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(Path.Combine(webRoot, ".well-known", "acme-challenge"));

        if (string.Equals(settings.ChallengeMode, "webroot", StringComparison.OrdinalIgnoreCase))
        {
            return new NginxChallengeSession(this, [], null, false);
        }

        var sites = await ScanAsync(ct);
        var httpSites = sites.Where(x => x.ListensOn80).ToList();
        var modified = new Dictionary<string, string>(StringComparer.Ordinal);
        var injected = new HashSet<(string File, int Offset)>();
        var servedDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var domain in domains)
        {
            var matches = httpSites
                .Where(site => site.ServerNames.Any(name => ServerNameMatches(name, domain)))
                .ToList();

            if (matches.Count == 0)
            {
                continue;
            }

            servedDomains.Add(domain);
            foreach (var site in matches)
            {
                if (site.HasAcmeLocation)
                {
                    continue;
                }

                if (!injected.Add((site.ConfigFile, site.OpenBraceOffset)))
                {
                    continue;
                }

                if (!modified.ContainsKey(site.ConfigFile))
                {
                    modified[site.ConfigFile] = await File.ReadAllTextAsync(site.ConfigFile, ct);
                }

                var snippet = BuildChallengeLocation(webRoot);
                var original = modified[site.ConfigFile];
                modified[site.ConfigFile] = original.Insert(site.OpenBraceOffset + 1, snippet);
            }
        }

        string? tempFile = null;
        var missingDomains = domains.Where(x => !servedDomains.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missingDomains.Count > 0)
        {
            var configDir = Directory.Exists(Path.Combine(settings.NginxPrefix, "conf.d"))
                ? Path.Combine(settings.NginxPrefix, "conf.d")
                : Path.GetDirectoryName(settings.NginxConfigPath)!;
            tempFile = Path.Combine(configDir, $"zz-ssl-manager-challenge-{Guid.NewGuid():N}.conf");

            var builder = new StringBuilder();
            builder.AppendLine("# Temporary file created by nginx-ssl-manager for ACME HTTP-01 validation.");
            builder.AppendLine("server {");
            builder.AppendLine("    listen 80;");
            builder.AppendLine($"    server_name {string.Join(' ', missingDomains)};");
            builder.AppendLine(BuildChallengeLocation(webRoot, indent: "    "));
            builder.AppendLine("}");
            await File.WriteAllTextAsync(tempFile, builder.ToString(), ct);
        }

        var session = new NginxChallengeSession(this, modified.ToList(), tempFile, modified.Count > 0 || tempFile is not null);

        if (!session.HasChanges)
        {
            return session;
        }

        try
        {
            foreach (var (file, content) in modified)
            {
                await File.WriteAllTextAsync(file, content, ct);
            }

            var test = await TestAsync(ct);
            if (!test.Success)
            {
                await session.RestoreAsync(ct);
                throw new NginxException("为 ACME 验证添加临时 location 后 nginx 配置检查失败", test.Output);
            }

            var reload = await ReloadAsync(testFirst: false, ct);
            if (!reload.Success)
            {
                await session.RestoreAsync(ct);
                throw new NginxException("为 ACME 验证重载 nginx 失败", reload.Output);
            }

            await logs.InfoAsync("nginx", "已临时开放 ACME HTTP-01 验证路径", details: string.Join(", ", domains), ct: ct);
            return session;
        }
        catch
        {
            await session.RestoreAsync(ct);
            throw;
        }
    }

    internal async Task RestoreChallengeAsync(
        IReadOnlyList<KeyValuePair<string, string>> modified,
        string? tempFile,
        CancellationToken ct)
    {
        var changed = false;

        foreach (var (file, original) in modified)
        {
            try
            {
                if (File.Exists(file))
                {
                    await File.WriteAllTextAsync(file, original, ct);
                    changed = true;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to restore nginx config {File}", file);
            }
        }

        if (tempFile is not null && File.Exists(tempFile))
        {
            try
            {
                File.Delete(tempFile);
                changed = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete temporary nginx config {File}", tempFile);
            }
        }

        if (changed)
        {
            try
            {
                var test = await TestAsync(ct);
                if (test.Success)
                {
                    await ReloadAsync(testFirst: false, ct);
                }

                await logs.InfoAsync("nginx", "ACME 验证完成后已恢复 nginx 配置", ct: ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reload nginx after restoring challenge config");
            }
        }
    }

    private async Task<FileEdit> PrepareFileEditAsync(string file, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(file, ct);
        return new FileEdit { File = file, OriginalContent = content };
    }

    private static bool ApplyLineEdit(string[] lines, int lineNumber, string oldValue, string newValue)
    {
        var index = lineNumber - 1;
        if (index < 0 || index >= lines.Length)
        {
            return false;
        }

        var line = lines[index];
        var position = line.IndexOf(oldValue, StringComparison.Ordinal);
        if (position < 0)
        {
            return false;
        }

        lines[index] = string.Concat(line.AsSpan(0, position), newValue, line.AsSpan(position + oldValue.Length));
        return true;
    }

    private async Task RestoreEditsAsync(IEnumerable<FileEdit> edits, CancellationToken ct)
    {
        foreach (var edit in edits)
        {
            try
            {
                if (File.Exists(edit.File))
                {
                    await File.WriteAllTextAsync(edit.File, edit.OriginalContent, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to roll back nginx config {File}", edit.File);
            }
        }
    }

    private async Task SaveBackupAsync(FileEdit edit, CancellationToken ct)
    {
        try
        {
            var name = Regex.Replace(edit.File.TrimStart('/').Replace('/', '_'), @"[^a-zA-Z0-9_.-]", "_");
            var backup = Path.Combine(paths.BackupDirectory, $"{name}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.bak");
            await File.WriteAllTextAsync(backup, edit.OriginalContent, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save nginx config backup for {File}", edit.File);
        }
    }

    private static string BuildChallengeLocation(string webRoot, string indent = "    ") =>
        $"\n{indent}location ^~ /.well-known/acme-challenge/ {{\n" +
        $"{indent}    root \"{webRoot}\";\n" +
        $"{indent}    default_type text/plain;\n" +
        $"{indent}    try_files $uri =404;\n" +
        $"{indent}    access_log off;\n" +
        $"{indent}}}\n";

    private static bool ServerNameMatches(string serverName, string domain)
    {
        if (string.Equals(serverName, domain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (serverName.StartsWith("*.", StringComparison.Ordinal))
        {
            var suffix = serverName[1..];
            return domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                   domain.Count(c => c == '.') == serverName.Count(c => c == '.');
        }

        if (serverName.StartsWith('~') || serverName == "_")
        {
            return false;
        }

        return false;
    }

    private static bool MatchesAnyDomain(string serverName, IReadOnlyList<string> domains) =>
        domains.Any(domain => ServerNameMatches(serverName, domain));

    private static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.Ordinal);
        }
        catch
        {
            return string.Equals(first, second, StringComparison.Ordinal);
        }
    }

    private async Task<NginxProcessResult> RunNginxAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // ignored
                }

                return new NginxProcessResult
                {
                    Success = false,
                    Output = $"执行 {fileName} 超时"
                };
            }

            var output = string.Join('\n', new[] { await stdoutTask, await stderrTask }
                .Where(x => !string.IsNullOrWhiteSpace(x)))
                .Trim();

            return new NginxProcessResult
            {
                Success = process.ExitCode == 0,
                Output = output
            };
        }
        catch (Exception ex)
        {
            return new NginxProcessResult
            {
                Success = false,
                Output = $"无法执行 {fileName}: {ex.Message}"
            };
        }
    }

    private static bool IsNginxRunning()
    {
        foreach (var pidFile in new[] { "/run/nginx.pid", "/var/run/nginx.pid" })
        {
            try
            {
                if (!File.Exists(pidFile))
                {
                    continue;
                }

                var text = File.ReadAllText(pidFile).Trim();
                if (int.TryParse(text, out var pid) && Directory.Exists($"/proc/{pid}"))
                {
                    return true;
                }
            }
            catch
            {
                // try the next detection method
            }
        }

        try
        {
            return Process.GetProcessesByName("nginx").Length > 0 ||
                   Directory.EnumerateDirectories("/proc")
                       .Where(path => int.TryParse(Path.GetFileName(path), out _))
                       .Any(path =>
                       {
                           try
                           {
                               return File.ReadAllText(Path.Combine(path, "comm")).Trim() == "nginx";
                           }
                           catch
                           {
                               return false;
                           }
                       });
        }
        catch
        {
            return false;
        }
    }

    private static List<string> SplitCommandLine(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var quote = '\0';

        foreach (var ch in command)
        {
            if (quoted)
            {
                if (ch == quote)
                {
                    quoted = false;
                }
                else
                {
                    current.Append(ch);
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quoted = true;
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        value = value.Trim();
        return value.Length <= 4000 ? value : value[..4000];
    }

    private sealed class FileEdit
    {
        public string File { get; init; } = "";
        public string OriginalContent { get; init; } = "";
        public string? NewContent { get; set; }
    }
}

public sealed class NginxChallengeSession(
    NginxConfigService owner,
    IReadOnlyList<KeyValuePair<string, string>> modified,
    string? tempFile,
    bool hasChanges) : IAsyncDisposable
{
    private int _disposed;

    public bool HasChanges { get; } = hasChanges;

    public Task RestoreAsync(CancellationToken ct) =>
        owner.RestoreChallengeAsync(modified, tempFile, ct);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0 || !HasChanges)
        {
            return;
        }

        await owner.RestoreChallengeAsync(modified, tempFile, CancellationToken.None);
    }
}

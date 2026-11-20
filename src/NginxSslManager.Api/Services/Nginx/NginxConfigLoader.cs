using System.Text.RegularExpressions;
using NginxSslManager.Api.Common;
using NginxSslManager.Api.Services;

namespace NginxSslManager.Api.Services.Nginx;

public sealed class NginxSiteInfo
{
    public string ConfigFile { get; init; } = "";
    public int Line { get; init; }
    public int OpenBraceOffset { get; init; }
    public List<string> ServerNames { get; init; } = [];
    public List<string> Listens { get; init; } = [];
    public int[] Ports { get; init; } = [];
    public bool HasSsl { get; init; }
    public bool ListensOn80 => Ports.Contains(80);
    public bool ListensOn443 => Ports.Contains(443);
    public string? CertificatePath { get; init; }
    public string? CertificateKeyPath { get; init; }
    public string? CertificateRaw { get; init; }
    public string? CertificateKeyRaw { get; init; }
    public int? CertificateLine { get; init; }
    public int? CertificateKeyLine { get; init; }
    public bool HasOwnCertificate { get; init; }
    public bool HasAcmeLocation { get; init; }
    public bool DefaultServer { get; init; }
    public int? CertificateId { get; set; }
    public CertificateSummary? Certificate { get; set; }
    public string? Error { get; set; }
}

public sealed class CertificateSummary
{
    public string Path { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Issuer { get; init; } = "";
    public List<string> DnsNames { get; init; } = [];
    public DateTime? NotBefore { get; init; }
    public DateTime? NotAfter { get; init; }
    public int? DaysRemaining { get; init; }
    public string Status { get; init; } = "unknown";
    public string? Thumbprint { get; init; }
    public string? SerialNumber { get; init; }
}

/// <summary>
/// Reads nginx.conf including all <c>include</c> directives and extracts server blocks.
/// </summary>
public class NginxConfigLoader
{
    private static readonly Regex ListenPortRegex = new(@":(?<port>\d+)$", RegexOptions.Compiled);

    public async Task<List<NginxDirective>> LoadTreeAsync(
        string configPath,
        string prefix,
        CancellationToken ct = default)
    {
        if (!File.Exists(configPath))
        {
            throw new ApiException($"找不到 nginx 配置文件: {configPath}");
        }

        var root = await ParseAndExpandAsync(configPath, prefix, new HashSet<string>(StringComparer.Ordinal), ct);
        return root;
    }

    private async Task<List<NginxDirective>> ParseAndExpandAsync(
        string file,
        string prefix,
        HashSet<string> stack,
        CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(file);
        if (!File.Exists(fullPath) || stack.Contains(fullPath))
        {
            return [];
        }

        stack.Add(fullPath);
        try
        {
            string text;
            try
            {
                text = await File.ReadAllTextAsync(fullPath, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ApiException($"无法读取 nginx 配置 {fullPath}: {ex.Message}");
            }

            var nodes = NginxConfigParser.Parse(text, fullPath);
            var expanded = new List<NginxDirective>(nodes.Count);

            foreach (var node in nodes)
            {
                if (string.Equals(node.Name, "include", StringComparison.OrdinalIgnoreCase) && node.Args.Count > 0)
                {
                    foreach (var include in ResolveIncludes(node.Args[0], prefix))
                    {
                        var children = await ParseAndExpandAsync(include, prefix, stack, ct);
                        expanded.AddRange(children);
                    }

                    continue;
                }

                if (node.Children is not null)
                {
                    node.Children = await ExpandChildrenAsync(node.Children, prefix, stack, ct);
                }

                expanded.Add(node);
            }

            return expanded;
        }
        finally
        {
            stack.Remove(fullPath);
        }
    }

    private async Task<List<NginxDirective>> ExpandChildrenAsync(
        List<NginxDirective> children,
        string prefix,
        HashSet<string> stack,
        CancellationToken ct)
    {
        var result = new List<NginxDirective>(children.Count);
        foreach (var node in children)
        {
            if (string.Equals(node.Name, "include", StringComparison.OrdinalIgnoreCase) && node.Args.Count > 0)
            {
                foreach (var include in ResolveIncludes(node.Args[0], prefix))
                {
                    result.AddRange(await ParseAndExpandAsync(include, prefix, stack, ct));
                }

                continue;
            }

            if (node.Children is not null)
            {
                node.Children = await ExpandChildrenAsync(node.Children, prefix, stack, ct);
            }

            result.Add(node);
        }

        return result;
    }

    public List<NginxSiteInfo> ExtractSites(List<NginxDirective> tree, string prefix)
    {
        var http = tree.FirstOrDefault(x => string.Equals(x.Name, "http", StringComparison.OrdinalIgnoreCase));
        var scope = http?.Children ?? tree;
        var servers = scope.Where(x => string.Equals(x.Name, "server", StringComparison.OrdinalIgnoreCase)).ToList();

        // A site file could be loaded directly in tests or by a non standard entry point.
        if (servers.Count == 0)
        {
            servers = FindDirectives(tree, "server").ToList();
        }

        var inheritedCert = http?.Direct("ssl_certificate").FirstOrDefault()?.FirstArg;
        var inheritedKey = http?.Direct("ssl_certificate_key").FirstOrDefault()?.FirstArg;

        var result = new List<NginxSiteInfo>(servers.Count);
        foreach (var server in servers)
        {
            var ownCertNode = server.Direct("ssl_certificate").FirstOrDefault();
            var ownKeyNode = server.Direct("ssl_certificate_key").FirstOrDefault();
            var certArg = ownCertNode?.FirstArg ?? inheritedCert;
            var keyArg = ownKeyNode?.FirstArg ?? inheritedKey;

            var listens = server.Direct("listen")
                .Select(x => string.Join(' ', x.Args))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var ports = listens.SelectMany(ParseListenPorts).Distinct().OrderBy(x => x).ToArray();
            var sslKeyword = listens.Any(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part, "ssl", StringComparison.OrdinalIgnoreCase)));

            var names = server.Direct("server_name")
                .SelectMany(x => x.Args)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var acmeLocation = server.Direct("location")
                .Any(x => x.Args.Any(arg => arg.Contains(".well-known", StringComparison.OrdinalIgnoreCase)));

            var defaultServer = listens.Any(x => x.Contains("default_server", StringComparison.OrdinalIgnoreCase));
            var certPath = ResolveConfigPath(certArg, prefix);
            var keyPath = ResolveConfigPath(keyArg, prefix);

            result.Add(new NginxSiteInfo
            {
                ConfigFile = server.File,
                Line = server.Line,
                OpenBraceOffset = server.OpenBraceOffset,
                ServerNames = names,
                Listens = listens,
                Ports = ports,
                HasSsl = sslKeyword || ports.Contains(443) || certPath is not null,
                CertificatePath = certPath,
                CertificateKeyPath = keyPath,
                CertificateRaw = ownCertNode?.FirstArg,
                CertificateKeyRaw = ownKeyNode?.FirstArg,
                CertificateLine = ownCertNode?.Line,
                CertificateKeyLine = ownKeyNode?.Line,
                HasOwnCertificate = ownCertNode is not null,
                HasAcmeLocation = acmeLocation,
                DefaultServer = defaultServer
            });
        }

        return result;
    }

    public static string? ResolveConfigPath(string? path, string prefix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(prefix, path));
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static IEnumerable<NginxDirective> FindDirectives(IEnumerable<NginxDirective> nodes, string name)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                yield return node;
            }

            if (node.Children is not null)
            {
                foreach (var child in FindDirectives(node.Children, name))
                {
                    yield return child;
                }
            }
        }
    }

    private static IEnumerable<int> ParseListenPorts(string listen)
    {
        var first = listen.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(first))
        {
            yield break;
        }

        var match = ListenPortRegex.Match(first);
        if (match.Success && int.TryParse(match.Groups["port"].Value, out var port))
        {
            yield return port;
            yield break;
        }

        if (int.TryParse(first, out var barePort))
        {
            yield return barePort;
            yield break;
        }

        // listen 127.0.0.1; and listen [::]; default to 80, while listen ... ssl defaults to 443.
        if (first.Contains('.')) 
        {
            yield return listen.Contains("ssl", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        }
        else if (first.Contains(':'))
        {
            yield return listen.Contains("ssl", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        }
    }

    internal static List<string> ResolveIncludes(string pattern, string prefix)
    {
        var expanded = Path.IsPathRooted(pattern) ? pattern : Path.Combine(prefix, pattern);
        expanded = Path.GetFullPath(expanded);

        if (!expanded.Contains('*') && !expanded.Contains('?'))
        {
            return File.Exists(expanded) ? [expanded] : [];
        }

        var wildcardIndex = expanded.IndexOfAny(['*', '?']);
        var lastSeparator = expanded.LastIndexOf(Path.DirectorySeparatorChar, Math.Max(0, wildcardIndex));
        var baseDirectory = lastSeparator > 0 ? expanded[..lastSeparator] : prefix;

        if (!Directory.Exists(baseDirectory))
        {
            return [];
        }

        var regex = BuildGlobRegex(expanded);
        try
        {
            return Directory.EnumerateFiles(baseDirectory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .Where(path => regex.IsMatch(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Take(2000)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", @"[^/\\]*")
            .Replace(@"\?", @"[^/\\]");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

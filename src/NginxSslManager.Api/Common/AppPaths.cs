namespace NginxSslManager.Api.Common;

/// <summary>
/// Resolved directories used by the application for its own state.
/// </summary>
public sealed class AppPaths
{
    public required string DataDirectory { get; init; }
    public required string AcmeAccountDirectory { get; init; }
    public required string BackupDirectory { get; init; }
    public required string DatabasePath { get; init; }
    public required string JwtKeyPath { get; init; }

    public static AppPaths Create(string contentRoot, IConfiguration configuration)
    {
        var configured = configuration["Storage:DataDirectory"];
        var dataDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configured) ? "data" : configured,
            contentRoot);

        var paths = new AppPaths
        {
            DataDirectory = dataDirectory,
            AcmeAccountDirectory = Path.Combine(dataDirectory, "acme"),
            BackupDirectory = Path.Combine(dataDirectory, "backups"),
            DatabasePath = Path.Combine(dataDirectory, "nginx-ssl-manager.db"),
            JwtKeyPath = Path.Combine(dataDirectory, "jwt.key")
        };

        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.AcmeAccountDirectory);
        Directory.CreateDirectory(paths.BackupDirectory);
        return paths;
    }
}

using NginxSslManager.Api.Services;

namespace NginxSslManager.Api.Background;

public class RenewalWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RenewalWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromMinutes(60);

            try
            {
                interval = await RunCheckAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scheduled certificate check failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<TimeSpan> RunCheckAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var settings = await settingsService.GetAppSettingsAsync(ct);
        var manager = scope.ServiceProvider.GetRequiredService<CertificateManager>();
        var logs = scope.ServiceProvider.GetRequiredService<ActivityLogService>();
        var nginx = scope.ServiceProvider.GetRequiredService<Services.Nginx.NginxConfigService>();

        if (settings.AutoImport)
        {
            try
            {
                var imported = await manager.ImportFromNginxAsync(ct);
                if (imported.Imported > 0)
                {
                    await logs.InfoAsync("scan", $"自动从 nginx 配置导入 {imported.Imported} 张证书", details: string.Join(", ", imported.Domains), ct: ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auto import from nginx failed");
                await logs.WarningAsync("scan", $"自动扫描 nginx 配置失败: {ex.Message}", ct: ct);
            }
        }

        try
        {
            await manager.SyncAllAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Certificate metadata sync failed");
        }

        var due = await manager.GetDueForRenewalAsync(ct);
        foreach (var record in due)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            try
            {
                logger.LogInformation("Auto renewing {Domain} ({Days} days remaining)", record.PrimaryDomain, record.DaysRemaining);
                await manager.RenewAsync(record.Id, force: false, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto renewal failed for {Domain}", record.PrimaryDomain);
            }
        }

        // Refresh the nginx status cache at least once per cycle so broken configs
        // show up in the dashboard even when no certificate changed.
        try
        {
            await nginx.GetStatusAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Nginx status check failed");
        }

        return TimeSpan.FromMinutes(Math.Clamp(settings.CheckIntervalMinutes, 5, 10080));
    }
}

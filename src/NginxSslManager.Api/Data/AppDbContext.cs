using Microsoft.EntityFrameworkCore;
using NginxSslManager.Api.Models;

namespace NginxSslManager.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CertificateRecord> Certificates => Set<CertificateRecord>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CertificateRecord>(entity =>
        {
            entity.HasIndex(x => x.PrimaryDomain);
            entity.HasIndex(x => x.CertPath);
            entity.HasIndex(x => x.NotAfter);
            entity.Property(x => x.Domains).HasMaxLength(4000);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasIndex(x => x.Timestamp);
            entity.Property(x => x.Message).HasMaxLength(2000);
            entity.Property(x => x.Details).HasMaxLength(8000);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(x => x.Key);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.Value).HasMaxLength(8000);
        });
    }
}

// SPDX-FileCopyrightText: 2026 QSI-Banking
//
// SPDX-License-Identifier: GPL-3.0-only

using Microsoft.EntityFrameworkCore;

namespace Usbipd.Data;

/// <summary>
/// The usbipd database (SQL Server). Schema changes are EF Core migrations in Data\Migrations;
/// Database\usbipd.sql is the same as an idempotent script for DBAs.
/// </summary>
sealed class UsbipdDbContext(DbContextOptions<UsbipdDbContext> options) : DbContext(options)
{
    public const string Schema = "usbipd";

    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<AdminAuditEntry> AdminAudit => Set<AdminAuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.HasDefaultSchema(Schema);

        _ = modelBuilder.Entity<User>(entity =>
        {
            _ = entity.ToTable("Users");
            _ = entity.Property(u => u.Name).HasMaxLength(UserNames.MaxLength).IsRequired();
            // The default SQL Server collation is case-insensitive, matching UserNames.Comparer.
            _ = entity.HasIndex(u => u.Name).IsUnique();
            _ = entity.Property(u => u.CreatedUtc).HasColumnType("datetime2");
            _ = entity.Property(u => u.CreatedBy).HasMaxLength(256).IsRequired();
        });

        _ = modelBuilder.Entity<Device>(entity =>
        {
            _ = entity.ToTable("Devices");
            _ = entity.Property(d => d.ServerName).HasMaxLength(256).IsRequired();
            _ = entity.Property(d => d.InstanceId).HasMaxLength(400).IsRequired();
            _ = entity.Property(d => d.Description).HasMaxLength(400).IsRequired();
            _ = entity.HasIndex(d => new { d.ServerName, d.InstanceId }).IsUnique();
            _ = entity.Property(d => d.CreatedUtc).HasColumnType("datetime2");
        });

        _ = modelBuilder.Entity<UserDevice>(entity =>
        {
            _ = entity.ToTable("UserDevices");
            // One device per user.
            _ = entity.HasKey(m => m.UserId);
            _ = entity.HasOne(m => m.User).WithOne(u => u.UserDevice).HasForeignKey<UserDevice>(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity.HasOne(m => m.Device).WithMany().HasForeignKey(m => m.DeviceId).OnDelete(DeleteBehavior.Restrict);
            _ = entity.HasIndex(m => m.DeviceId);
            _ = entity.Property(m => m.CreatedUtc).HasColumnType("datetime2");
            _ = entity.Property(m => m.CreatedBy).HasMaxLength(256).IsRequired();
        });

        _ = modelBuilder.Entity<AdminAuditEntry>(entity =>
        {
            _ = entity.ToTable("AdminAudit");
            _ = entity.Property(a => a.TimestampUtc).HasColumnType("datetime2");
            _ = entity.Property(a => a.Account).HasMaxLength(256).IsRequired();
            _ = entity.Property(a => a.ServerName).HasMaxLength(256).IsRequired();
            _ = entity.Property(a => a.Action).HasMaxLength(64).IsRequired();
            _ = entity.Property(a => a.Target).HasMaxLength(400).IsRequired();
            _ = entity.HasIndex(a => a.TimestampUtc);
        });
    }
}

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

    public DbSet<Bind> Binds => Set<Bind>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<ClientCertificate> ClientCertificates => Set<ClientCertificate>();

    public DbSet<OtpSecret> OtpSecrets => Set<OtpSecret>();

    public DbSet<ServerRun> ServerRuns => Set<ServerRun>();
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

        _ = modelBuilder.Entity<Bind>(entity =>
        {
            _ = entity.ToTable("Binds");
            _ = entity.Property(b => b.Id).ValueGeneratedNever();
            // One bind per device.
            _ = entity.HasOne(b => b.Device).WithOne(d => d.Bind).HasForeignKey<Bind>(b => b.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
            _ = entity.Property(b => b.CreatedUtc).HasColumnType("datetime2");
            _ = entity.Property(b => b.CreatedBy).HasMaxLength(256).IsRequired();
            _ = entity.Property(b => b.Fingerprint).HasMaxLength(64).IsUnicode(false);
            _ = entity.Property(b => b.LocationPath).HasMaxLength(400);
            _ = entity.Property(b => b.HardwareId).HasMaxLength(9).IsUnicode(false);
            _ = entity.Property(b => b.SerialNumber).HasMaxLength(200);
            _ = entity.Property(b => b.Interfaces).HasMaxLength(400);
            _ = entity.Property(b => b.ConfirmedUtc).HasColumnType("datetime2");
            _ = entity.Property(b => b.ConfirmedBy).HasMaxLength(256);
        });

        _ = modelBuilder.Entity<ServerRun>(entity =>
        {
            _ = entity.ToTable("ServerRuns");
            _ = entity.HasKey(r => r.ServerName);
            _ = entity.Property(r => r.ServerName).HasMaxLength(256);
            _ = entity.Property(r => r.StartedUtc).HasColumnType("datetime2");
        });

        _ = modelBuilder.Entity<Attachment>(entity =>
        {
            _ = entity.ToTable("Attachments");
            _ = entity.HasKey(a => a.BindId);
            // Unbind ends the attachment.
            _ = entity.HasOne(a => a.Bind).WithOne(b => b.Attachment).HasForeignKey<Attachment>(a => a.BindId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity.Property(a => a.BusId).HasMaxLength(16).IsRequired();
            _ = entity.Property(a => a.ClientAddress).HasMaxLength(64).IsRequired();
            _ = entity.Property(a => a.StubInstanceId).HasMaxLength(400).IsRequired();
            _ = entity.Property(a => a.UserName).HasMaxLength(UserNames.MaxLength).IsRequired();
            _ = entity.Property(a => a.StartedUtc).HasColumnType("datetime2");
        });

        _ = modelBuilder.Entity<OtpSecret>(entity =>
        {
            _ = entity.ToTable("OtpSecrets");
            _ = entity.HasKey(o => o.UserId);
            _ = entity.HasOne(o => o.User).WithOne(u => u.OtpSecret).HasForeignKey<OtpSecret>(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            _ = entity.Property(o => o.Secret).HasMaxLength(64).IsRequired();
            _ = entity.Property(o => o.CreatedUtc).HasColumnType("datetime2");
            _ = entity.Property(o => o.CreatedBy).HasMaxLength(256).IsRequired();
        });

        _ = modelBuilder.Entity<ClientCertificate>(entity =>
        {
            _ = entity.ToTable("ClientCertificates");
            _ = entity.Property(c => c.Thumbprint).HasMaxLength(40).IsFixedLength().IsUnicode(false).IsRequired();
            _ = entity.HasIndex(c => c.Thumbprint).IsUnique();
            _ = entity.Property(c => c.Name).HasMaxLength(256).IsRequired();
            _ = entity.Property(c => c.Subject).HasMaxLength(400);
            _ = entity.Property(c => c.NotAfterUtc).HasColumnType("datetime2");
            _ = entity.Property(c => c.CreatedUtc).HasColumnType("datetime2");
            _ = entity.Property(c => c.CreatedBy).HasMaxLength(256).IsRequired();
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

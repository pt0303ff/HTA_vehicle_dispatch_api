
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Data.Configurations.Auth
{
    public class DriverAppSessionConfiguration
        : IEntityTypeConfiguration<DriverAppSession>
    {
        public void Configure(
            EntityTypeBuilder<DriverAppSession> builder)
        {
            // 資料表名稱
            builder.ToTable("vehicledriverappsession", "dbo");

            // Primary Key
            builder.HasKey(x => x.SessionId)
                .HasName("PK_vehicledriverappsession");

            builder.Property(x => x.SessionId)
                .HasColumnName("session_id")
                .HasColumnType("uniqueidentifier")
                .HasDefaultValueSql("NEWSEQUENTIALID()");
            // TODO(DB欄位確認)：Session 主鍵

            builder.Property(x => x.DriverId)
                .HasColumnName("driver_id")
                .HasColumnType("uniqueidentifier")
                .IsRequired();
            // TODO(DB欄位確認)：司機 ID

            builder.Property(x => x.PhoneId)
                .HasColumnName("phone_id")
                .HasColumnType("uniqueidentifier")
                .IsRequired();
            // TODO(DB欄位確認)：手機設備 ID

            builder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("datetime2(3)")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            // TODO(DB欄位確認)：Session 建立時間

            builder.Property(x => x.LastUsedAt)
                .HasColumnName("lastused_at")
                .HasColumnType("datetime2(3)");
            // TODO(DB欄位確認)：最後使用時間

            builder.Property(x => x.IdleExpiresAt)
                .HasColumnName("idleexpires_at")
                .HasColumnType("datetime2(3)")
                .IsRequired();
            // TODO(DB欄位確認)：Session 閒置到期時間

            builder.Property(x => x.AbsoluteExpiresAt)
                .HasColumnName("absoluteexpiresat")
                .HasColumnType("datetime2(3)")
                .IsRequired();
            // TODO(DB欄位確認)：Session 最長到期時間

            builder.Property(x => x.RevokedAt)
                .HasColumnName("revoked_at")
                .HasColumnType("datetime2(3)");
            // TODO(DB欄位確認)：Session 撤銷時間

            builder.Property(x => x.RevokedReason)
                .HasColumnName("revokedreason")
                .HasMaxLength(50);
            // TODO(DB欄位確認)：Session 撤銷原因

            builder.Property(x => x.RowVersion)
                .HasColumnName("row_version")
                .IsRowVersion();
            // TODO(DB欄位確認)：SQL Server ROWVERSION
            // 用於並行控制，避免同一 Session 被同時更新

            // 查詢司機的 Session
            builder.HasIndex(x => x.DriverId)
                .HasDatabaseName("IX_appsession_driver");

            // 查詢手機的 Session
            builder.HasIndex(x => x.PhoneId)
                .HasDatabaseName("IX_appsession_phone");

            // 一個 Session 對應多筆 Refresh Token
            builder.HasMany(x => x.RefreshTokens)
                .WithOne(x => x.Session)
                .HasForeignKey(x => x.SessionId)
                .HasConstraintName("FK_refreshtoken_session")
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

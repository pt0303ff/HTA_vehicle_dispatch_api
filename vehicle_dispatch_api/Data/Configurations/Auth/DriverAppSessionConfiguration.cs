
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
            // ==========================================
            // 1. 資料表名稱
            // ==========================================

            builder.ToTable(
                "vehicledriverappsession",
                "dbo");


            // ==========================================
            // 2. Primary Key
            // ==========================================

            builder.HasKey(x => x.SessionId)
                .HasName("PK_vehicledriverappsession");

            builder.Property(x => x.SessionId)
                .HasColumnName("session_id")
                .HasColumnType("uniqueidentifier")
                .HasDefaultValueSql("NEWSEQUENTIALID()");


            // ==========================================
            // 3. 司機與手機 ID
            // ==========================================

            builder.Property(x => x.DriverId)
                .HasColumnName("driver_id")
                .HasColumnType("uniqueidentifier")
                .IsRequired();

            builder.Property(x => x.PhoneId)
                .HasColumnName("phone_id")
                .HasColumnType("uniqueidentifier")
                .IsRequired();


            // ==========================================
            // 4. Session 時間
            // ==========================================

            builder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("datetime2(3)")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(x => x.LastUsedAt)
                .HasColumnName("lastused_at")
                .HasColumnType("datetime2(3)");

            builder.Property(x => x.IdleExpiresAt)
                .HasColumnName("idleexpires_at")
                .HasColumnType("datetime2(3)")
                .IsRequired();

            builder.Property(x => x.AbsoluteExpiresAt)
                .HasColumnName("absoluteexpiresat")
                .HasColumnType("datetime2(3)")
                .IsRequired();


            // ==========================================
            // 5. Session 撤銷
            // ==========================================

            builder.Property(x => x.RevokedAt)
                .HasColumnName("revoked_at")
                .HasColumnType("datetime2(3)");

            builder.Property(x => x.RevokedReason)
                .HasColumnName("revokedreason")
                .HasMaxLength(50);


            // ==========================================
            // 6. 並行控制
            // ==========================================

            builder.Property(x => x.RowVersion)
                .HasColumnName("row_version")
                .IsRowVersion();


            // ==========================================
            // 7. 查詢索引
            // ==========================================

            builder.HasIndex(x => x.DriverId)
                .HasDatabaseName("IX_appsession_driver");

            builder.HasIndex(x => x.PhoneId)
                .HasDatabaseName("IX_appsession_phone");


            // ==========================================
            // 8. Session 與手機設備關聯（本次新增）
            // ==========================================

            builder.HasOne(x => x.Phone)
                .WithMany(x => x.Sessions)

                // Session 端的 Foreign Key
                .HasForeignKey(x => new
                {
                    x.PhoneId,
                    x.DriverId
                })

                // Phone 端的 Alternate Key
                .HasPrincipalKey(x => new
                {
                    x.PhoneId,
                    x.DriverId
                })

                .HasConstraintName(
                    "FK_appsession_driverphone")

                .OnDelete(DeleteBehavior.Restrict);

            // 關聯：
            // vehicledriverappsession(phone_id, driver_id)
            //   -> vehicledriverphone(phone_id, driver_id)
            //
            // 必須同時符合手機 ID 及司機 ID


            // ==========================================
            // 9. Session 與 Refresh Token 關聯
            // ==========================================

            builder.HasMany(x => x.RefreshTokens)
                .WithOne(x => x.Session)

                .HasForeignKey(x => x.SessionId)

                .HasConstraintName(
                    "FK_refreshtoken_session")

                .OnDelete(DeleteBehavior.Restrict);

            // 一個 Session 可以有多筆 Refresh Token
            // 保留原本設定
        }
    }
}

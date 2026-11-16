
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Data.Configurations.Auth
{
    public class DriverAppPhoneConfiguration
        : IEntityTypeConfiguration<DriverAppPhone>
    {
        public void Configure(
            EntityTypeBuilder<DriverAppPhone> builder)
        {
            // ==========================================
            // 1. 資料表名稱
            // ==========================================

            builder.ToTable("vehicledriverphone", "dbo");


            // ==========================================
            // 2. Primary Key
            // ==========================================

            builder.HasKey(x => x.PhoneId)
                .HasName("PK_vehicledriverphone");

            builder.Property(x => x.PhoneId)
                .HasColumnName("phone_id")
                .HasColumnType("uniqueidentifier")
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .ValueGeneratedOnAdd();

            // phone_id 由 SQL Server 自動產生 GUID
            // 實際資料表須有對應的 DEFAULT 約束


            // ==========================================
            // 3. 司機 ID
            // ==========================================

            builder.Property(x => x.DriverId)
                .HasColumnName("driver_id")
                .HasColumnType("uniqueidentifier")
                .IsRequired();

            // 對應 vehicledriver.driver_id


            // ==========================================
            // 4. 手機平台
            // ==========================================

            builder.Property(x => x.Platform)
                .HasColumnName("platform")
                .HasColumnType("varchar(20)")
                .IsRequired();

            // Android / iOS


            // ==========================================
            // 5. 設備狀態
            // ==========================================

            builder.Property(x => x.Status)
                .HasColumnName("status")
                .HasColumnType("nvarchar(20)")
                .IsRequired();

            // Bound：已完成綁定
            // Disabled：已停用


            // ==========================================
            // 6. 手機公鑰
            // ==========================================

            builder.Property(x => x.PublicKey)
                .HasColumnName("publickey")
                .HasColumnType("nvarchar(MAX)");
                

            // 儲存手機端產生的公鑰
            // DB 欄位允許 NULL
            // 但首次綁定 API 必須檢查公鑰不可為空


            // ==========================================
            // 7. 綁定時間
            // ==========================================

            builder.Property(x => x.CreatedAt)
                .HasColumnName("createdate")
                .HasColumnType("datetime2(3)")
                .IsRequired();

            // 使用 UTC 時間
            // 由 API 建立設備時填入


            // ==========================================
            // 8. 解除綁定時間
            // ==========================================

            builder.Property(x => x.RevokedAt)
                .HasColumnName("revokedate")
                .HasColumnType("datetime2(3)");

            // Bound：NULL
            // Disabled：記錄停用時間


            // ==========================================
            // 9. 複合唯一鍵
            // ==========================================

            builder.HasAlternateKey(x => new
            {
                x.PhoneId,
                x.DriverId
            })
            .HasName("UQ_driverphone_phone_driver");

            // 供 Session 使用複合外鍵：
            // (phone_id, driver_id)
            // 下一步設定 Session 關聯


            // ==========================================
            // 10. 司機資料表外鍵
            // ==========================================

            builder.HasOne<DriverAppAccount>()
                .WithMany()
                .HasForeignKey(x => x.DriverId)
                .HasConstraintName("FK_driverphone_driver")
                .OnDelete(DeleteBehavior.Restrict);

            // 每台設備必須對應一筆司機資料
            // 不允許刪除司機時連帶刪除設備歷史紀錄


            // ==========================================
            // 11. 查詢索引
            // ==========================================

            builder.HasIndex(x => x.DriverId)
                .HasDatabaseName("IX_driverphone_driver");

            // 方便查詢某個司機已綁定的手機
        }
    }
}

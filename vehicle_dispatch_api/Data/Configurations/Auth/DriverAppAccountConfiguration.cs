
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Data.Configurations.Auth
{
    public class DriverAppAccountConfiguration
        : IEntityTypeConfiguration<DriverAppAccount>
    {
        public void Configure(
            EntityTypeBuilder<DriverAppAccount> builder)
        {
            // 對應正式資料表
            builder.ToTable("vehicledriver", "dbo");

            // 主鍵：driver_id
            builder.HasKey(x => x.DriverId)
                .HasName("PK_vehicledriver");

            builder.Property(x => x.DriverId)
                .HasColumnName("driver_id")
                .HasColumnType("uniqueidentifier")
                .HasDefaultValueSql("NEWSEQUENTIALID()");
            // DB：司機識別碼

            builder.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(20)
                .IsRequired();
            // DB：司機姓名

            builder.Property(x => x.LoginId)
                .HasColumnName("loginid")
                .HasMaxLength(100)
                .IsRequired();
            // DB：APP 登入帳號

            builder.Property(x => x.PasswordHash)
                .HasColumnName("passwordhash")
                .HasColumnType("varchar(500)")
                .IsRequired();
            // DB：密碼雜湊

            builder.Property(x => x.AccountStatus)
                .HasColumnName("accountstatus")
                .HasMaxLength(20)
                .IsRequired();
            // DB：帳號狀態

            builder.Property(x => x.ActivationTokenHash)
                .HasColumnName("activationtokenhash")
                .HasColumnType("char(64)")
                .IsFixedLength();
            // DB：開通碼 Hash

            builder.Property(x => x.ActivationExpiresAt)
                .HasColumnName("activationexpires_at")
                .HasColumnType("datetime2(3)");
            // DB：開通碼到期時間

            builder.Property(x => x.FailedLoginAttempts)
                .HasColumnName("failedloginattempts")
                .HasDefaultValue(0);
            // DB：登入失敗次數

            builder.Property(x => x.PasswordChangedAt)
                .HasColumnName("passwordchanged_at")
                .HasColumnType("datetime2(3)");
            // DB：密碼修改時間

            builder.Property(x => x.ModifyDate)
                .HasColumnName("modifydate")
                .HasColumnType("datetime2(3)")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            // DB：最後異動時間

            builder.Property(x => x.InitDate)
                .HasColumnName("initdate")
                .HasColumnType("datetime2(3)")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            // DB：建立時間

            builder.Property(x => x.ModifyUid)
                .HasColumnName("modifyuid")
                .HasColumnType("uniqueidentifier");
            // DB：最後修改者

            // 登入帳號不可重複
            builder.HasIndex(x => x.LoginId)
                .IsUnique()
                .HasDatabaseName("UQ_vehicledriver_loginid");

            // 開通碼 Hash 不可重複
            builder.HasIndex(x => x.ActivationTokenHash)
                .IsUnique()
                .HasFilter("[activationtokenhash] IS NOT NULL")
                .HasDatabaseName("UX_driver_activationtoken");
        }
    }
}


using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Data.Configurations.Auth
{
    public class DriverAppRefreshTokenConfiguration
        : IEntityTypeConfiguration<DriverAppRefreshToken>
    {
        public void Configure(
            EntityTypeBuilder<DriverAppRefreshToken> builder)
        {
            // 資料表名稱
            builder.ToTable("vehicledriverapprefreshtoken", "dbo");

            // Primary Key
            builder.HasKey(x => x.TokenId)
                .HasName("PK_vehicledriverapprefreshtoken");

            builder.Property(x => x.TokenId)
                .HasColumnName("token_id")
                .HasColumnType("uniqueidentifier")
                .HasDefaultValueSql("NEWSEQUENTIALID()");
            // TODO(DB欄位確認)：Refresh Token 主鍵

            builder.Property(x => x.SessionId)
                .HasColumnName("session_id")
                .HasColumnType("uniqueidentifier")
                .IsRequired();
            // TODO(DB欄位確認)：所屬 Session ID

            builder.Property(x => x.TokenHash)
                .HasColumnName("tokenhash")
                .HasColumnType("char(64)")
                .IsFixedLength()
                .IsRequired();
            // TODO(DB欄位確認)：Refresh Token SHA256 雜湊

            builder.Property(x => x.ExpiresAt)
                .HasColumnName("expiresat")
                .HasColumnType("datetime2(3)")
                .IsRequired();
            // TODO(DB欄位確認)：Refresh Token 到期時間

            builder.Property(x => x.RevokedAt)
                .HasColumnName("revoked_at")
                .HasColumnType("datetime2(3)");
            // TODO(DB欄位確認)：Refresh Token 撤銷時間

            builder.Property(x => x.ReplacedByTokenId)
                .HasColumnName("replacedbytokenid")
                .HasColumnType("uniqueidentifier");
            // TODO(DB欄位確認)：取代目前 Token 的新 Token ID

            builder.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("datetime2(3)")
                .HasDefaultValueSql("SYSUTCDATETIME()");
            // TODO(DB欄位確認)：Token 建立時間

            builder.Property(x => x.TokenVersion)
                .HasColumnName("tokenversion")
                .HasColumnType("int")
                .IsRequired();
            // TODO(DB欄位確認)：Token 輪替版本

            // 一般 Session 查詢索引
            builder.HasIndex(x => x.SessionId,"IX_refreshtoken_session");

            // Token Hash 不可重複
            builder.HasIndex(x => x.TokenHash)
                .IsUnique()
                .HasDatabaseName("UQ_refreshtoken_hash");

            // 同一 Session 的 TokenVersion 不可重複
            builder.HasIndex(x => new
            {
                x.SessionId,
                x.TokenVersion
            })
                .IsUnique()
                .HasDatabaseName("UQ_refreshtoken_version");

            // 同一 Session 只能有一筆未撤銷 Token
            builder.HasIndex(
                    x => x.SessionId,
                    "UX_refreshtoken_active"
                )
                .IsUnique()
                .HasFilter("[revoked_at] IS NULL");

            // Refresh Token 自我關聯
            // 舊 Token 可以指向取代它的新 Token
            builder.HasOne(x => x.ReplacedByToken)
                .WithMany(x => x.ReplacedTokens)
                .HasForeignKey(x => x.ReplacedByTokenId)
                .HasConstraintName("FK_refreshtoken_replaced")
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

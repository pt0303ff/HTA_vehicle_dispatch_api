
using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Data
{
    public class ApplicationDbContext : DbContext
    {
        // 建構子：接收 Program.cs 注入的 SQL Server 設定
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ==========================================
        // DbSet：APP 帳號資料表
        // ==========================================

        public DbSet<DriverAppAccount> DriverAppAccounts
            => Set<DriverAppAccount>();
        // 對應 dbo.vehicledriverappaccount
        // TODO(DB確認)：資料表名稱及欄位目前仍為暫定


        // ==========================================
        // DbSet：登入 Session 資料表
        // ==========================================

        public DbSet<DriverAppSession> DriverAppSessions
            => Set<DriverAppSession>();
        // 對應 dbo.vehicledriverappsession
        // TODO(DB確認)：依照目前提供的 DDL


        // ==========================================
        // DbSet：Refresh Token 資料表
        // ==========================================

        public DbSet<DriverAppRefreshToken> DriverAppRefreshTokens
            => Set<DriverAppRefreshToken>();
        // 對應 dbo.vehicledriverapprefreshtoken
        // TODO(DB確認)：依照目前提供的 DDL


        // ==========================================
        // EF Core Model Configuration
        // ==========================================

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 自動載入目前專案 Assembly 中所有
            // 實作 IEntityTypeConfiguration 的設定類別
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(ApplicationDbContext).Assembly);
        }
    }
}

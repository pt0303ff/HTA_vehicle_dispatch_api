
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
        // 1. APP 司機帳號資料表
        // ==========================================

        public DbSet<DriverAppAccount> DriverAppAccounts
            => Set<DriverAppAccount>();

        // DB：dbo.vehicledriver
        // 儲存司機帳號、密碼 Hash、開通驗證碼等


        // ==========================================
        // 2. APP 手機設備資料表（本次新增）
        // ==========================================

        public DbSet<DriverAppPhone> DriverAppPhones
            => Set<DriverAppPhone>();

        // DB：dbo.vehicledriverphone
        // 儲存手機綁定紀錄、公鑰、設備狀態
        // 一個司機可以對應多台手機


        // ==========================================
        // 3. APP 登入 Session 資料表
        // ==========================================

        public DbSet<DriverAppSession> DriverAppSessions
            => Set<DriverAppSession>();

        // DB：dbo.vehicledriverappsession
        // 儲存司機每次登入的 Session


        // ==========================================
        // 4. APP Refresh Token 資料表
        // ==========================================

        public DbSet<DriverAppRefreshToken> DriverAppRefreshTokens
            => Set<DriverAppRefreshToken>();

        // DB：dbo.vehicledriverapprefreshtoken
        // 儲存 Refresh Token Hash 及撤銷紀錄


        // ==========================================
        // 5. EF Core Model Configuration
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

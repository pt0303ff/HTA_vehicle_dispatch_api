
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    // 管理員主系統傳入的司機資料
    public record CreateDriverAccountCommand(
        Guid DriverId,
        string Name,
        string LoginId,
        Guid? OperatorId
    );

    // 只回傳給經授權管理員的一次性開通資訊
    public record CreateDriverAccountResult(
        Guid DriverId,
        string ActivationCode,
        DateTime ActivationExpiresAtUtc
    );

    public class DriverAccountProvisioningService
    {
        private readonly ApplicationDbContext _db;
        private readonly DriverPasswordService _passwordService;

        public DriverAccountProvisioningService(
            ApplicationDbContext db,
            DriverPasswordService passwordService)
        {
            _db = db;
            _passwordService = passwordService;
        }

        public async Task<CreateDriverAccountResult> CreateAsync(
            CreateDriverAccountCommand command,
            CancellationToken cancellationToken = default)
        {
            // driver_id 由管理員主系統決定
            // 員工司機：使用員工 id
            // 外部司機：使用新產生的 GUID
            if (command.DriverId == Guid.Empty)
                throw new ArgumentException("DriverId 不可為空");

            var name = command.Name?.Trim() ?? "";
            var loginId = command.LoginId?.Trim() ?? "";

            if (name.Length == 0 || name.Length > 20)
                throw new ArgumentException("司機姓名長度不正確");

            if (loginId.Length == 0 || loginId.Length > 100)
                throw new ArgumentException("登入帳號長度不正確");

            // 檢查司機 ID 或登入帳號是否重複
            var exists = await _db.DriverAppAccounts
                .AnyAsync(x =>
                    x.DriverId == command.DriverId ||
                    x.LoginId == loginId,
                    cancellationToken);

            if (exists)
                throw new InvalidOperationException(
                    "司機 ID 或登入帳號已存在");

            var now = DateTime.UtcNow;

            // 產生 128-bit 隨機開通碼
            var activationCode = Convert.ToHexString(
                RandomNumberGenerator.GetBytes(16));

            // 資料庫只儲存開通碼 Hash
            var activationHash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(activationCode)));

            var driver = new DriverAppAccount
            {
                DriverId = command.DriverId,
                Name = name,
                LoginId = loginId,

                AccountStatus = "PendingActivation",

                ActivationTokenHash = activationHash,
                ActivationExpiresAt = now.AddHours(48),
                FailedLoginAttempts = 0,

                InitDate = now,
                ModifyDate = now,
                ModifyUid = command.OperatorId
            };

            // 因為 passwordhash 不允許 NULL，
            // 未開通前先放入不可預測的隨機密碼 Hash。
            // 不將這個臨時密碼提供給任何使用者。
            var randomPassword = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(32));

            driver.PasswordHash = _passwordService
                .HashPassword(driver, randomPassword);

            _db.DriverAppAccounts.Add(driver);

            await _db.SaveChangesAsync(cancellationToken);

            // 開通碼只在建立成功時回傳一次
            // 不寫入 Log，也不儲存明文到 DB
            return new CreateDriverAccountResult(
                driver.DriverId,
                activationCode,
                driver.ActivationExpiresAt!.Value
            );
        }
    }
}

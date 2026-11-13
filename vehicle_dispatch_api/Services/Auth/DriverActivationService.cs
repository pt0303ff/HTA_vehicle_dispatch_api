using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.DTOs.Auth;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class DriverActivationService
    {
        private readonly ApplicationDbContext _db;
        private readonly DriverPasswordService _passwordService;

        // 初始密碼或開通碼任一驗證錯誤，都計入開通失敗次數。
        private const int MaxActivationAttempts = 5;

        public DriverActivationService(
            ApplicationDbContext db,
            DriverPasswordService passwordService)
        {
            _db = db;
            _passwordService = passwordService;
        }

        public async Task<bool> ActivateAsync(
            ActivateDriverRequest request,
            CancellationToken cancellationToken = default)
        {
            // 1. 基本資料檢查

            var loginId = request.LoginId?.Trim() ?? string.Empty;

            // Base36 不區分大小寫：
            // 管理者端與 API 皆先 Trim，再轉成大寫，最後以 UTF-8 計算 SHA256。
            var activationCode =
                request.ActivationCode?.Trim().ToUpperInvariant()
                ?? string.Empty;

            // 密碼不可 Trim，空白可能是密碼本身的一部分。
            var initialPassword = request.InitialPassword ?? string.Empty;
            var newPassword = request.NewPassword ?? string.Empty;

            if (loginId.Length == 0 || loginId.Length > 100)
                throw new ArgumentException("登入帳號格式不正確");

            // 開通碼：16 位 Base36，允許 0-9、A-Z。
            if (activationCode.Length != 16 ||
                !activationCode.All(char.IsAsciiLetterOrDigit))
            {
                return false;
            }

            // 初始密碼長度限制，需與管理者頁面產生規格相容。
            if (string.IsNullOrWhiteSpace(initialPassword) ||
                initialPassword.Length < 12 ||
                initialPassword.Length > 128)
            {
                throw new ArgumentException(
                    "初始密碼長度必須介於 12 至 128 個字元");
            }

            // 司機自行設定的正式密碼長度限制。
            if (string.IsNullOrWhiteSpace(newPassword) ||
                newPassword.Length < 12 ||
                newPassword.Length > 128)
            {
                throw new ArgumentException(
                    "新密碼長度必須介於 12 至 128 個字元");
            }

            var now = DateTime.UtcNow;

            // 2. 查詢司機帳號
            var driver = await _db.DriverAppAccounts
                .FirstOrDefaultAsync(
                    x => x.LoginId == loginId,
                    cancellationToken);

            // 不存在的帳號不透露詳細原因。
            if (driver == null)
                return false;

            // 3. 確認帳號狀態
            if (driver.AccountStatus != "PendingActivation")
                return false;

            // 4. 確認開通碼是否存在及是否過期
            if (string.IsNullOrEmpty(driver.ActivationTokenHash) ||
                !driver.ActivationExpiresAt.HasValue ||
                driver.ActivationExpiresAt.Value <= now)
            {
                return false;
            }

            // 已達最大失敗次數，必須由管理員重新發送開通碼。
            if (driver.ActivationFailedAttempts >= MaxActivationAttempts)
                return false;

            // 5. 驗證管理者預先設定的初始密碼
            // DB passwordhash 是由管理者頁面使用 PasswordHasher Hash 後寫入。
            // API 使用 VerifyHashedPassword 驗證，不可直接重新 Hash 比對。
            bool isInitialPasswordValid =
                _passwordService.VerifyPassword(driver, initialPassword);

            // 6. 驗證 16 位 Base36 開通碼的 SHA256
            bool isActivationCodeValid = VerifyActivationCode(
                activationCode,
                driver.ActivationTokenHash);

            // 初始密碼或開通碼任一錯誤，都增加失敗次數。
            if (!isInitialPasswordValid || !isActivationCodeValid)
            {
                await RegisterFailedAttemptAsync(
                    driver,
                    now,
                    cancellationToken);

                return false;
            }

            // 7. 兩項驗證均成功，產生司機正式密碼 Hash
            var newPasswordHash = _passwordService.HashPassword(
                driver,
                newPassword);

            // 8. 原子性更新帳號
            // 再次確認狀態、開通碼、密碼 Hash 及期限仍與剛才讀取時相同，
            // 避免管理員同時重設資料，或同一組開通碼被重複使用。
            var affectedRows = await _db.DriverAppAccounts
                .Where(x =>
                    x.DriverId == driver.DriverId &&
                    x.AccountStatus == "PendingActivation" &&
                    x.PasswordHash == driver.PasswordHash &&
                    x.ActivationTokenHash == driver.ActivationTokenHash &&
                    x.ActivationExpiresAt > now &&
                    x.ActivationFailedAttempts < MaxActivationAttempts)
                .ExecuteUpdateAsync(setters => setters

                    // 將管理者設定的初始密碼 Hash
                    // 替換成司機自行設定的正式密碼 Hash。
                    .SetProperty(
                        x => x.PasswordHash,
                        newPasswordHash)

                    .SetProperty(
                        x => x.AccountStatus,
                        "Active")

                    // 開通成功後，一次性開通碼立即失效。
                    .SetProperty(
                        x => x.ActivationTokenHash,
                        (string?)null)

                    .SetProperty(
                        x => x.ActivationExpiresAt,
                        (DateTime?)null)

                    .SetProperty(
                        x => x.ActivationFailedAttempts,
                        0)

                    .SetProperty(
                        x => x.FailedLoginAttempts,
                        0)

                    .SetProperty(
                        x => x.PasswordChangedAt,
                        now)

                    .SetProperty(
                        x => x.ModifyDate,
                        now),

                    cancellationToken);

            // 只有成功更新一筆才算開通成功。
            return affectedRows == 1;
        }

        // SHA256 驗證：
        // 原始碼先 Trim + ToUpperInvariant，再以 UTF-8 計算 SHA256。
        // Hash 輸出為 64 位 Hex，與 DB CHAR(64) 比較。
        private static bool VerifyActivationCode(
            string inputCode,
            string storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash))
                return false;

            try
            {
                var normalizedCode =
                    inputCode.Trim().ToUpperInvariant();

                if (normalizedCode.Length != 16 ||
                    !normalizedCode.All(char.IsAsciiLetterOrDigit))
                {
                    return false;
                }

                byte[] inputHash = SHA256.HashData(
                    Encoding.UTF8.GetBytes(normalizedCode));

                byte[] databaseHash = Convert.FromHexString(
                    storedHash.Trim());

                // 先確認長度相同，再以固定時間比較。
                return databaseHash.Length == inputHash.Length &&
                    CryptographicOperations.FixedTimeEquals(
                        inputHash,
                        databaseHash);
            }
            catch (FormatException)
            {
                // DB 內 activationtokenhash 不是有效的 Hex 字串。
                return false;
            }
        }

        // 初始密碼或開通碼驗證失敗時，增加開通失敗次數。
        // 第五次錯誤後，直接清除開通碼及到期時間，使其失效。
        private async Task RegisterFailedAttemptAsync(
            DriverAppAccount driver,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var expectedActivationHash = driver.ActivationTokenHash;
            var expectedPasswordHash = driver.PasswordHash;

            await _db.DriverAppAccounts
                .Where(x =>
                    x.DriverId == driver.DriverId &&
                    x.AccountStatus == "PendingActivation" &&
                    x.PasswordHash == expectedPasswordHash &&
                    x.ActivationTokenHash == expectedActivationHash &&
                    x.ActivationExpiresAt > now &&
                    x.ActivationFailedAttempts < MaxActivationAttempts)
                .ExecuteUpdateAsync(setters => setters

                    .SetProperty(
                        x => x.ActivationFailedAttempts,
                        x => x.ActivationFailedAttempts + 1)

                    .SetProperty(
                        x => x.ActivationTokenHash,
                        x => x.ActivationFailedAttempts + 1 >= MaxActivationAttempts
                            ? null
                            : x.ActivationTokenHash)

                    .SetProperty(
                        x => x.ActivationExpiresAt,
                        x => x.ActivationFailedAttempts + 1 >= MaxActivationAttempts
                            ? (DateTime?)null
                            : x.ActivationExpiresAt)

                    .SetProperty(
                        x => x.ModifyDate,
                        now),

                    cancellationToken);
        }
    }
}

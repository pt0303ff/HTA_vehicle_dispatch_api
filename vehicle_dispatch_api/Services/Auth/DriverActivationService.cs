
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.DTOs.Auth;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class DriverActivationService
    {
        private readonly ApplicationDbContext _db;
        private readonly DriverPasswordService _passwordService;
        private readonly ILogger<DriverActivationService> _logger;

        // 最多允許開通碼輸入錯誤 5 次
        private const int MaxActivationAttempts = 5;

        public DriverActivationService(
            ApplicationDbContext db,
            DriverPasswordService passwordService,
            ILogger<DriverActivationService> logger)
        {
            _db = db;
            _passwordService = passwordService;
            _logger = logger;
        }

        // ==========================================
        // 司機首次開通
        // ==========================================

        public async Task<ActivateDriverResponse?> ActivateAsync(
            ActivateDriverRequest request,
            CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "Driver activation service started.");

            // ==========================================
            // 1. 基本資料檢查
            // ==========================================

            ArgumentNullException.ThrowIfNull(request);

            var loginId = request.LoginId?.Trim() ?? "";

            // 密碼不可 Trim，避免改變使用者輸入內容
            var initialPassword = request.InitialPassword ?? "";
            var newPassword = request.NewPassword ?? "";

            // 統一正規化開通碼
            var activationCode = NormalizeActivationCode(
                request.ActivationCode
            );

            var publicKey = request.PublicKey?.Trim() ?? "";

            if (loginId.Length == 0 || loginId.Length > 100)
                return Reject("Input", "LoginIdInvalid");

            if (initialPassword.Length == 0 ||
                initialPassword.Length > 128)
                return Reject("Input", "InitialPasswordInvalid");

            if (newPassword.Length < 8 ||
                newPassword.Length > 128)
                return Reject("Input", "NewPasswordInvalid");

            // ==========================================
            // 2. 驗證手機平台
            // ==========================================

            string platform;

            if (string.Equals(
                request.Platform?.Trim(),
                "Android",
                StringComparison.OrdinalIgnoreCase))
            {
                platform = "Android";
            }
            else if (string.Equals(
                request.Platform?.Trim(),
                "iOS",
                StringComparison.OrdinalIgnoreCase))
            {
                platform = "iOS";
            }
            else
            {
                return Reject("Platform", "PlatformInvalid");
            }

            // ==========================================
            // 3. 驗證手機公鑰格式
            // ==========================================

            if (publicKey.Length < 80 ||
                publicKey.Length > 2048)
            {
                return Reject(
                    "PublicKey",
                    "PublicKeyLengthInvalid");
            }

            if (!IsValidP256PublicKey(publicKey))
            {
                return Reject(
                    "PublicKey",
                    "PublicKeyInvalid");
            }

            // ==========================================
            // 4. 查詢司機帳號
            // ==========================================

            var now = DateTime.UtcNow;

            var driver = await _db.DriverAppAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.LoginId == loginId,
                    cancellationToken);

            if (driver == null)
                return Reject("Account", "DriverNotFound");

            // 只能開通尚未開通的帳號
            if (driver.AccountStatus != "PendingActivation")
                return Reject("Account", "AccountStatusInvalid");

            // ==========================================
            // 5. 驗證管理員提供的初始密碼
            // ==========================================

            bool passwordValid = _passwordService.VerifyPassword(
                driver,
                initialPassword);

            if (!passwordValid)
            {
                return Reject(
                    "Password",
                    "InitialPasswordVerificationFailed");
            }

            // ==========================================
            // 6. 檢查開通碼錯誤次數
            // ==========================================

            if (driver.ActivationFailedAttempts >= MaxActivationAttempts)
            {
                // 保險處理：若資料庫仍有開通碼，將其失效
                await _db.DriverAppAccounts
                    .Where(x =>
                        x.DriverId == driver.DriverId &&
                        x.AccountStatus == "PendingActivation" &&
                        x.ActivationFailedAttempts >= MaxActivationAttempts)
                    .ExecuteUpdateAsync(setters => setters

                        .SetProperty(
                            x => x.ActivationTokenHash,
                            (string?)null)

                        .SetProperty(
                            x => x.ActivationExpiresAt,
                            (DateTime?)null)

                        .SetProperty(
                            x => x.ModifyDate,
                            now),

                        cancellationToken);

                return Reject(
                    "ActivationCode",
                    "MaxAttemptsExceeded");
            }

            // ==========================================
            // 7. 檢查開通碼有效期限
            // ==========================================

            if (string.IsNullOrWhiteSpace(driver.ActivationTokenHash))
            {
                return Reject(
                    "ActivationCode",
                    "TokenHashMissing");
            }

            if (!driver.ActivationExpiresAt.HasValue)
            {
                return Reject(
                    "ActivationCode",
                    "ExpiryMissing");
            }

            if (driver.ActivationExpiresAt.Value <= now)
            {
                return Reject(
                    "ActivationCode",
                    "Expired");
            }

            // ==========================================
            // 8. 驗證開通碼格式
            // ==========================================

            // 16 位 Base36：0-9、A-Z

            bool validCodeFormat =
                activationCode.Length == 16 &&
                activationCode.All(c =>
                    (c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'Z'));

            if (!validCodeFormat)
            {
                await RecordActivationFailureAsync(
                    driver,
                    now,
                    cancellationToken);

                return Reject(
                    "ActivationCode",
                    "CodeFormatInvalid");
            }

            // ==========================================
            // 9. SHA256 驗證開通碼
            // ==========================================

            byte[] inputHash = SHA256.HashData(
                Encoding.UTF8.GetBytes(activationCode));

            byte[] storedHash;

            try
            {
                storedHash = Convert.FromHexString(
                    driver.ActivationTokenHash.Trim());
            }
            catch (FormatException)
            {
                // DB Hash 格式錯誤屬於系統資料異常
                // 不計入使用者錯誤次數
                return Reject(
                    "ActivationCode",
                    "StoredHashInvalid");
            }

            if (storedHash.Length != inputHash.Length)
            {
                return Reject(
                    "ActivationCode",
                    "StoredHashLengthInvalid");
            }

            bool activationValid =
                CryptographicOperations.FixedTimeEquals(
                    inputHash,
                    storedHash);

            if (!activationValid)
            {
                // 開通碼錯誤，累計失敗次數
                await RecordActivationFailureAsync(
                    driver,
                    now,
                    cancellationToken);

                return Reject(
                    "ActivationCode",
                    "HashMismatch");
            }

            // ==========================================
            // 10. 產生正式密碼 Hash
            // ==========================================

            string newPasswordHash =
                _passwordService.HashPassword(
                    driver,
                    newPassword);

            // ==========================================
            // 11. 建立手機設備資料
            // ==========================================

            var phone = new DriverAppPhone
            {
                PhoneId = Guid.NewGuid(),

                DriverId = driver.DriverId,

                Platform = platform,

                Status = "Bound",

                PublicKey = publicKey,

                CreatedAt = now,

                RevokedAt = null
            };

            // ==========================================
            // 12. 開始 Transaction
            // ==========================================

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    cancellationToken);

            // ==========================================
            // 13. 更新司機帳號
            // ==========================================

            // 再次確認帳號狀態、開通碼及密碼 Hash
            // 防止重複使用開通碼
            // 同時確認尚未達到錯誤次數上限

            var affectedRows = await _db.DriverAppAccounts
                .Where(x =>
                    x.DriverId == driver.DriverId &&
                    x.AccountStatus == "PendingActivation" &&
                    x.ActivationTokenHash == driver.ActivationTokenHash &&
                    x.ActivationExpiresAt > now &&
                    x.PasswordHash == driver.PasswordHash &&
                    x.ActivationFailedAttempts < MaxActivationAttempts)
                .ExecuteUpdateAsync(setters => setters

                    // 更新正式密碼
                    .SetProperty(
                        x => x.PasswordHash,
                        newPasswordHash)

                    // 啟用帳號
                    .SetProperty(
                        x => x.AccountStatus,
                        "Active")

                    // 清除一次性開通碼
                    .SetProperty(
                        x => x.ActivationTokenHash,
                        (string?)null)

                    .SetProperty(
                        x => x.ActivationExpiresAt,
                        (DateTime?)null)

                    // 開通成功，重設開通失敗次數
                    .SetProperty(
                        x => x.ActivationFailedAttempts,
                        0)

                    // 重設登入失敗次數
                    .SetProperty(
                        x => x.FailedLoginAttempts,
                        0)

                    // 更新密碼修改時間
                    .SetProperty(
                        x => x.PasswordChangedAt,
                        now)

                    .SetProperty(
                        x => x.ModifyDate,
                        now),

                    cancellationToken);

            // 帳號更新失敗，取消整個流程
            if (affectedRows != 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Reject(
                    "Database",
                    "UpdateConflict");
            }

            // ==========================================
            // 14. 寫入手機設備資料
            // ==========================================

            _db.DriverAppPhones.Add(phone);

            await _db.SaveChangesAsync(cancellationToken);

            // ==========================================
            // 15. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Driver activation succeeded. DriverId={DriverId}, PhoneId={PhoneId}",
                driver.DriverId,
                phone.PhoneId);

            // ==========================================
            // 16. 回傳開通結果
            // ==========================================

            return new ActivateDriverResponse
            {
                DriverId = driver.DriverId,

                PhoneId = phone.PhoneId,

                AccountStatus = "Active"
            };
        }

        // ==========================================
        // 記錄開通碼錯誤次數
        // ==========================================

        private async Task<int?> RecordActivationFailureAsync(
            DriverAppAccount driver,
            DateTime now,
            CancellationToken cancellationToken)
        {
            // 使用 SQL 原子更新，避免同時請求覆蓋次數
            var affectedRows = await _db.DriverAppAccounts
                .Where(x =>
                    x.DriverId == driver.DriverId &&
                    x.AccountStatus == "PendingActivation" &&
                    x.PasswordHash == driver.PasswordHash &&
                    x.ActivationTokenHash == driver.ActivationTokenHash &&
                    x.ActivationExpiresAt > now &&
                    x.ActivationFailedAttempts < MaxActivationAttempts)
                .ExecuteUpdateAsync(setters => setters

                    // 失敗次數 +1
                    .SetProperty(
                        x => x.ActivationFailedAttempts,
                        x => x.ActivationFailedAttempts + 1)

                    // 第五次錯誤，清除開通碼
                    .SetProperty(
                        x => x.ActivationTokenHash,
                        x => x.ActivationFailedAttempts + 1 >= MaxActivationAttempts
                            ? (string?)null
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

            if (affectedRows != 1)
            {
                _logger.LogWarning(
                    "Activation failure counter was not updated. DriverId={DriverId}",
                    driver.DriverId);

                return null;
            }

            // 取得更新後的實際錯誤次數
            int? attempts = await _db.DriverAppAccounts
                .AsNoTracking()
                .Where(x => x.DriverId == driver.DriverId)
                .Select(x => (int?)x.ActivationFailedAttempts)
                .SingleOrDefaultAsync(cancellationToken);

            _logger.LogWarning(
                "Activation code failure recorded. DriverId={DriverId}, Attempts={Attempts}, MaxAttempts={MaxAttempts}",
                driver.DriverId,
                attempts,
                MaxActivationAttempts);

            if (attempts >= MaxActivationAttempts)
            {
                _logger.LogWarning(
                    "Activation code locked after maximum failed attempts. DriverId={DriverId}",
                    driver.DriverId);
            }

            return attempts;
        }

        // ==========================================
        // 統一開通失敗紀錄
        // ==========================================

        private ActivateDriverResponse? Reject(
            string stage,
            string reason)
        {
            _logger.LogWarning(
                "Driver activation rejected. Stage={Stage}, Reason={Reason}",
                stage,
                reason);

            return null;
        }

        // ==========================================
        // 驗證 ECDSA P-256 公鑰
        // ==========================================

        private static bool IsValidP256PublicKey(
            string publicKey)
        {
            try
            {
                byte[] keyBytes =
                    Convert.FromBase64String(publicKey);

                using var ecdsa = ECDsa.Create();

                ecdsa.ImportSubjectPublicKeyInfo(
                    keyBytes,
                    out int bytesRead);

                if (bytesRead != keyBytes.Length)
                    return false;

                if (ecdsa.KeySize != 256)
                    return false;

                var curveOid =
                    ecdsa.ExportParameters(false).Curve.Oid.Value;

                var expectedOid =
                    ECCurve.NamedCurves.nistP256.Oid.Value;

                return string.Equals(
                    curveOid,
                    expectedOid,
                    StringComparison.Ordinal);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // ==========================================
        // 開通碼正規化
        // ==========================================

        private static string NormalizeActivationCode(
            string? code)
        {
            return new string(
                (code ?? string.Empty)
                    .Where(c =>
                        c != '-' &&
                        !char.IsWhiteSpace(c))
                    .ToArray()
            ).ToUpperInvariant();
        }
    }
}

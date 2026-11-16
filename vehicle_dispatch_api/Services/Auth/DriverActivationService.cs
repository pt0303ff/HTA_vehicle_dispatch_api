
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            // 2. 驗證開通碼格式
            // ==========================================

            // 16 位 Base36：0-9、A-Z

            bool validCodeFormat =
                activationCode.Length == 16 &&
                activationCode.All(c =>
                    (c >= '0' && c <= '9') ||
                    (c >= 'A' && c <= 'Z'));

            if (!validCodeFormat)
                return Reject("ActivationCode", "CodeFormatInvalid");

            // ==========================================
            // 3. 驗證手機平台
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
            // 4. 驗證手機公鑰格式
            // ==========================================

            if (publicKey.Length < 80 ||
                publicKey.Length > 2048)
            {
                return Reject("PublicKey", "PublicKeyLengthInvalid");
            }

            if (!IsValidP256PublicKey(publicKey))
            {
                return Reject("PublicKey", "PublicKeyInvalid");
            }

            // ==========================================
            // 5. 查詢司機帳號
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
            // 6. 驗證管理員提供的初始密碼
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
            // 8. SHA256 驗證開通碼
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
                return Reject(
                    "ActivationCode",
                    "HashMismatch");
            }

            // ==========================================
            // 9. 產生正式密碼 Hash
            // ==========================================

            string newPasswordHash =
                _passwordService.HashPassword(
                    driver,
                    newPassword);

            // ==========================================
            // 10. 建立手機設備資料
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
            // 11. 開始 Transaction
            // ==========================================

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    cancellationToken);

            // ==========================================
            // 12. 更新司機帳號
            // ==========================================

            // 再次確認帳號狀態、開通碼及密碼 Hash
            // 防止同一組開通碼被重複使用

            var affectedRows = await _db.DriverAppAccounts
                .Where(x =>
                    x.DriverId == driver.DriverId &&
                    x.AccountStatus == "PendingActivation" &&
                    x.ActivationTokenHash == driver.ActivationTokenHash &&
                    x.ActivationExpiresAt > now &&
                    x.PasswordHash == driver.PasswordHash)
                .ExecuteUpdateAsync(setters => setters

                    .SetProperty(
                        x => x.PasswordHash,
                        newPasswordHash)

                    .SetProperty(
                        x => x.AccountStatus,
                        "Active")

                    .SetProperty(
                        x => x.ActivationTokenHash,
                        (string?)null)

                    .SetProperty(
                        x => x.ActivationExpiresAt,
                        (DateTime?)null)

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
            // 13. 寫入手機設備資料
            // ==========================================

            _db.DriverAppPhones.Add(phone);

            await _db.SaveChangesAsync(cancellationToken);

            // ==========================================
            // 14. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Driver activation succeeded. DriverId={DriverId}, PhoneId={PhoneId}",
                driver.DriverId,
                phone.PhoneId);

            // ==========================================
            // 15. 回傳開通結果
            // ==========================================

            return new ActivateDriverResponse
            {
                DriverId = driver.DriverId,

                PhoneId = phone.PhoneId,

                AccountStatus = "Active"
            };
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

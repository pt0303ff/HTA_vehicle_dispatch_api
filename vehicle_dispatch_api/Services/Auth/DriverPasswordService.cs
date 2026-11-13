using Microsoft.AspNetCore.Identity;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class DriverPasswordService
    {
        private readonly IPasswordHasher<DriverAppAccount> _hasher;

        public DriverPasswordService(
            IPasswordHasher<DriverAppAccount> hasher)
        {
            _hasher = hasher;
        }

        // 將原始密碼轉換成安全的 Password Hash。
        // 規格由 Program.cs 統一設定：
        // IdentityV3 / PBKDF2-HMAC-SHA512 / 100,000 iterations。
        public string HashPassword(
            DriverAppAccount driver,
            string password)
        {
            ArgumentNullException.ThrowIfNull(driver);

            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("密碼不可為空", nameof(password));

            return _hasher.HashPassword(driver, password);
        }

        // 驗證使用者輸入的密碼。
        // 不可自行重新 Hash 後直接比較字串，因為每次 Hash 都會產生不同 Salt。
        public bool VerifyPassword(
            DriverAppAccount driver,
            string password)
        {
            if (driver == null ||
                string.IsNullOrEmpty(driver.PasswordHash) ||
                string.IsNullOrEmpty(password))
            {
                return false;
            }

            try
            {
                var result = _hasher.VerifyHashedPassword(
                    driver,
                    driver.PasswordHash,
                    password);

                // SuccessRehashNeeded 也代表密碼正確。
                // 目前兩端固定相同規格，正常情況應回傳 Success。
                return result != PasswordVerificationResult.Failed;
            }
            catch (FormatException)
            {
                // DB 內 Hash 格式損壞或不是有效的 Identity Hash。
                return false;
            }
            catch (ArgumentException)
            {
                // DB 內 Hash 格式不正確。
                return false;
            }
        }
    }
}

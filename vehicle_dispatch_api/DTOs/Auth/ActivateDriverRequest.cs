using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class ActivateDriverRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;
        // DB：vehicledriver.loginid
        // APP 登入帳號

        [Required]
        [StringLength(128, MinimumLength = 12)]
        public string InitialPassword { get; set; } = string.Empty;
        // 管理者頁面預先產生的初始密碼明文
        // API 使用 PasswordHasher.VerifyHashedPassword() 與 DB passwordhash 驗證
        // 不會將初始密碼寫回 DB

        [Required]
        [StringLength(16, MinimumLength = 16)]
        [RegularExpression("^[0-9A-Za-z]{16}$")]
        public string ActivationCode { get; set; } = string.Empty;
        // 管理者頁面產生的 16 位 Base36 原始開通碼
        // 允許 0-9、A-Z、a-z；API 會 Trim 並轉大寫後計算 SHA256
        // DB：vehicledriver.activationtokenhash，CHAR(64)

        [Required]
        [StringLength(128, MinimumLength = 12)]
        public string NewPassword { get; set; } = string.Empty;
        // 司機自行設定的正式密碼
        // 驗證成功後使用 PasswordHasher Hash，覆蓋 DB passwordhash
    }
}

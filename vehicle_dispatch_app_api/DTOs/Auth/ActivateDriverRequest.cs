
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_app_api.DTOs.Auth
{
    public class ActivateDriverRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;
        // APP 登入帳號

        [Required]
        [StringLength(128, MinimumLength = 12)]
        public string InitialPassword { get; set; } = string.Empty;
        // 管理者頁面預先建立帳號時提供的初始密碼
        // 外網 API 不驗證密碼，只負責轉送至內網 API
        // 內網 API 使用 ASP.NET Core Identity PasswordHasher
        // 驗證資料庫中的 passwordhash

        [Required]
        [StringLength(16, MinimumLength = 16)]
        [RegularExpression("^[0-9A-Za-z]{16}$")]
        public string ActivationCode { get; set; } = string.Empty;
        // 管理者頁面產生的 16 位 Base36 開通驗證碼
        // 允許字元：0-9、A-Z、a-z
        // 內網 API 會 Trim、轉大寫，再使用 UTF-8 計算 SHA256
        // DB：vehicledriver.activationtokenhash（CHAR(64)）

        [Required]
        [StringLength(128, MinimumLength = 12)]
        public string NewPassword { get; set; } = string.Empty;
        // 司機自行設定的正式密碼
        // 外網 API 不進行 Hash
        // 由內網 API 使用 PasswordHasher 處理後寫入 DB
    }
}

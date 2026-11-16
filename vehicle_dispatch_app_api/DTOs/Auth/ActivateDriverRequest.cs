
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_app_api.DTOs.Auth
{
    public class ActivateDriverRequest
    {
        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;
        // 司機 APP 登入帳號

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string InitialPassword { get; set; } = string.Empty;
        // 管理者頁面預先建立帳號時提供的初始密碼
        // 外網 API 不驗證密碼，只負責安全轉送至內網 API
        // 內網 API 使用 ASP.NET Core Identity PasswordHasher 驗證 DB passwordhash

        [Required]
        [StringLength(25, MinimumLength = 16)]
        [RegularExpression(@"^(?:[0-9A-Za-z]{16}|[0-9A-Za-z]{4}(?:\s*-\s*[0-9A-Za-z]{4}){3})$")]
        public string ActivationCode { get; set; } = string.Empty;
        // 管理者頁面產生的 16 位 Base36 原始開通碼
        // 允許 0-9、A-Z、a-z；內網 API 會 Trim、轉大寫後計算 SHA256
        // DB：vehicledriver.activationtokenhash，CHAR(64)

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string NewPassword { get; set; } = string.Empty;
        // 司機自行設定的正式密碼
        // 外網 API 不進行 Hash；由內網 API 使用 PasswordHasher 處理後寫入 DB

        [Required]
        [RegularExpression("^(Android|iOS)$")]
        public string Platform { get; set; } = string.Empty;
        // 手機作業系統
        // Android / iOS
        // DB：vehicledriverphone.platform

        [Required]
        [StringLength(2048, MinimumLength = 80)]
        public string PublicKey { get; set; } = string.Empty;
        // 手機產生的非對稱金鑰公鑰
        // 私鑰只儲存在手機端，不會傳送給 API
        // 預計使用 ECDSA P-256
        // 格式：Base64 DER SubjectPublicKeyInfo (SPKI)
        // DB：vehicledriverphone.publickey
    }
}


using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class ActivateDriverRequest
    {
        // ==========================================
        // 1. APP 登入帳號
        // ==========================================

        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;

        // DB：vehicledriver.loginid
        // 管理員預先建立的司機登入帳號


        // ==========================================
        // 2. 管理員提供的初始密碼
        // ==========================================

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string InitialPassword { get; set; } = string.Empty;

        // 管理員建立司機帳號時設定的初始密碼
        // API 使用 PasswordHasher.VerifyHashedPassword()
        // 驗證 DB：vehicledriver.passwordhash


        // ==========================================
        // 3. 首次開通驗證碼
        // ==========================================

        [Required]
        [StringLength(25, MinimumLength = 16)]
        [RegularExpression(@"^(?:[0-9A-Za-z]{16}|[0-9A-Za-z]{4}(?:\s*-\s*[0-9A-Za-z]{4}){3})$")]
        public string ActivationCode { get; set; } = string.Empty;

        // 原始開通驗證碼：16 位 Base36
        // 允許字元：0-9、A-Z、a-z
        // API 會 Trim + ToUpperInvariant + SHA256
        // DB：vehicledriver.activationtokenhash
        // 儲存格式：CHAR(64)，64 位 Hex


        // ==========================================
        // 4. 司機自行設定的新密碼
        // ==========================================

        [Required]
        [StringLength(128, MinimumLength = 8)]
        public string NewPassword { get; set; } = string.Empty;

        // 司機開通時自行設定的正式密碼
        // API 使用 PasswordHasher Hash 後寫入 DB
        // DB：vehicledriver.passwordhash


        // ==========================================
        // 5. 手機平台
        // ==========================================

        [Required]
        [RegularExpression("^(Android|iOS)$")]
        public string Platform { get; set; } = string.Empty;

        // DB：vehicledriverphone.platform
        // 允許：Android / iOS
        // SQL：VARCHAR(20)


        // ==========================================
        // 6. 手機公鑰
        // ==========================================

        [Required]
        [StringLength(2048, MinimumLength = 80)]
        public string PublicKey { get; set; } = string.Empty;

        // DB：vehicledriverphone.publickey
        // 儲存手機產生的非對稱公鑰
        // 私鑰只能保留在手機端
        //
        // 預計統一格式：
        // ECDSA P-256
        // Base64 編碼的 DER SubjectPublicKeyInfo (SPKI)
        //
        // 後續 Challenge-Response 使用此公鑰驗證手機簽章
    }
}

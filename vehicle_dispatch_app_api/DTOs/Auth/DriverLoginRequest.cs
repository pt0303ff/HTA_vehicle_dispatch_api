
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_app_api.DTOs.Auth
{
    public class DriverLoginRequest
    {
        // ==========================================
        // 1. APP 登入帳號
        // ==========================================

        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;

        // 司機登入帳號
        // DB：vehicledriver.loginid


        // ==========================================
        // 2. APP 登入密碼
        // ==========================================

        [Required]
        [StringLength(128, MinimumLength = 1)]
        public string Password { get; set; } = string.Empty;

        // APP 傳送原始密碼
        // 外網 API 不驗證密碼
        // 由內網 API 使用 PasswordHasher 驗證


        // ==========================================
        // 3. 手機設備 ID
        // ==========================================

        [Required]
        public Guid? PhoneId { get; set; }

        // 首次開通成功時由內網 API 建立並回傳
        // DB：vehicledriverphone.phone_id
        // 登入時用來確認手機是否屬於該司機
    }
}


using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DriverLoginRequest
    {
        // ==========================================
        // 1. APP 登入帳號
        // ==========================================

        [Required]
        [StringLength(100, MinimumLength = 1)]
        public string LoginId { get; set; } = string.Empty;

        // DB：vehicledriver.loginid
        // 司機登入帳號


        // ==========================================
        // 2. APP 登入密碼
        // ==========================================

        [Required]
        [StringLength(128, MinimumLength = 1)]
        public string Password { get; set; } = string.Empty;

        // DB：vehicledriver.passwordhash
        // APP 傳送原始密碼
        // API 使用 DriverPasswordService.VerifyPassword()
        // 與 DB 儲存的 PasswordHasher Hash 進行驗證
        // 不可將原始密碼寫入 Log 或 DB


        // ==========================================
        // 3. 手機設備 ID
        // ==========================================

        [Required]
        public Guid? PhoneId { get; set; }

        // DB：vehicledriverphone.phone_id
        // 首次開通成功後，API 回傳給 APP 的設備 GUID
        // 登入時用來查詢該手機是否屬於此司機
        // 並確認設備狀態為 Bound
        //
        // 注意：
        // PhoneId 只是設備識別碼
        // 後續 Phase 5-3 還需要 Challenge-Response
        // 驗證手機是否持有對應的私鑰
    }
}

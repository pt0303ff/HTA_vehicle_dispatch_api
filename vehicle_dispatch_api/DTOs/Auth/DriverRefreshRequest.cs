
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DriverRefreshRequest
    {
        // ==========================================
        // Refresh Token
        // ==========================================

        [Required]
        [StringLength(256, MinimumLength = 1)]
        public string RefreshToken { get; set; } = string.Empty;

        // APP 傳送目前持有的 Refresh Token 明碼
        // API 會使用 SHA256 Hash 後與 DB 比對
        // DB：vehicledriverapprefreshtoken.tokenhash
        //
        // 注意：
        // 不可將 Refresh Token 明碼寫入 DB 或 Log
    }
}


using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DriverLogoutRequest
    {
        // ==========================================
        // 登出請求
        // ==========================================

        [Required]
        [StringLength(256, MinimumLength = 1)]
        public string RefreshToken { get; set; } = string.Empty;

        // APP 目前持有的 Refresh Token 明碼
        // API 會轉換成 SHA256 Hash 後查詢 DB
        //
        // DB：vehicledriverapprefreshtoken.tokenhash
    }
}

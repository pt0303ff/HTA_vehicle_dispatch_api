
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_app_api.DTOs.Auth
{
    public class DriverLogoutRequest
    {
        // ==========================================
        // Logout 登出請求
        // ==========================================

        [Required]
        [StringLength(256, MinimumLength = 1)]
        public string RefreshToken { get; set; } = string.Empty;

        // APP 目前持有的 Refresh Token 明碼
        // 內網 API 負責 Hash、查詢及撤銷 Session
    }
}

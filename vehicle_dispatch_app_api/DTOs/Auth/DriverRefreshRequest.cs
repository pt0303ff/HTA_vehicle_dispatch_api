
using System.ComponentModel.DataAnnotations;

namespace vehicle_dispatch_app_api.DTOs.Auth
{
    public class DriverRefreshRequest
    {
        // ==========================================
        // Refresh Token 更新請求
        // ==========================================

        [Required]
        [StringLength(256, MinimumLength = 1)]
        public string RefreshToken { get; set; } = string.Empty;

        // APP 目前持有的 Refresh Token 明碼
        // 外網 API 不進行 Hash
        // 直接轉送至內網 API
    }
}

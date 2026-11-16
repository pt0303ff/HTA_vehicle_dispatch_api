
namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DriverRefreshResponse
    {
        // ==========================================
        // 司機與 Session 資訊
        // ==========================================

        public Guid DriverId { get; set; }

        public Guid SessionId { get; set; }

        public DateTime SessionIdleExpiresAt { get; set; }

        public DateTime SessionAbsoluteExpiresAt { get; set; }


        // ==========================================
        // JWT Access Token
        // ==========================================

        public string TokenType { get; set; } = "Bearer";

        public string AccessToken { get; set; } = string.Empty;

        public DateTime AccessTokenExpiresAt { get; set; }


        // ==========================================
        // Refresh Token
        // ==========================================

        public string RefreshToken { get; set; } = string.Empty;

        public DateTime RefreshTokenExpiresAt { get; set; }

        // 本次更新後的 Token 版本
        public int TokenVersion { get; set; }
    }
}

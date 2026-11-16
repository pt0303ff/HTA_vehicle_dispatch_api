
namespace vehicle_dispatch_api.DTOs.Auth
{
    public class DriverLoginResponse
    {
        // 司機資訊
        public Guid DriverId { get; set; }

        // Session 資訊
        public Guid SessionId { get; set; }

        public DateTime SessionIdleExpiresAt { get; set; }

        public DateTime SessionAbsoluteExpiresAt { get; set; }

        public string AccountStatus { get; set; } = string.Empty;

        // Access Token
        public string TokenType { get; set; } = "Bearer";

        public string AccessToken { get; set; } = string.Empty;

        public DateTime AccessTokenExpiresAt { get; set; }

        // Refresh Token
        public string RefreshToken { get; set; } = string.Empty;

        public DateTime RefreshTokenExpiresAt { get; set; }
    }
}

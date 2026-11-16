
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;

using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class JwtTokenService
    {
        private readonly string _issuer;
        private readonly string _audience;

        private readonly SymmetricSecurityKey _signingKey;

        private readonly TimeSpan _accessTokenLifetime;


        // ==========================================
        // 建構子
        // ==========================================

        public JwtTokenService(IConfiguration configuration)
        {
            // 1. 取得 JWT Issuer

            _issuer = configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException(
                    "找不到 Jwt:Issuer 設定");


            // 2. 取得 JWT Audience

            _audience = configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException(
                    "找不到 Jwt:Audience 設定");


            // 3. 取得簽章金鑰

            var signingKey =
                configuration["Jwt:SigningKey"];

            if (string.IsNullOrWhiteSpace(signingKey))
            {
                throw new InvalidOperationException(
                    "找不到 Jwt:SigningKey 設定");
            }

            byte[] keyBytes;

            try
            {
                keyBytes = Convert.FromBase64String(signingKey);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Jwt:SigningKey 必須是有效的 Base64 字串",
                    ex);
            }

            // 至少 256 bits，也就是 32 Bytes
            if (keyBytes.Length < 32)
            {
                throw new InvalidOperationException(
                    "JWT 簽章金鑰長度至少需要 32 Bytes");
            }

            _signingKey = new SymmetricSecurityKey(keyBytes);


            // 4. Access Token 有效期限

            // 預設 20 分鐘
            // 未來修改 secrets.json 的 Jwt:AccessTokenMinutes
            var minutes =
                configuration.GetValue<int?>(
                    "Jwt:AccessTokenMinutes") ?? 20;

            if (minutes <= 0)
            {
                throw new InvalidOperationException(
                    "Jwt:AccessTokenMinutes 必須大於 0");
            }

            _accessTokenLifetime =
                TimeSpan.FromMinutes(minutes);
        }


        // ==========================================
        // 產生 JWT Access Token
        // ==========================================

        public (string Token, DateTime ExpiresAt)
            CreateAccessToken(
                DriverAppSession session,
                DateTime nowUtc)
        {
            // 1. 計算 Token 到期時間

            var expiresAt =
                nowUtc.Add(_accessTokenLifetime);

            // Access Token 不可以超過 Session 最長期限
            if (expiresAt > session.IdleExpiresAt)
            {
                expiresAt = session.IdleExpiresAt;
            }

            // Access Token 不可以超過 Session 最長期限
            if (expiresAt > session.AbsoluteExpiresAt)
            {
                expiresAt = session.AbsoluteExpiresAt;
            }

            if (expiresAt <= nowUtc)
            {
                throw new InvalidOperationException(
                    "Session 已經到期，無法產生 Access Token");
            }


            // 2. 建立 JWT Claims

            var claims = new List<Claim>
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    session.DriverId.ToString()),

                new Claim(
                    "session_id",
                    session.SessionId.ToString()),

                new Claim(
                    "phone_id",
                    session.PhoneId.ToString()),

                new Claim(
                    JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString("N")),

                new Claim(
                    "token_type",
                    "access")
            };


            // 3. 建立簽章資訊

            var credentials = new SigningCredentials(
                _signingKey,
                SecurityAlgorithms.HmacSha256);


            // 4. 建立 JWT

            var jwt = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: nowUtc,
                expires: expiresAt,
                signingCredentials: credentials);


            // 5. 序列化 JWT

            var token = new JwtSecurityTokenHandler()
                .WriteToken(jwt);


            return (token, expiresAt);
        }
    }
}

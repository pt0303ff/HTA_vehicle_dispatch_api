
using System;
using System.Collections.Generic;
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

        public JwtTokenService(IConfiguration configuration)
        {
            _issuer = configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException(
                    "找不到 Jwt:Issuer 設定");

            _audience = configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException(
                    "找不到 Jwt:Audience 設定");

            var signingKey = configuration["Jwt:SigningKey"];

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

            if (keyBytes.Length < 32)
            {
                throw new InvalidOperationException(
                    "JWT 簽章金鑰長度至少需要 32 Bytes");
            }

            _signingKey = new SymmetricSecurityKey(keyBytes);

            var minutes =
                configuration.GetValue<int?>(
                    "Jwt:AccessTokenMinutes") ?? 20;

            if (minutes <= 0)
            {
                throw new InvalidOperationException(
                    "Jwt:AccessTokenMinutes 必須大於 0");
            }

            _accessTokenLifetime = TimeSpan.FromMinutes(minutes);
        }

        // ==========================================
        // 產生 JWT Access Token
        // ==========================================

        public (string Token, DateTime ExpiresAt)
            CreateAccessToken(
                DriverAppSession session,
                DateTime nowUtc,
                string dpopThumbprint)
        {
            if (string.IsNullOrWhiteSpace(dpopThumbprint))
            {
                throw new ArgumentException(
                    "DPoP Thumbprint 不可為空",
                    nameof(dpopThumbprint));
            }

            var expiresAt = nowUtc.Add(_accessTokenLifetime);

            if (expiresAt > session.IdleExpiresAt)
                expiresAt = session.IdleExpiresAt;

            if (expiresAt > session.AbsoluteExpiresAt)
                expiresAt = session.AbsoluteExpiresAt;

            if (expiresAt <= nowUtc)
            {
                throw new InvalidOperationException(
                    "Session 已經到期，無法產生 Access Token");
            }

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
                    "access"),

                // RFC 9449 DPoP Key Confirmation
                new Claim(
                    "cnf",
                    System.Text.Json.JsonSerializer.Serialize(
                        new Dictionary<string, string>
                        {
                            ["jkt"] = dpopThumbprint
                        }),
                    JsonClaimValueTypes.Json)
            };

            var credentials = new SigningCredentials(
                _signingKey,
                SecurityAlgorithms.HmacSha256);

            var jwt = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: nowUtc,
                expires: expiresAt,
                signingCredentials: credentials);

            var token = new JwtSecurityTokenHandler()
                .WriteToken(jwt);

            return (token, expiresAt);
        }
    }
}

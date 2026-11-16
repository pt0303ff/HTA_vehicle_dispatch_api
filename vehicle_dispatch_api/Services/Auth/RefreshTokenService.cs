
using System.Security.Cryptography;
using System.Text;

namespace vehicle_dispatch_api.Services.Auth
{
    public class RefreshTokenService
    {
        // ==========================================
        // 1. 產生 Refresh Token
        // ==========================================

        public (string Token, string Hash) CreateToken()
        {
            // 使用密碼學安全亂數產生器
            // 產生 64 Bytes 隨機資料

            byte[] randomBytes =
                RandomNumberGenerator.GetBytes(64);


            // 轉換為 Base64Url
            // 適合放在 HTTP JSON 及 Header 中

            string token = Base64UrlEncode(randomBytes);


            // 產生 SHA256 Hash
            string hash = HashToken(token);


            // 回傳：
            // Token：原始明碼，只回傳給 APP
            // Hash：寫入資料庫

            return (token, hash);
        }


        // ==========================================
        // 2. Refresh Token SHA256 Hash
        // ==========================================

        public string HashToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException(
                    "Refresh Token 不可為空");
            }

            byte[] hashBytes = SHA256.HashData(
                Encoding.UTF8.GetBytes(token));

            // 回傳 64 位十六進位字串
            return Convert.ToHexString(hashBytes);
        }


        // ==========================================
        // 3. Base64Url 編碼
        // ==========================================

        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}

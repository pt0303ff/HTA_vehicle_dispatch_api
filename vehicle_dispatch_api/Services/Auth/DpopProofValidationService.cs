//這個服務負責驗證：
//DPoP JWT 格式。
//Header 的 typ 是否為 dpop+jwt。
//簽章演算法是否為 ES256。
// 公鑰是否與資料庫綁定公鑰一致。
// jti 是否有效。
// iat 是否在允許時間內。
// htm、htu 是否符合實際請求。
// Nonce 是否有效。


using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace vehicle_dispatch_api.Services.Auth
{
    public sealed class DpopProofValidationService
    {
        private readonly DpopNonceService _nonceService;
        private readonly IMemoryCache _cache;

        // 所有 DPoP 驗證請求共用同一把鎖
        private readonly object _validationLock = new();

        private static readonly TimeSpan AllowedClockSkew =
            TimeSpan.FromSeconds(60);

        // iat 最多容許未來 60 秒，
        // 因此防重播紀錄保留 120 秒。
        private static readonly TimeSpan ReplayLifetime =
            TimeSpan.FromSeconds(120);

        private const string ReplayCachePrefix =
            "dpop:replay:";

        private const int MaxProofLength = 16384;

        public DpopProofValidationService(
            DpopNonceService nonceService,
            IMemoryCache cache)
        {
            _nonceService = nonceService;
            _cache = cache;
        }


        // ==========================================
        // 原本 Login / Refresh 使用的驗證入口
        // 不要求 ath，維持舊流程相容
        // ==========================================

        public bool ValidateProof(
            string proof,
            string storedPublicKeyBase64,
            Guid phoneId,
            string expectedMethod,
            string expectedUri,
            out string thumbprint,
            out string error)
        {
            return ValidateProofCore(
                proof,
                storedPublicKeyBase64,
                phoneId,
                expectedMethod,
                expectedUri,
                null,
                null,
                out thumbprint,
                out error);
        }


        // ==========================================
        // Phase 8-4：資源 API 專用驗證入口
        // 必須驗證 Access Token 綁定
        // ==========================================

        public bool ValidateAccessTokenProof(
            string proof,
            string storedPublicKeyBase64,
            Guid phoneId,
            string expectedMethod,
            string expectedUri,
            string accessToken,
            string expectedJkt,
            out string thumbprint,
            out string error)
        {
            if (string.IsNullOrWhiteSpace(accessToken) ||
                string.IsNullOrWhiteSpace(expectedJkt))
            {
                thumbprint = string.Empty;
                error = "缺少 Access Token 或 DPoP Thumbprint";
                return false;
            }

            return ValidateProofCore(
                proof,
                storedPublicKeyBase64,
                phoneId,
                expectedMethod,
                expectedUri,
                accessToken,
                expectedJkt,
                out thumbprint,
                out error);
        }


        // ==========================================
        // 驗證 DPoP Proof
        // ==========================================

        private bool ValidateProofCore(
            string proof,
            string storedPublicKeyBase64,
            Guid phoneId,
            string expectedMethod,
            string expectedUri,
            string? accessToken,
            string? expectedJkt,
            out string thumbprint,
            out string error)
        {
            thumbprint = string.Empty;
            error = string.Empty;

            try
            {
                // 1. 基本檢查
                if (string.IsNullOrWhiteSpace(proof) ||
                    proof.Length > MaxProofLength ||
                    string.IsNullOrWhiteSpace(storedPublicKeyBase64) ||
                    phoneId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(expectedMethod) ||
                    string.IsNullOrWhiteSpace(expectedUri))
                {
                    return Reject("缺少必要驗證資料", out error);
                }

                // htu 必須是公開 HTTPS URL
                var isDevelopment =
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")== "Development";
                if (!Uri.TryCreate(
                        expectedUri,
                        UriKind.Absolute,
                        out var expectedUriObject) ||
                    (expectedUriObject.Scheme != Uri.UriSchemeHttps &&
                     !(isDevelopment &&
                       expectedUriObject.Scheme == Uri.UriSchemeHttp)))
                {
                    return Reject("伺服器 DPoP URL 設定不正確", out error);
                }

                // 2. 解析 JWT
                var handler = new JwtSecurityTokenHandler
                {
                    MapInboundClaims = false
                };

                if (!handler.CanReadToken(proof))
                {
                    return Reject("DPoP JWT 格式錯誤", out error);
                }

                var jwt = handler.ReadJwtToken(proof);

                // 3. 驗證 Header
                if (!string.Equals(
                    jwt.Header.Typ,
                    "dpop+jwt",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Reject("DPoP typ 不正確", out error);
                }

                if (jwt.Header.Alg != SecurityAlgorithms.EcdsaSha256)
                {
                    return Reject("DPoP 必須使用 ES256", out error);
                }

                if (!jwt.Header.TryGetValue("jwk", out var jwkValue))
                {
                    return Reject("缺少 DPoP 公鑰", out error);
                }

                // 不接受未支援的 critical header
                if (jwt.Header.ContainsKey("crit") ||
                    jwt.Header.ContainsKey("b64"))
                {
                    return Reject("DPoP Header 包含不支援的參數", out error);
                }

                using var jwkDocument = JsonDocument.Parse(
                    JsonSerializer.Serialize(jwkValue));

                var jwk = jwkDocument.RootElement;

                if (jwk.ValueKind != JsonValueKind.Object)
                {
                    return Reject("JWK 格式錯誤", out error);
                }

                string kty = GetString(jwk, "kty");
                string crv = GetString(jwk, "crv");
                string x = GetString(jwk, "x");
                string y = GetString(jwk, "y");

                if (kty != "EC" || crv != "P-256")
                {
                    return Reject("僅允許 EC P-256 公鑰", out error);
                }

                // 公開 JWK 不可以包含私鑰
                if (jwk.TryGetProperty("d", out _))
                {
                    return Reject("DPoP 公鑰不可包含私鑰", out error);
                }

                if (string.IsNullOrWhiteSpace(x) ||
                    string.IsNullOrWhiteSpace(y))
                {
                    return Reject("JWK 公鑰座標缺失", out error);
                }

                byte[] xBytes = Base64UrlEncoder.DecodeBytes(x);
                byte[] yBytes = Base64UrlEncoder.DecodeBytes(y);

                if (xBytes.Length != 32 || yBytes.Length != 32)
                {
                    return Reject("P-256 公鑰座標長度錯誤", out error);
                }

                // 4. 讀取資料庫 SPKI 公鑰
                byte[] storedKey =
                    Convert.FromBase64String(storedPublicKeyBase64);

                using var storedEcdsa = ECDsa.Create();

                storedEcdsa.ImportSubjectPublicKeyInfo(
                    storedKey,
                    out int bytesRead);

                if (bytesRead != storedKey.Length)
                {
                    return Reject("資料庫公鑰格式錯誤", out error);
                }

                var storedParameters =
                    storedEcdsa.ExportParameters(false);

                // 確認資料庫公鑰確實為 P-256
                if (storedParameters.Curve.Oid.Value !=
                    ECCurve.NamedCurves.nistP256.Oid.Value)
                {
                    return Reject("資料庫設備公鑰不是 P-256", out error);
                }

                if (storedParameters.Q.X == null ||
                    storedParameters.Q.Y == null ||
                    storedParameters.Q.X.Length != 32 ||
                    storedParameters.Q.Y.Length != 32)
                {
                    return Reject("資料庫設備公鑰座標錯誤", out error);
                }

                // 5. 比對資料庫公鑰與 DPoP JWK
                bool xMatches =
                    CryptographicOperations.FixedTimeEquals(
                        storedParameters.Q.X,
                        xBytes);

                bool yMatches =
                    CryptographicOperations.FixedTimeEquals(
                        storedParameters.Q.Y,
                        yBytes);

                if (!xMatches || !yMatches)
                {
                    return Reject("設備公鑰與資料庫不一致", out error);
                }


                // ==========================================
                // 6. 使用資料庫 SPKI 公鑰直接驗證 ES256
                // ==========================================

                // ES256 JWT 必須使用 JOSE 格式：R(32) + S(32)
                // .NET ECDsa 預設簽章格式需要明確指定。

                var jwtParts = proof.Split('.');

                if (jwtParts.Length != 3)
                {
                    return Reject("DPoP JWT 格式錯誤", out error);
                }

                byte[] signingInput = Encoding.ASCII.GetBytes(
                    jwtParts[0] + "." + jwtParts[1]);

                byte[] joseSignature =
                    Base64UrlEncoder.DecodeBytes(jwtParts[2]);

                if (joseSignature.Length != 64)
                {
                    return Reject(
                        $"ES256 簽章長度錯誤：{joseSignature.Length}",
                        out error);
                }

                bool signatureValid = storedEcdsa.VerifyData(
                    signingInput,
                    joseSignature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

                if (!signatureValid)
                {
                    return Reject(
                        "ES256 簽章與資料庫設備公鑰不匹配",
                        out error);
                }


                // 7. 計算 RFC 7638 JWK Thumbprint
                // 使用標準化後的 Base64URL 座標
                string canonicalX = Base64UrlEncoder.Encode(xBytes);
                string canonicalY = Base64UrlEncoder.Encode(yBytes);

                string canonicalJwk =
                    "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" +
                    canonicalX +
                    "\",\"y\":\"" +
                    canonicalY +
                    "\"}";

                byte[] thumbprintHash = SHA256.HashData(
                    Encoding.UTF8.GetBytes(canonicalJwk));

                thumbprint = Base64UrlEncoder.Encode(thumbprintHash);


                // ==========================================
                // Phase 8-4：Access Token 綁定驗證
                // ==========================================

                // Login / Refresh 不提供 Access Token
                // 因此只在資源 API 請求時執行。

                if (accessToken != null && expectedJkt != null)
                {
                    // 1. 驗證 cnf.jkt
                    // 確認 Proof 使用的設備公鑰
                    // 與 Access Token 綁定的公鑰相同。

                    if (!FixedTimeEqualsText(
                        thumbprint,
                        expectedJkt))
                    {
                        return Reject(
                            "DPoP 公鑰與 Access Token 綁定不一致",
                            out error);
                    }

                    // 2. 計算 Access Token SHA256

                    byte[] accessTokenHash = SHA256.HashData(
                        Encoding.ASCII.GetBytes(accessToken));

                    string expectedAth =
                        Base64UrlEncoder.Encode(accessTokenHash);

                    // 3. 取得 DPoP Payload ath

                    string actualAth = GetClaim(jwt, "ath");

                    if (string.IsNullOrWhiteSpace(actualAth))
                    {
                        return Reject(
                            "DPoP Proof 缺少 ath",
                            out error);
                    }

                    // 4. 比對 ath

                    if (!FixedTimeEqualsText(
                        expectedAth,
                        actualAth))
                    {
                        return Reject(
                            "Access Token ath 驗證失敗",
                            out error);
                    }
                }


                // 8. 驗證 Payload
                string jti = GetClaim(jwt, "jti");
                string htm = GetClaim(jwt, "htm");
                string htu = GetClaim(jwt, "htu");
                string nonce = GetClaim(jwt, "nonce");

                if (string.IsNullOrWhiteSpace(jti) ||
                    jti.Length > 200)
                {
                    return Reject("jti 無效", out error);
                }

                if (!jwt.Payload.TryGetValue("iat", out var iatValue))
                {
                    return Reject("缺少 iat", out error);
                }

                if (!long.TryParse(
                    Convert.ToString(iatValue, CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long issuedAt))
                {
                    return Reject("iat 格式錯誤", out error);
                }

                DateTime issuedAtUtc;

                try
                {
                    issuedAtUtc =
                        DateTimeOffset.FromUnixTimeSeconds(
                            issuedAt).UtcDateTime;
                }
                catch
                {
                    return Reject("iat 超出有效時間範圍", out error);
                }

                var now = DateTime.UtcNow;

                if ((now - issuedAtUtc) > AllowedClockSkew ||
                    (issuedAtUtc - now) > AllowedClockSkew)
                {
                    return Reject("DPoP Proof 已過期或時間錯誤", out error);
                }

                // 9. 驗證 HTTP Method
                if (!string.Equals(
                    htm,
                    expectedMethod.ToUpperInvariant(),
                    StringComparison.Ordinal))
                {
                    return Reject("HTTP Method 不一致", out error);
                }

                // 10. 驗證 HTTP URI
                //if (!string.Equals(
                //    htu,
                //    expectedUri,
                //    StringComparison.Ordinal))
                //{
                //    return Reject("HTTP URI 不一致", out error);
                //}

                if (!string.Equals(
                    htu,
                    expectedUri,
                    StringComparison.Ordinal))
                {
                    Console.WriteLine("========== DPoP URI DEBUG ==========");
                    Console.WriteLine($"Proof htu : [{htu}]");
                    Console.WriteLine($"Expected  : [{expectedUri}]");
                    Console.WriteLine("====================================");

                    return Reject("HTTP URI 不一致", out error);
                }

                if (string.IsNullOrWhiteSpace(nonce))
                {
                    return Reject("缺少 Nonce", out error);
                }

                // 11. 原子防重播及 Nonce 消耗
                // 本服務必須註冊為 Singleton。
                // 同一 API Server 的所有驗證共用此鎖。

                string replayKey =
                    ReplayCachePrefix + thumbprint + ":" + jti;

                lock (_validationLock)
                {
                    // 先檢查 jti 是否已使用
                    if (_cache.TryGetValue(replayKey, out _))
                    {
                        return Reject("DPoP Proof 已重複使用", out error);
                    }

                    // 再消耗 Nonce
                    if (!_nonceService.TryConsumeNonce(
                        nonce,
                        phoneId))
                    {
                        return Reject(
                            "Nonce 無效、過期或已使用",
                            out error);
                    }

                    // Nonce 成功消耗後才登記 jti
                    _cache.Set(
                        replayKey,
                        true,
                        new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow =
                                ReplayLifetime
                        });
                }

                return true;
            }
            catch (FormatException)
            {
                return Reject("公鑰或 JWK 編碼格式錯誤", out error);
            }
            catch (CryptographicException)
            {
                return Reject("DPoP 簽章或公鑰驗證失敗", out error);
            }
            catch (SecurityTokenException)
            {
                return Reject("DPoP JWT 簽章驗證失敗", out error);
            }
            catch (Exception)
            {
                // 不將內部例外內容回傳給 APP
                return Reject("DPoP 驗證發生未預期錯誤", out error);
            }
        }

        // ==========================================
        // 固定時間字串比對
        // 避免一般字串比較造成時間差異
        // ==========================================

        private static bool FixedTimeEqualsText(
            string expected,
            string actual)
        {
            if (string.IsNullOrEmpty(expected) ||
                string.IsNullOrEmpty(actual))
            {
                return false;
            }

            byte[] expectedBytes =
                Encoding.UTF8.GetBytes(expected);

            byte[] actualBytes =
                Encoding.UTF8.GetBytes(actual);

            if (expectedBytes.Length != actualBytes.Length)
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                expectedBytes,
                actualBytes);
        }


        // ==========================================
        // 統一錯誤回傳
        // ==========================================

        private static bool Reject(
            string message,
            out string error)
        {
            error = message;
            return false;
        }

        // ==========================================
        // 讀取 JWK JSON 字串
        // ==========================================

        private static string GetString(
            JsonElement element,
            string name)
        {
            if (!element.TryGetProperty(name, out var value))
            {
                return string.Empty;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return value.GetString() ?? string.Empty;
        }

        // ==========================================
        // 讀取 JWT Payload
        // ==========================================

        private static string GetClaim(
            JwtSecurityToken jwt,
            string name)
        {
            return jwt.Payload.TryGetValue(name, out var value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }
    }
}




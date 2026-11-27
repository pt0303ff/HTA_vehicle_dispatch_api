
using System.Data;
using Microsoft.EntityFrameworkCore;

using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.DTOs.Auth;
using vehicle_dispatch_api.Models.Auth;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace vehicle_dispatch_api.Services.Auth
{
    public class DriverRefreshService
    {
        private readonly ApplicationDbContext _db;
        private readonly RefreshTokenService _refreshTokenService;
        private readonly JwtTokenService _jwtTokenService;
        private readonly DpopProofValidationService _dpopProofValidationService;


        // ==========================================
        // 建構子
        // ==========================================

        public DriverRefreshService(
            ApplicationDbContext db,
            RefreshTokenService refreshTokenService,
            JwtTokenService jwtTokenService,
            DpopProofValidationService dpopProofValidationService)
        {
            _db = db;
            _refreshTokenService = refreshTokenService;
            _jwtTokenService = jwtTokenService;
            _dpopProofValidationService = dpopProofValidationService;
        }


        // ==========================================
        // Refresh Token 更新
        // ==========================================

        public async Task<DriverRefreshResponse?> RefreshAsync(
            string refreshToken,
            string dpopProof,
            string expectedUri,
            CancellationToken cancellationToken = default)
        {
            // ==========================================
            // 1. 基本資料檢查
            // ==========================================

            if (string.IsNullOrWhiteSpace(refreshToken) ||
                refreshToken.Length > 256 ||
                string.IsNullOrWhiteSpace(dpopProof) ||
                string.IsNullOrWhiteSpace(expectedUri))
            {
                return null;
            }

            // 將 APP 傳入的明碼轉換成 SHA256 Hash
            var tokenHash =
                _refreshTokenService.HashToken(refreshToken);


            // ==========================================
            // 2. 開始資料庫交易
            // ==========================================

            // 使用 Serializable 確保更新過程的交易隔離
            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            var now = DateTime.UtcNow;


            // ==========================================
            // 3. 查詢 Refresh Token
            // ==========================================

            // SQL Server 使用 UPDLOCK + HOLDLOCK
            // 避免同一 Refresh Token 被同時更新
            //
            // 同時載入所屬 Session 及手機設備

            var currentToken = await _db.DriverAppRefreshTokens
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM dbo.vehicledriverapprefreshtoken
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE tokenhash = {tokenHash}")
                .Include(x => x.Session)
                .ThenInclude(x => x.Phone)
                .SingleOrDefaultAsync(cancellationToken);


            // Token 不存在
            if (currentToken == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            // ==========================================
            // 鎖定 Session，避免 Refresh / Logout 同時執行
            // ==========================================

            var session = await _db.DriverAppSessions
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM dbo.vehicledriverappsession
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE session_id = {currentToken.SessionId}")
                .SingleOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }

            // ==========================================
            // 4. DPoP 設備持有權驗證
            // ==========================================

            // 從 Refresh Token 關聯的 Session 取得原設備
            // 不接受 APP 自行指定公鑰或 Phone ID。

            var phone = currentToken.Session?.Phone;

            if (phone == null ||
                string.IsNullOrWhiteSpace(phone.PublicKey))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }

            // 使用資料庫內的公鑰驗證 DPoP Proof
            bool dpopValid = _dpopProofValidationService.ValidateProof(
                dpopProof,
                phone.PublicKey,
                session.PhoneId,
                "POST",
                expectedUri,
                out var dpopThumbprint,
                out var dpopError);

            if (!dpopValid)
            {
                // DPoP 失敗不能修改或撤銷 Refresh Token
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }
            // ==========================================
            // 4. 檢查 Token 是否已經使用過
            // ==========================================

            // 如果舊 Token 再次被使用
            // 代表可能發生 Refresh Token 重放攻擊
            //
            // 撤銷整個 Session，包含目前有效的 Token

            if (currentToken.RevokedAt.HasValue)
            {
                await RevokeSessionAsync(
                    session,
                    "RefreshTokenReuse",
                    now,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return null;
            }


            // ==========================================
            // 5. 檢查 Session 狀態
            // ==========================================

            // Session 已經被登出或撤銷
            if (session.RevokedAt.HasValue)
            {
                await RevokeSessionAsync(
                    session,
                    "SessionRevoked",
                    now,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return null;
            }


            // 閒置期限或最長期限已過
            if (session.IdleExpiresAt <= now ||
                session.AbsoluteExpiresAt <= now)
            {
                await RevokeSessionAsync(
                    session,
                    "SessionExpired",
                    now,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return null;
            }


            // ==========================================
            // 6. 檢查 Refresh Token 有效期限
            // ==========================================

            if (currentToken.ExpiresAt <= now)
            {
                currentToken.RevokedAt = now;

                await _db.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return null;
            }


            // ==========================================
            // 7. 確認司機帳號仍然有效
            // ==========================================

            bool accountActive =
                await _db.DriverAppAccounts
                    .AnyAsync(
                        x =>
                            x.DriverId == session.DriverId &&
                            x.AccountStatus == "Active",
                        cancellationToken);

            if (!accountActive)
            {
                await RevokeSessionAsync(
                    session,
                    "AccountDisabled",
                    now,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return null;
            }


            // ==========================================
            // 8. 確認手機設備仍然有效
            // ==========================================

            if (phone.Status != DriverAppPhone.StatusBound)
            {
                await RevokeSessionAsync(
                    session,
                    "PhoneDisabled",
                    now,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                return null;
            }


            // ==========================================
            // 9. 更新 Session 閒置期限
            // ==========================================

            // 每次成功 Refresh 都代表使用者仍在使用 APP
            // 因此重新計算 7 天閒置期限

            var newIdleExpiresAt =
                now.Add(DriverTokenSettings.SessionIdleTimeout);

            // 不可以超過 Session 最長期限（30 天）
            if (newIdleExpiresAt > session.AbsoluteExpiresAt)
            {
                newIdleExpiresAt = session.AbsoluteExpiresAt;
            }

            session.LastUsedAt = now;

            session.IdleExpiresAt = newIdleExpiresAt;


            // ==========================================
            // 10. 產生新的 JWT Access Token
            // ==========================================

            // 從設備公鑰計算 RFC 7638 JWK Thumbprint
            

            // 產生綁定原設備金鑰的 JWT
            var accessTokenResult =
                _jwtTokenService.CreateAccessToken(
                    session,
                    now,
                    dpopThumbprint);


            // ==========================================
            // 11. 產生新的 Refresh Token
            // ==========================================

            var newRefreshTokenResult =
                _refreshTokenService.CreateToken();

            // 新 Refresh Token 預設 7 天
            var newRefreshExpiresAt =
                now.Add(DriverTokenSettings.RefreshTokenLifetime);

            // 不可以超過 Session 閒置期限
            if (newRefreshExpiresAt > session.IdleExpiresAt)
            {
                newRefreshExpiresAt = session.IdleExpiresAt;
            }

            // 不可以超過 Session 最長期限
            if (newRefreshExpiresAt > session.AbsoluteExpiresAt)
            {
                newRefreshExpiresAt = session.AbsoluteExpiresAt;
            }


            // ==========================================
            // 12. 建立新的 Refresh Token DB 紀錄
            // ==========================================

            var newToken = new DriverAppRefreshToken
            {
                TokenId = Guid.NewGuid(),

                SessionId = session.SessionId,

                // DB 只儲存 SHA256 Hash
                TokenHash = newRefreshTokenResult.Hash,

                ExpiresAt = newRefreshExpiresAt,

                RevokedAt = null,

                ReplacedByTokenId = null,

                CreatedAt = now,

                // 舊版本 + 1
                TokenVersion = checked(currentToken.TokenVersion + 1),

                Session = session
            };


            // ==========================================
            // 13. 撤銷舊 Refresh Token
            // ==========================================

            currentToken.RevokedAt = now;

            // 先儲存舊 Token 撤銷及 Session 更新
            // 避免違反 DB 同一 Session 僅能有一筆
            // 未撤銷 Token 的唯一索引

            await _db.SaveChangesAsync(cancellationToken);


            // ==========================================
            // 14. 寫入新的 Refresh Token
            // ==========================================

            _db.DriverAppRefreshTokens.Add(newToken);

            await _db.SaveChangesAsync(cancellationToken);


            // ==========================================
            // 15. 記錄新舊 Token 關聯
            // ==========================================

            currentToken.ReplacedByTokenId = newToken.TokenId;

            await _db.SaveChangesAsync(cancellationToken);


            // ==========================================
            // 16. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(cancellationToken);


            // ==========================================
            // 17. 回傳新的 Token
            // ==========================================

            return new DriverRefreshResponse
            {
                DriverId = session.DriverId,

                SessionId = session.SessionId,

                SessionIdleExpiresAt = session.IdleExpiresAt,

                SessionAbsoluteExpiresAt = session.AbsoluteExpiresAt,

                // Access Token
                TokenType = "Bearer",

                AccessToken = accessTokenResult.Token,

                AccessTokenExpiresAt = accessTokenResult.ExpiresAt,

                // Refresh Token
                RefreshToken = newRefreshTokenResult.Token,

                RefreshTokenExpiresAt = newRefreshExpiresAt,

                TokenVersion = newToken.TokenVersion
            };
        }


        // ==========================================
        // 撤銷整個 Session
        // ==========================================

        private async Task RevokeSessionAsync(
            DriverAppSession session,
            string reason,
            DateTime now,
            CancellationToken cancellationToken)
        {
            // 記錄 Session 撤銷原因
            if (!session.RevokedAt.HasValue)
            {
                session.RevokedAt = now;
                session.RevokedReason = reason;
            }

            // 撤銷此 Session 所有尚未撤銷的 Refresh Token
            var activeTokens =
                await _db.DriverAppRefreshTokens
                    .Where(x =>
                        x.SessionId == session.SessionId &&
                        x.RevokedAt == null)
                    .ToListAsync(cancellationToken);

            foreach (var token in activeTokens)
            {
                token.RevokedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }
        // ==========================================
        // 計算 RFC 7638 JWK Thumbprint
        // ==========================================

        private static string GetPublicKeyThumbprint(
            string publicKeyBase64)
        {
            if (string.IsNullOrWhiteSpace(publicKeyBase64))
            {
                throw new InvalidOperationException(
                    "設備公鑰不存在");
            }

            byte[] publicKeyBytes =
                Convert.FromBase64String(publicKeyBase64);

            using var ecdsa = ECDsa.Create();

            ecdsa.ImportSubjectPublicKeyInfo(
                publicKeyBytes,
                out int bytesRead);

            if (bytesRead != publicKeyBytes.Length)
            {
                throw new CryptographicException(
                    "設備公鑰格式不正確");
            }

            var parameters = ecdsa.ExportParameters(false);

            if (parameters.Q.X == null ||
                parameters.Q.Y == null ||
                parameters.Q.X.Length != 32 ||
                parameters.Q.Y.Length != 32)
            {
                throw new CryptographicException(
                    "設備公鑰不是有效的 P-256 公鑰");
            }

            string x = Base64UrlEncoder.Encode(parameters.Q.X);
            string y = Base64UrlEncoder.Encode(parameters.Q.Y);

            // RFC 7638 規定的 JWK 欄位順序
            string canonicalJwk =
                "{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"" +
                x +
                "\",\"y\":\"" +
                y +
                "\"}";

            byte[] hash = SHA256.HashData(
                Encoding.UTF8.GetBytes(canonicalJwk));

            return Base64UrlEncoder.Encode(hash);
        }
    }


}


using System.Data;

using Microsoft.EntityFrameworkCore;

using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class DriverLogoutService
    {
        private readonly ApplicationDbContext _db;
        private readonly RefreshTokenService _refreshTokenService;

        // ==========================================
        // 建構子
        // ==========================================

        public DriverLogoutService(
            ApplicationDbContext db,
            RefreshTokenService refreshTokenService)
        {
            _db = db;
            _refreshTokenService = refreshTokenService;
        }

        // ==========================================
        // 司機登出
        // JWT 身分與 Refresh Token 必須一致
        // ==========================================

        public async Task<bool> LogoutAsync(
            string refreshToken,
            Guid driverId,
            Guid sessionId,
            Guid phoneId,
            CancellationToken cancellationToken = default)
        {
            // ==========================================
            // 1. 基本資料檢查
            // ==========================================

            if (string.IsNullOrWhiteSpace(refreshToken) ||
                refreshToken.Length > 256 ||
                driverId == Guid.Empty ||
                sessionId == Guid.Empty ||
                phoneId == Guid.Empty)
            {
                return false;
            }

            // ==========================================
            // 2. Refresh Token SHA256 Hash
            // ==========================================

            var tokenHash =
                _refreshTokenService.HashToken(refreshToken);

            // ==========================================
            // 3. 開始資料庫交易
            // ==========================================

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);

            var now = DateTime.UtcNow;

            // ==========================================
            // 4. 查詢並鎖定 Refresh Token
            // ==========================================

            var token = await _db.DriverAppRefreshTokens
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM dbo.vehicledriverapprefreshtoken
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE tokenhash = {tokenHash}")
                .SingleOrDefaultAsync(cancellationToken);

            if (token == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 5. 確認 Refresh Token 所屬 Session
            // ==========================================

            // 必須與目前 JWT 的 Session ID 相同
            if (token.SessionId != sessionId)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 6. 檢查 Token 狀態
            // ==========================================

            if (token.RevokedAt.HasValue)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            if (token.ExpiresAt <= now)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 7. 查詢並鎖定 Session
            // ==========================================

            var session = await _db.DriverAppSessions
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM dbo.vehicledriverappsession
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE session_id = {token.SessionId}")
                .SingleOrDefaultAsync(cancellationToken);

            if (session == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 8. 確認 JWT 與 Session 身分一致
            // ==========================================

            if (session.DriverId != driverId ||
                session.SessionId != sessionId ||
                session.PhoneId != phoneId)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 9. 檢查 Session 狀態
            // ==========================================

            if (session.RevokedAt.HasValue)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            if (session.IdleExpiresAt <= now ||
                session.AbsoluteExpiresAt <= now)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 10. 確認此 Token 是目前有效的 Token
            // ==========================================

            var activeToken =
                await _db.DriverAppRefreshTokens
                    .FromSqlInterpolated($@"
                        SELECT *
                        FROM dbo.vehicledriverapprefreshtoken
                            WITH (UPDLOCK, HOLDLOCK)
                        WHERE session_id = {session.SessionId}
                          AND revoked_at IS NULL
                          AND expiresat > {now}")
                    .SingleOrDefaultAsync(cancellationToken);

            if (activeToken == null ||
                activeToken.TokenId != token.TokenId)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }

            // ==========================================
            // 11. 撤銷 Session
            // ==========================================

            session.RevokedAt = now;
            session.RevokedReason = "UserLogout";

            // ==========================================
            // 12. 撤銷此 Session 所有有效 Refresh Token
            // ==========================================

            var activeTokens =
                await _db.DriverAppRefreshTokens
                    .Where(x =>
                        x.SessionId == session.SessionId &&
                        x.RevokedAt == null)
                    .ToListAsync(cancellationToken);

            foreach (var activeTokenItem in activeTokens)
            {
                activeTokenItem.RevokedAt = now;
            }

            // ==========================================
            // 13. 寫入資料庫
            // ==========================================

            await _db.SaveChangesAsync(
                cancellationToken);

            // ==========================================
            // 14. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(
                cancellationToken);

            // ==========================================
            // 15. 登出成功
            // ==========================================

            return true;
        }
    }
}

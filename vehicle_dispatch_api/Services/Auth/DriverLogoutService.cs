
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
        // ==========================================

        public async Task<bool> LogoutAsync(
            string refreshToken,
            CancellationToken cancellationToken = default)
        {
            // ==========================================
            // 1. 基本資料檢查
            // ==========================================

            if (string.IsNullOrWhiteSpace(refreshToken) ||
                refreshToken.Length > 256)
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
            // 4. 查詢 Refresh Token
            // ==========================================

            // UPDLOCK + HOLDLOCK
            // 避免同一筆 Token 同時被其他交易修改

            var token = await _db.DriverAppRefreshTokens
                .FromSqlInterpolated($@"
                    SELECT *
                    FROM dbo.vehicledriverapprefreshtoken
                        WITH (UPDLOCK, HOLDLOCK)
                    WHERE tokenhash = {tokenHash}")
                .SingleOrDefaultAsync(cancellationToken);


            // 找不到 Token
            if (token == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }


            // ==========================================
            // 4-1. 檢查 Token 是否已經撤銷
            // ==========================================

            // 舊版 Token 已經被 Refresh Token 輪替
            // 不允許使用已撤銷的 Token 再次登出

            if (token.RevokedAt.HasValue)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }


            // ==========================================
            // 4-2. 檢查 Refresh Token 是否過期
            // ==========================================

            if (token.ExpiresAt <= now)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }


            // ==========================================
            // 5. 鎖定並取得 Session
            // ==========================================

            // 透過 Refresh Token 找到對應 Session
            // 同時取得更新鎖，避免與 Refresh 交易交錯

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
            // 5-1. 檢查 Session 是否已經撤銷
            // ==========================================

            if (session.RevokedAt.HasValue)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }


            // ==========================================
            // 5-2. 檢查 Session 是否過期
            // ==========================================

            if (session.IdleExpiresAt <= now ||
                session.AbsoluteExpiresAt <= now)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }


            // ==========================================
            // 5-3. 確認此 Token 是 Session 目前有效的 Token
            // ==========================================

            // 同一個 Session 可能保留多個 TokenVersion
            // 但只能有一筆 revoked_at IS NULL
            //
            // 必須確認目前有效 Token 的 token_id
            // 與 APP 傳入的 Token 所對應 token_id 相同

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


            // 找不到有效 Token，或不是 APP 傳入的那一筆
            if (activeToken == null ||
                activeToken.TokenId != token.TokenId)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return false;
            }


            // ==========================================
            // 6. 撤銷 Session
            // ==========================================

            // 如果尚未撤銷，記錄本次登出時間

            session.RevokedAt = now;

            session.RevokedReason = "UserLogout";


            // ==========================================
            // 7. 撤銷此 Session 所有有效 Refresh Token
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
            // 8. 寫入資料庫
            // ==========================================

            await _db.SaveChangesAsync(
                cancellationToken);


            // ==========================================
            // 9. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(
                cancellationToken);


            // ==========================================
            // 10. 登出成功
            // ==========================================

            return true;
        }
    }
}

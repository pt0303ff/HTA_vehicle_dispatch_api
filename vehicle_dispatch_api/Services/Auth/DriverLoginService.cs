
using System.Data;
using Microsoft.EntityFrameworkCore;

using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.DTOs.Auth;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class DriverLoginService
    {
        private readonly ApplicationDbContext _db;
        private readonly DriverPasswordService _passwordService;
        private readonly JwtTokenService _jwtTokenService;
        private readonly RefreshTokenService _refreshTokenService;


        // ==========================================
        // Session 有效期限設定
        // ==========================================

        // 閒置期限：7 天
        // 未來要修改閒置期限，調整這裡即可。
        // DB：vehicledriverappsession.idleexpires_at
        private static readonly TimeSpan SessionIdleTimeout =
            TimeSpan.FromDays(7);

        // 最長有效期限：30 天
        // 未來要修改 Session 最長期限，調整這裡即可。
        // DB：vehicledriverappsession.absoluteexpiresat
        private static readonly TimeSpan SessionAbsoluteTimeout =
            TimeSpan.FromDays(30);


        // ==========================================
        // 建構子
        // ==========================================

        public DriverLoginService(
            ApplicationDbContext db,
            DriverPasswordService passwordService,
            JwtTokenService jwtTokenService,
            RefreshTokenService refreshTokenService)
        {
            _db = db;
            _passwordService = passwordService;
            _jwtTokenService = jwtTokenService;
            _refreshTokenService = refreshTokenService;
        }


        // ==========================================
        // 司機登入驗證
        // ==========================================

        public async Task<DriverLoginResponse?> LoginAsync(
            DriverLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            // ==========================================
            // 1. 基本資料檢查
            // ==========================================

            ArgumentNullException.ThrowIfNull(request);

            var loginId = request.LoginId?.Trim() ?? "";

            // 密碼不可 Trim，避免改變使用者輸入內容
            var password = request.Password ?? "";

            var phoneId = request.PhoneId;

            if (loginId.Length == 0 ||
                loginId.Length > 100 ||
                password.Length == 0 ||
                password.Length > 128 ||
                !phoneId.HasValue ||
                phoneId.Value == Guid.Empty)
            {
                return null;
            }


            // ==========================================
            // 2. 查詢司機帳號
            // ==========================================

            var driver = await _db.DriverAppAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.LoginId == loginId,
                    cancellationToken);

            // 帳號不存在
            if (driver == null)
                return null;


            // ==========================================
            // 3. 確認帳號狀態
            // ==========================================

            // 只有 Active 帳號允許登入
            // PendingActivation：尚未開通
            // Disabled：管理員已停用

            if (driver.AccountStatus != "Active")
                return null;


            // ==========================================
            // 4. 驗證登入密碼
            // ==========================================

            bool passwordValid =
                _passwordService.VerifyPassword(
                    driver,
                    password);

            if (!passwordValid)
            {
                // 記錄登入失敗次數
                // 使用資料庫原子更新，避免同時登入時覆蓋計數

                await _db.DriverAppAccounts
                    .Where(x =>
                        x.DriverId == driver.DriverId &&
                        x.AccountStatus == "Active" &&
                        x.PasswordHash == driver.PasswordHash)
                    .ExecuteUpdateAsync(setters => setters

                        .SetProperty(
                            x => x.FailedLoginAttempts,
                            x => x.FailedLoginAttempts + 1),

                        cancellationToken);

                return null;
            }


            // ==========================================
            // 5. 開始資料庫交易
            // ==========================================

            var now = DateTime.UtcNow;

            await using var transaction =
                await _db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);


            // ==========================================
            // 6. 再次確認帳號仍然有效
            // ==========================================

            // 防止驗證密碼後，帳號被管理員停用
            // 或密碼已被其他請求修改

            bool accountStillValid =
                await _db.DriverAppAccounts
                    .AsNoTracking()
                    .AnyAsync(
                        x =>
                            x.DriverId == driver.DriverId &&
                            x.AccountStatus == "Active" &&
                            x.PasswordHash == driver.PasswordHash,
                        cancellationToken);

            if (!accountStillValid)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }


            // ==========================================
            // 7. 查詢手機設備
            // ==========================================

            var phone = await _db.DriverAppPhones
                .FirstOrDefaultAsync(
                    x =>
                        x.PhoneId == phoneId.Value &&
                        x.DriverId == driver.DriverId &&
                        x.Status == DriverAppPhone.StatusBound,
                    cancellationToken);

            // 確認手機存在、屬於該司機且仍有效
            if (phone == null ||
                string.IsNullOrWhiteSpace(phone.PublicKey))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }


            // ==========================================
            // 8. 建立 Session
            // ==========================================

            var absoluteExpiresAt =
                now.Add(DriverTokenSettings.SessionAbsoluteTimeout);

            var idleExpiresAt =
                now.Add(DriverTokenSettings.SessionIdleTimeout);

            // 閒置期限不能超過最長有效期限
            if (idleExpiresAt > absoluteExpiresAt)
            {
                idleExpiresAt = absoluteExpiresAt;
            }

            var session = new DriverAppSession
            {
                SessionId = Guid.NewGuid(),

                DriverId = driver.DriverId,

                PhoneId = phone.PhoneId,

                CreatedAt = now,

                LastUsedAt = now,

                IdleExpiresAt = idleExpiresAt,

                AbsoluteExpiresAt = absoluteExpiresAt,

                RevokedAt = null,

                RevokedReason = null,

                Phone = phone
            };
            // ==========================================
            // 9. 產生 JWT Access Token
            // ==========================================

            var accessTokenResult =
                _jwtTokenService.CreateAccessToken(
                    session,
                    now);


            // ==========================================
            // 10. 產生 Refresh Token
            // ==========================================

            var refreshTokenResult =
                _refreshTokenService.CreateToken();

            // Refresh Token 到期時間
            // 不可超過 Session 閒置及最長有效期限

            var refreshExpiresAt =
                now.Add(DriverTokenSettings.RefreshTokenLifetime);

            if (refreshExpiresAt > session.IdleExpiresAt)
            {
                refreshExpiresAt = session.IdleExpiresAt;
            }

            if (refreshExpiresAt > session.AbsoluteExpiresAt)
            {
                refreshExpiresAt = session.AbsoluteExpiresAt;
            }


            // ==========================================
            // 11. 建立 Refresh Token DB 紀錄
            // ==========================================

            var refreshTokenEntity = new DriverAppRefreshToken
            {
                TokenId = Guid.NewGuid(),

                SessionId = session.SessionId,

                TokenHash = refreshTokenResult.Hash,

                ExpiresAt = refreshExpiresAt,

                RevokedAt = null,

                ReplacedByTokenId = null,

                CreatedAt = now,

                TokenVersion = 1,

                Session = session
            };

            // ==========================================
            // 12. 重設登入失敗次數
            // ==========================================

            var affectedRows = await _db.DriverAppAccounts
                .Where(x =>
                    x.DriverId == driver.DriverId &&
                    x.AccountStatus == "Active" &&
                    x.PasswordHash == driver.PasswordHash)
                .ExecuteUpdateAsync(setters => setters

                    .SetProperty(
                        x => x.FailedLoginAttempts,
                        0),

                    cancellationToken);

            if (affectedRows != 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }


            // ==========================================
            // 13. 寫入 Session
            // ==========================================

            _db.DriverAppSessions.Add(session);

            _db.DriverAppRefreshTokens.Add(refreshTokenEntity);

            await _db.SaveChangesAsync(cancellationToken);


            // ==========================================
            // 14. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(cancellationToken);


            // ==========================================
            // 15. 回傳登入結果
            // ==========================================

            // ==========================================
            // 13. 回傳完整登入結果
            // ==========================================

            return new DriverLoginResponse
            {
                DriverId = driver.DriverId,

                SessionId = session.SessionId,

                SessionIdleExpiresAt = session.IdleExpiresAt,

                SessionAbsoluteExpiresAt = session.AbsoluteExpiresAt,

                AccountStatus = driver.AccountStatus,

                // Access Token
                TokenType = "Bearer",

                AccessToken = accessTokenResult.Token,

                AccessTokenExpiresAt = accessTokenResult.ExpiresAt,

                // Refresh Token
                // 注意：只有這次登入回傳明碼
                RefreshToken = refreshTokenResult.Token,

                RefreshTokenExpiresAt = refreshExpiresAt
            };
        }
    }
}

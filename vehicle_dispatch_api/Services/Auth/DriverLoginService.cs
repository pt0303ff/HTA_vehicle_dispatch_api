
using System;
using System.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
        private readonly DpopProofValidationService _dpopValidationService;
        private readonly ILogger<DriverLoginService> _logger;

        public DriverLoginService(
            ApplicationDbContext db,
            DriverPasswordService passwordService,
            JwtTokenService jwtTokenService,
            RefreshTokenService refreshTokenService,
            DpopProofValidationService dpopValidationService,
            ILogger<DriverLoginService> logger)
        {
            _db = db;
            _passwordService = passwordService;
            _jwtTokenService = jwtTokenService;
            _refreshTokenService = refreshTokenService;
            _dpopValidationService = dpopValidationService;
            _logger = logger;
        }

        // ==========================================
        // 司機登入驗證
        // ==========================================

        public async Task<DriverLoginResponse?> LoginAsync(
            DriverLoginRequest request,
            string dpopProof,
            string expectedUri,
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
                phoneId.Value == Guid.Empty ||
                string.IsNullOrWhiteSpace(dpopProof) ||
                string.IsNullOrWhiteSpace(expectedUri))
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

            if (driver == null)
            {
                return null;
            }

            // ==========================================
            // 3. 確認帳號狀態
            // ==========================================

            if (driver.AccountStatus != "Active")
            {
                return null;
            }

            // ==========================================
            // 4. 驗證登入密碼
            // ==========================================

            bool passwordValid =
                _passwordService.VerifyPassword(
                    driver,
                    password);

            if (!passwordValid)
            {
                // 使用資料庫原子更新
                // 避免同時登入時覆蓋失敗次數

                await _db.DriverAppAccounts
                    .Where(x =>
                        x.DriverId == driver.DriverId &&
                        x.AccountStatus == "Active" &&
                        x.PasswordHash == driver.PasswordHash)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
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
            // 7. 查詢已綁定手機設備
            // ==========================================

            var phone = await _db.DriverAppPhones
                .FirstOrDefaultAsync(
                    x =>
                        x.PhoneId == phoneId.Value &&
                        x.DriverId == driver.DriverId &&
                        x.Status == DriverAppPhone.StatusBound,
                    cancellationToken);

            if (phone == null ||
                string.IsNullOrWhiteSpace(phone.PublicKey))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }

            // ==========================================
            // 8. 驗證 DPoP Proof
            // ==========================================

            bool dpopValid =
                _dpopValidationService.ValidateProof(
                    dpopProof,
                    phone.PublicKey,
                    phone.PhoneId,
                    "POST",
                    expectedUri,
                    out string thumbprint,
                    out string dpopError);

            if (!dpopValid)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                // 只記錄驗證原因，不記錄完整 Proof 或 Nonce
                _logger.LogWarning(
                    "司機登入 DPoP 驗證失敗。PhoneId: {PhoneId}, Reason: {Reason}",
                    phone.PhoneId,
                    dpopError);

                return null;
            }

            _logger.LogInformation(
                "司機設備 DPoP 驗證成功。DriverId: {DriverId}, PhoneId: {PhoneId}",
                driver.DriverId,
                phone.PhoneId);

            // ==========================================
            // 9. 建立 Session
            // ==========================================

            var absoluteExpiresAt =
                now.Add(
                    DriverTokenSettings.SessionAbsoluteTimeout);

            var idleExpiresAt =
                now.Add(
                    DriverTokenSettings.SessionIdleTimeout);

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
            // 10. 產生 JWT Access Token
            // ==========================================

            // JWT 內加入 cnf.jkt
            // 綁定目前通過 DPoP 驗證的設備公鑰

            var accessTokenResult =
                _jwtTokenService.CreateAccessToken(
                    session,
                    now,
                    thumbprint);

            // ==========================================
            // 11. 產生 Refresh Token
            // ==========================================

            var refreshTokenResult =
                _refreshTokenService.CreateToken();

            var refreshExpiresAt =
                now.Add(
                    DriverTokenSettings.RefreshTokenLifetime);

            if (refreshExpiresAt > session.IdleExpiresAt)
            {
                refreshExpiresAt = session.IdleExpiresAt;
            }

            if (refreshExpiresAt > session.AbsoluteExpiresAt)
            {
                refreshExpiresAt = session.AbsoluteExpiresAt;
            }

            // ==========================================
            // 12. 建立 Refresh Token DB 紀錄
            // ==========================================

            var refreshTokenEntity =
                new DriverAppRefreshToken
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
            // 13. 重設登入失敗次數
            // ==========================================

            var affectedRows =
                await _db.DriverAppAccounts
                    .Where(x =>
                        x.DriverId == driver.DriverId &&
                        x.AccountStatus == "Active" &&
                        x.PasswordHash == driver.PasswordHash)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
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
            // 14. 寫入 Session 及 Refresh Token
            // ==========================================

            _db.DriverAppSessions.Add(session);

            _db.DriverAppRefreshTokens.Add(
                refreshTokenEntity);

            await _db.SaveChangesAsync(
                cancellationToken);

            // ==========================================
            // 15. Transaction Commit
            // ==========================================

            await transaction.CommitAsync(
                cancellationToken);

            // ==========================================
            // 16. 回傳登入結果
            // ==========================================

            return new DriverLoginResponse
            {
                DriverId = driver.DriverId,

                SessionId = session.SessionId,

                SessionIdleExpiresAt =
                    session.IdleExpiresAt,

                SessionAbsoluteExpiresAt =
                    session.AbsoluteExpiresAt,

                AccountStatus = driver.AccountStatus,

                // Access Token
                TokenType = "Bearer",

                AccessToken = accessTokenResult.Token,

                AccessTokenExpiresAt =
                    accessTokenResult.ExpiresAt,

                // Refresh Token
                // 明碼僅在這次登入回傳
                RefreshToken = refreshTokenResult.Token,

                RefreshTokenExpiresAt = refreshExpiresAt
            };
        }
    }
}

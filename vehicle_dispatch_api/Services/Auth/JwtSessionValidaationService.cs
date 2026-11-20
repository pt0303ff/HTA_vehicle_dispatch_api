
using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.Models.Auth;

namespace vehicle_dispatch_api.Services.Auth
{
    public class JwtSessionValidationService
    {
        private readonly ApplicationDbContext _db;

        public JwtSessionValidationService(
            ApplicationDbContext db)
        {
            _db = db;
        }

        // ==========================================
        // 驗證 JWT 對應的 Session
        // ==========================================

        public async Task<bool> IsSessionValidAsync(
            Guid driverId,
            Guid sessionId,
            Guid phoneId,
            CancellationToken cancellationToken = default)
        {
            // 使用 UTC 時間
            var now = DateTime.UtcNow;

            // 同時查詢 Session、司機及手機設備
            // 三者必須全部符合有效條件

            var isValid = await (
                from session in _db.DriverAppSessions
                    .AsNoTracking()

                join phone in _db.DriverAppPhones
                    .AsNoTracking()

                    on new
                    {
                        session.PhoneId,
                        session.DriverId
                    }
                    equals new
                    {
                        phone.PhoneId,
                        phone.DriverId
                    }

                join driver in _db.DriverAppAccounts
                    .AsNoTracking()

                    on session.DriverId equals driver.DriverId

                where

                    // Session ID 必須一致
                    session.SessionId == sessionId

                    // 司機 ID 必須一致
                    && session.DriverId == driverId

                    // 手機 ID 必須一致
                    && session.PhoneId == phoneId

                    // Session 不可已撤銷
                    && session.RevokedAt == null

                    // Session 閒置期限尚未到期
                    && session.IdleExpiresAt > now

                    // Session 最長期限尚未到期
                    && session.AbsoluteExpiresAt > now

                    // 司機帳號必須正常
                    && driver.AccountStatus == "Active"

                    // 手機必須仍處於綁定狀態
                    && phone.Status == DriverAppPhone.StatusBound

                    // 手機不可已解除綁定
                    && phone.RevokedAt == null

                select session.SessionId

            ).AnyAsync(cancellationToken);

            return isValid;
        }
    }
}

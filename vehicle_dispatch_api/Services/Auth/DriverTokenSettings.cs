
namespace vehicle_dispatch_api.Services.Auth
{
    public static class DriverTokenSettings
    {
        // ==========================================
        // 1. Refresh Token 有效期限
        // ==========================================

        // 目前：7 天
        // 未來要修改 Refresh Token 有效期限，
        // 請修改這個欄位。
        public static readonly TimeSpan RefreshTokenLifetime =
            TimeSpan.FromDays(7);


        // ==========================================
        // 2. Session 閒置期限
        // ==========================================

        // 目前：7 天
        // 未來要修改 Session 閒置期限，
        // 請修改這個欄位。
        // DB：vehicledriverappsession.idleexpires_at
        public static readonly TimeSpan SessionIdleTimeout =
            TimeSpan.FromDays(7);


        // ==========================================
        // 3. Session 最長有效期限
        // ==========================================

        // 目前：30 天
        // 未來要修改 Session 最長有效期限，
        // 請修改這個欄位。
        // DB：vehicledriverappsession.absoluteexpiresat
        public static readonly TimeSpan SessionAbsoluteTimeout =
            TimeSpan.FromDays(30);
    }
}

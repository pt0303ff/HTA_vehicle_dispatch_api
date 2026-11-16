
namespace vehicle_dispatch_api.Models.Auth
{
    public class DriverAppSession
    {
        // ==========================================
        // 1. Session ID
        // ==========================================

        public Guid SessionId { get; set; }
        // DB：vehicledriverappsession.session_id
        // Session 主鍵


        // ==========================================
        // 2. 司機與手機 ID
        // ==========================================

        public Guid DriverId { get; set; }
        // DB：vehicledriverappsession.driver_id
        // 目前登入的司機 ID

        public Guid PhoneId { get; set; }
        // DB：vehicledriverappsession.phone_id
        // 目前登入的手機設備 ID


        // ==========================================
        // 3. Session 時間
        // ==========================================

        public DateTime CreatedAt { get; set; }
        // DB：created_at
        // Session 建立時間，UTC

        public DateTime? LastUsedAt { get; set; }
        // DB：lastused_at
        // 最後使用時間，UTC

        public DateTime IdleExpiresAt { get; set; }
        // DB：idleexpires_at
        // 閒置到期時間，UTC

        public DateTime AbsoluteExpiresAt { get; set; }
        // DB：absoluteexpiresat
        // Session 最長到期時間，UTC


        // ==========================================
        // 4. Session 撤銷
        // ==========================================

        public DateTime? RevokedAt { get; set; }
        // DB：revoked_at
        // Session 撤銷時間

        public string? RevokedReason { get; set; }
        // DB：revokedreason
        // Session 撤銷原因


        // ==========================================
        // 5. 並行控制
        // ==========================================

        public byte[] RowVersion { get; set; }
            = Array.Empty<byte>();

        // DB：row_version
        // SQL Server ROWVERSION


        // ==========================================
        // 6. EF Core Navigation Properties
        // ==========================================

        // 本次新增：
        // 每個 Session 對應一台已登記的手機
        public DriverAppPhone Phone { get; set; } = null!;

        // 一個 Session 可以有多筆歷史 Refresh Token
        public ICollection<DriverAppRefreshToken> RefreshTokens
        {
            get;
            set;
        } = new List<DriverAppRefreshToken>();
    }
}

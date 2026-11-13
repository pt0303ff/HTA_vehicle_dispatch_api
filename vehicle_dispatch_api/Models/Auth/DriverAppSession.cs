
using System.ComponentModel.DataAnnotations.Schema;

namespace vehicle_dispatch_api.Models.Auth
{
    public class DriverAppSession
    {
        public Guid SessionId { get; set; }
        // TODO(DB欄位確認)：Session 主鍵
        // 資料表：vehicledriverappsession
        // 欄位：session_id

        public Guid DriverId { get; set; }
        // TODO(DB欄位確認)：司機 ID
        // 欄位：driver_id

        public Guid PhoneId { get; set; }
        // TODO(DB欄位確認)：手機設備 ID
        // 欄位：phone_id
        // Phase 5-3 對應 vehicledriverphone.phone_id

        public DateTime CreatedAt { get; set; }
        // TODO(DB欄位確認)：Session 建立時間
        // 欄位：created_at

        public DateTime? LastUsedAt { get; set; }
        // TODO(DB欄位確認)：最後使用時間
        // 欄位：lastused_at

        public DateTime IdleExpiresAt { get; set; }
        // TODO(DB欄位確認)：閒置期限
        // 欄位：idleexpires_at

        public DateTime AbsoluteExpiresAt { get; set; }
        // TODO(DB欄位確認)：Session 最長期限
        // 欄位：absoluteexpiresat

        public DateTime? RevokedAt { get; set; }
        // TODO(DB欄位確認)：Session 撤銷時間
        // 欄位：revoked_at

        public string? RevokedReason { get; set; }
        // TODO(DB欄位確認)：Session 撤銷原因
        // 欄位：revokedreason

        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
        // TODO(DB欄位確認)：SQL Server ROWVERSION
        // 欄位：row_version

        // EF Core Navigation Property
        // 對應一個 Session 可以擁有多筆 Refresh Token
        public ICollection<DriverAppRefreshToken> RefreshTokens
        { get; set; } = new List<DriverAppRefreshToken>();
    }
}


namespace vehicle_dispatch_api.Models.Auth
{
    public class DriverAppRefreshToken
    {
        public Guid TokenId { get; set; }
        // TODO(DB欄位確認)：Refresh Token 主鍵
        // 資料表：vehicledriverapprefreshtoken
        // 欄位：token_id

        public Guid SessionId { get; set; }
        // TODO(DB欄位確認)：所屬 Session ID
        // 欄位：session_id

        public string TokenHash { get; set; } = string.Empty;
        // TODO(DB欄位確認)：Refresh Token SHA256 Hash
        // 欄位：tokenhash
        // SQL型別：CHAR(64)

        public DateTime ExpiresAt { get; set; }
        // TODO(DB欄位確認)：Token 到期時間
        // 欄位：expiresat

        public DateTime? RevokedAt { get; set; }
        // TODO(DB欄位確認)：Token 撤銷時間
        // 欄位：revoked_at

        public Guid? ReplacedByTokenId { get; set; }
        // TODO(DB欄位確認)：被哪一筆新 Token 取代
        // 欄位：replacedbytokenid

        public DateTime CreatedAt { get; set; }
        // TODO(DB欄位確認)：Token 建立時間
        // 欄位：created_at

        public int TokenVersion { get; set; }
        // TODO(DB欄位確認)：Token 輪替版本
        // 欄位：tokenversion

        // EF Core Navigation Properties

        public DriverAppSession Session { get; set; } = null!;
        // 所屬 Session

        public DriverAppRefreshToken? ReplacedByToken { get; set; }
        // 被哪一筆新 Token 取代

        public ICollection<DriverAppRefreshToken> ReplacedTokens
        { get; set; } = new List<DriverAppRefreshToken>();
        // 反向關聯：哪些舊 Token 被這筆 Token 取代
    }
}

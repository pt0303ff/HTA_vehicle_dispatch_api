
namespace vehicle_dispatch_api.Models.Auth
{
    public class DriverAppPhone
    {
        // ==========================================
        // 設備狀態定義
        // ==========================================

        public const string StatusBound = "Bound";
        // 已完成綁定，設備有效，可以登入 APP

        public const string StatusDisabled = "Disabled";
        // 設備已停用，不允許登入 APP


        // ==========================================
        // 資料庫欄位
        // ==========================================

        public Guid PhoneId { get; set; }
        // DB：vehicledriverphone.phone_id
        // 手機設備唯一識別碼
        // SQL：UNIQUEIDENTIFIER
        // 由 SQL Server NEWSEQUENTIALID() 自動產生

        public Guid DriverId { get; set; }
        // DB：vehicledriverphone.driver_id
        // 所屬司機 ID
        // FK：vehicledriver.driver_id

        public string Platform { get; set; } = string.Empty;
        // DB：vehicledriverphone.platform
        // 手機作業系統
        // Android / iOS
        // SQL：VARCHAR(20)

        public string Status { get; set; } = "Bound";
        // DB：vehicledriverphone.status
        // 設備狀態
        // Bound / Disabled
        // SQL：NVARCHAR(20)

        public string PublicKey { get; set; } = string.Empty;
        // DB：vehicledriverphone.publickey
        // 手機產生的非對稱金鑰公鑰
        // 私鑰只保存在手機，不會傳送到 API
        // SQL：NVARCHAR(MAX)

        public DateTime CreatedAt { get; set; }
        // DB：vehicledriverphone.createdate
        // 設備首次綁定時間
        // SQL：DATETIME2(3)，UTC

        public DateTime? RevokedAt { get; set; }
        // DB：vehicledriverphone.revokedate
        // 設備解除綁定或停用時間
        // SQL：DATETIME2(3)


        // ==========================================
        // EF Core Navigation Properties
        // ==========================================

        public ICollection<DriverAppSession> Sessions
        {
            get;
            set;
        } = new List<DriverAppSession>();

        // 一台手機可以建立多個歷史登入 Session
        // 對應 vehicledriverappsession.phone_id
    }
}

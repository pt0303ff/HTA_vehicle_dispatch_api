namespace vehicle_dispatch_api.Models.Auth
{
    public class DriverAppAccount
    {
        public Guid DriverId { get; set; }
        // DB：vehicledriver.driver_id
        // 司機唯一識別碼，也是 Primary Key
        // 公司員工兼任司機時，使用 employee.id

        public string Name { get; set; } = string.Empty;
        // DB：vehicledriver.name
        // 司機姓名，NVARCHAR(20)

        public string LoginId { get; set; } = string.Empty;
        // DB：vehicledriver.loginid
        // APP 登入帳號，NVARCHAR(100)

        public string PasswordHash { get; set; } = string.Empty;
        // DB：vehicledriver.passwordhash
        // 密碼雜湊，VARCHAR(500)，不可為 NULL
        // 管理者頁面及 API 統一使用 ASP.NET Core Identity PasswordHasher V3

        public string AccountStatus { get; set; } = "PendingActivation";
        // DB：vehicledriver.accountstatus
        // PendingActivation / Active / Disabled

        public string? ActivationTokenHash { get; set; }
        // DB：vehicledriver.activationtokenhash
        // 16 位 Base36 開通碼經 Trim + ToUpperInvariant + UTF-8 + SHA256
        // 結果為 64 位十六進位字串，儲存於 CHAR(64)

        public DateTime? ActivationExpiresAt { get; set; }
        // DB：vehicledriver.activationexpires_at
        // 開通碼到期時間，UTC

        public int ActivationFailedAttempts { get; set; }
        // DB：vehicledriver.activationfailedattempts
        // 開通驗證碼輸入錯誤次數

        public int FailedLoginAttempts { get; set; }
        // DB：vehicledriver.failedloginattempts
        // 登入失敗次數

        public DateTime? PasswordChangedAt { get; set; }
        // DB：vehicledriver.passwordchanged_at
        // 最後修改密碼時間，UTC

        public DateTime ModifyDate { get; set; }
        // DB：vehicledriver.modifydate
        // 最後修改時間，UTC

        public DateTime InitDate { get; set; }
        // DB：vehicledriver.initdate
        // 建立時間，UTC

        public Guid? ModifyUid { get; set; }
        // DB：vehicledriver.modifyuid
        // 最後修改者 ID
    }
}

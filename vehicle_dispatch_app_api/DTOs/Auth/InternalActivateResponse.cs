
namespace vehicle_dispatch_app_api.DTOs.Auth
{
    // ==========================================
    // 內網 API 帳號開通回傳資料
    // ==========================================

    public class InternalActivateResponse
    {
        // 開通是否成功
        public bool Success { get; set; }

        // 開通結果訊息
        public string? Message { get; set; }

        // 司機 ID
        // 來源：內網 API DriverActivationService
        // 對應：vehicledriver.driver_id
        public Guid DriverId { get; set; }

        // 手機設備 ID
        // 來源：首次開通時由內網 API 自動建立 GUID
        // 對應：vehicledriverphone.phone_id
        public Guid PhoneId { get; set; }

        // 帳號狀態
        // 對應：vehicledriver.accountstatus
        public string? AccountStatus { get; set; }
    }
}

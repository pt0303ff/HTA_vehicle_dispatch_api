
namespace vehicle_dispatch_api.DTOs.Auth
{
    public class ActivateDriverResponse
    {
        public Guid DriverId { get; set; }

        public Guid PhoneId { get; set; }

        public string AccountStatus { get; set; } = "Active";
    }
}


namespace vehicle_dispatch_app_api.DTOs
{
    public class InternalPingResponse
    {
        public bool Success { get; set; }

        public string? Message { get; set; }

        public DateTimeOffset ServerTime { get; set; }
    }
}

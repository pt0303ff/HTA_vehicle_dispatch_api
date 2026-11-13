
using Microsoft.AspNetCore.Mvc;
using vehicle_dispatch_app_api.Clients;

namespace vehicle_dispatch_app_api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private readonly InternalApiClient _internalApiClient;

        public SystemController(
            InternalApiClient internalApiClient)
        {
            _internalApiClient = internalApiClient;
        }

        // GET: api/system/ping
        // 測試外網 API 自身
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new
            {
                success = true,
                message = "Vehicle Dispatch Public API is running",
                serverTime = DateTimeOffset.UtcNow
            });
        }

        // GET: api/system/internal-ping
        // 透過外網 API 呼叫內網 API
        [HttpGet("internal-ping")]
        public async Task<IActionResult> InternalPing(
            CancellationToken cancellationToken)
        {
            try
            {
                var result = await _internalApiClient
                    .GetPingAsync(cancellationToken);

                if (result == null)
                {
                    return StatusCode(502, new
                    {
                        success = false,
                        message = "內網 API 沒有回傳有效資料"
                    });
                }

                return Ok(new
                {
                    success = true,
                    source = "internal_api",
                    data = result
                });
            }
            catch (HttpRequestException)
            {
                return StatusCode(502, new
                {
                    success = false,
                    message = "無法連線至內網 API，請檢查服務網址及連線狀態"
                });
            }
            catch (TaskCanceledException)
            {
                return StatusCode(504, new
                {
                    success = false,
                    message = "呼叫內網 API 逾時"
                });
            }
        }
    }
}

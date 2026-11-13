
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using vehicle_dispatch_api.DTOs.Auth;
using vehicle_dispatch_api.Services.Auth;

namespace vehicle_dispatch_api.Controllers
{
    [ApiController]
    [Route("api/internal/auth")]
    public class InternalAuthController : ControllerBase
    {
        private readonly DriverActivationService _activationService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InternalAuthController> _logger;

        public InternalAuthController(
            DriverActivationService activationService,
            IConfiguration configuration,
            ILogger<InternalAuthController> logger)
        {
            _activationService = activationService;
            _configuration = configuration;
            _logger = logger;
        }

        // POST: api/internal/auth/activate
        [HttpPost("activate")]
        public async Task<IActionResult> Activate(
            [FromBody] ActivateDriverRequest request,
            CancellationToken cancellationToken)
        {
            // 1. 取得內部服務驗證 Key
            var expectedKey =
                _configuration["InternalApi:ApiKey"];

            if (string.IsNullOrWhiteSpace(expectedKey))
            {
                _logger.LogError(
                    "尚未設定 InternalApi:ApiKey");

                return StatusCode(500, new
                {
                    success = false,
                    message = "內部服務尚未完成設定"
                });
            }

            // 2. 檢查 HTTP Header
            if (!Request.Headers.TryGetValue(
                    "X-Internal-Api-Key",
                    out var suppliedKey))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            // 3. 使用固定時間比較 Key
            var expectedBytes =
                Encoding.UTF8.GetBytes(expectedKey);

            var suppliedBytes =
                Encoding.UTF8.GetBytes(suppliedKey.ToString());

            bool authorized =
                expectedBytes.Length == suppliedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(
                    expectedBytes,
                    suppliedBytes);

            if (!authorized)
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "未授權的服務請求"
                });
            }

            // 4. 執行帳號開通
            try
            {
                bool success = await _activationService.ActivateAsync(
                    request,
                    cancellationToken);

                if (!success)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "開通資訊無效或已過期，請確認後重試或聯絡管理員"
                    });
                }

                return Ok(new
                {
                    success = true,
                    message = "司機帳號開通成功"
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "司機帳號開通時發生錯誤");

                return StatusCode(500, new
                {
                    success = false,
                    message = "帳號開通服務發生錯誤"
                });
            }
        }
    }
}

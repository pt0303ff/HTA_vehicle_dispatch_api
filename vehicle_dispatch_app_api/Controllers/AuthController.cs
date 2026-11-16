
using System.Net;
using Microsoft.AspNetCore.Mvc;
using vehicle_dispatch_app_api.Clients;
using vehicle_dispatch_app_api.DTOs.Auth;

namespace vehicle_dispatch_app_api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly InternalAuthClient _internalAuthClient;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            InternalAuthClient internalAuthClient,
            ILogger<AuthController> logger)
        {
            _internalAuthClient = internalAuthClient;
            _logger = logger;
        }

        // POST: api/auth/activate
        [HttpPost("activate")]
        public async Task<IActionResult> Activate(
            [FromBody] ActivateDriverRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                // 轉送至內網 API
                using var response =
                    await _internalAuthClient.ActivateAsync(
                        request,
                        cancellationToken);

                // 1. 開通成功
                if (response.IsSuccessStatusCode)
                {
                    // 讀取內網 API 回傳的完整 JSON
                    // 包含 driverId、phoneId、accountStatus
                    var responseBody =
                        await response.Content.ReadAsStringAsync(
                            cancellationToken);

                    // 保留內網 API 回傳的 Content-Type
                    var contentType =
                        response.Content.Headers.ContentType?.ToString()
                        ?? "application/json";

                    // 將內網 API 的成功結果完整回傳給 APP
                    return new ContentResult
                    {
                        StatusCode = (int)response.StatusCode,
                        Content = responseBody,
                        ContentType = contentType
                    };
                }

                // 2. 開通資料錯誤
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = "開通資訊無效或已過期，請確認後重試或聯絡管理員"
                    });
                }

                // 3. 內外網服務驗證失敗
                if (response.StatusCode == HttpStatusCode.Unauthorized ||
                    response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogError(
                        "外網 API 呼叫內網 API 時服務驗證失敗");

                    return StatusCode(502, new
                    {
                        success = false,
                        message = "內部服務驗證失敗"
                    });
                }

                // 4. 內網 API 發生其他錯誤
                _logger.LogError(
                    "內網 API 回傳錯誤狀態：{StatusCode}",
                    (int)response.StatusCode);

                return StatusCode(503, new
                {
                    success = false,
                    message = "目前無法使用帳號開通服務，請稍後再試"
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "外網 API 無法連接內網 API");

                return StatusCode(503, new
                {
                    success = false,
                    message = "內部服務目前無法連線"
                });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(
                    ex,
                    "外網 API 內部服務設定錯誤");

                return StatusCode(503, new
                {
                    success = false,
                    message = "內部服務設定錯誤"
                });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("呼叫內網 API 逾時");

                return StatusCode(504, new
                {
                    success = false,
                    message = "帳號開通服務逾時，請稍後再試"
                });
            }
        }
    }
}

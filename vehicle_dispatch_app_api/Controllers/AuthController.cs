
using System.Net;
using System.Linq;
using System.Text.Json;

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

        // ==========================================
        // 1. POST: api/auth/activate
        // 司機首次開通帳號
        // ==========================================

        [HttpPost("activate")]
        public Task<IActionResult> Activate(
            [FromBody] ActivateDriverRequest request,
            CancellationToken cancellationToken)
        {
            return ForwardAsync(
                () => _internalAuthClient.ActivateAsync(
                    request,
                    cancellationToken),
                "帳號開通",
                cancellationToken);
        }

        // ==========================================
        // 2. POST: api/auth/nonce
        // 取得 DPoP Nonce
        // ==========================================

        [HttpPost("nonce")]
        public Task<IActionResult> GetNonce(
            [FromBody] DpopNonceRequest request,
            CancellationToken cancellationToken)
        {
            return ForwardAsync(
                () => _internalAuthClient.GetNonceAsync(
                    request,
                    cancellationToken),
                "取得 DPoP Nonce",
                cancellationToken,
                forwardDpopNonce: true);
        }

        // ==========================================
        // 3. POST: api/auth/login
        // DPoP + JWT 司機登入
        // ==========================================

        [HttpPost("login")]
        public Task<IActionResult> Login(
            [FromHeader(Name = "DPoP")]
            string? dpopProof,

            [FromBody] DriverLoginRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                return Task.FromResult<IActionResult>(
                    BadRequest(new
                    {
                        success = false,
                        message = "缺少 DPoP Proof"
                    }));
            }

            return ForwardAsync(
                () => _internalAuthClient.LoginAsync(
                    request,
                    dpopProof,
                    cancellationToken),
                "司機登入",
                cancellationToken);
        }

        // ==========================================
        // 4. POST: api/auth/refresh
        // DPoP + Refresh Token Rotation
        // ==========================================

        [HttpPost("refresh")]
        public Task<IActionResult> Refresh(
            [FromHeader(Name = "DPoP")]
            string? dpopProof,

            [FromBody] DriverRefreshRequest request,
            CancellationToken cancellationToken)
        {
            // DPoP Proof 必須存在
            if (string.IsNullOrWhiteSpace(dpopProof))
            {
                return Task.FromResult<IActionResult>(
                    BadRequest(new
                    {
                        success = false,
                        message = "缺少 DPoP Proof"
                    }));
            }

            return ForwardAsync(
                () => _internalAuthClient.RefreshAsync(
                    request,
                    dpopProof,
                    cancellationToken),
                "Refresh Token 更新",
                cancellationToken);
        }

        // ==========================================
        // 5. POST: api/auth/logout
        // 司機登出
        // ==========================================

        [HttpPost("logout")]
        public Task<IActionResult> Logout(
            [FromBody] DriverLogoutRequest request,
            CancellationToken cancellationToken)
        {
            return ForwardAsync(
                () => _internalAuthClient.LogoutAsync(
                    request,
                    cancellationToken),
                "司機登出",
                cancellationToken);
        }

        // ==========================================
        // 共用：轉送內網 API 回應
        // ==========================================

        private async Task<IActionResult> ForwardAsync(
            Func<Task<HttpResponseMessage>> sendRequest,
            string operation,
            CancellationToken cancellationToken,
            bool forwardDpopNonce = false)
        {
            try
            {
                using var response = await sendRequest();

                // 讀取內網 API JSON
                var responseBody =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken);

                // ==========================================
                // 轉送 DPoP-Nonce Response Header
                // ==========================================

                if (forwardDpopNonce &&
                    response.Headers.TryGetValues(
                        "DPoP-Nonce",
                        out var nonceValues))
                {
                    Response.Headers["DPoP-Nonce"] =
                        nonceValues.FirstOrDefault();
                }

                // ==========================================
                // 1. 內網服務驗證失敗
                // ==========================================

                if (response.StatusCode ==
                        HttpStatusCode.Unauthorized &&
                    IsInternalServiceUnauthorized(responseBody))
                {
                    _logger.LogError(
                        "外網 API 呼叫內網 API 時服務驗證失敗：{Operation}",
                        operation);

                    return StatusCode(502, new
                    {
                        success = false,
                        message = "內部服務驗證失敗"
                    });
                }

                // ==========================================
                // 2. 內網 API 路徑或權限設定錯誤
                // ==========================================

                if (response.StatusCode == HttpStatusCode.Forbidden ||
                    response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogError(
                        "內網 API 端點或服務權限異常：{Operation}，StatusCode={StatusCode}",
                        operation,
                        (int)response.StatusCode);

                    return StatusCode(502, new
                    {
                        success = false,
                        message = "內部服務端點或權限設定異常"
                    });
                }

                // ==========================================
                // 3. 內網 API 逾時
                // ==========================================

                if (response.StatusCode == HttpStatusCode.GatewayTimeout ||
                    response.StatusCode == HttpStatusCode.RequestTimeout)
                {
                    return StatusCode(504, new
                    {
                        success = false,
                        message = $"{operation}服務逾時"
                    });
                }

                // ==========================================
                // 4. 內網 API 發生伺服器錯誤
                // ==========================================

                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogError(
                        "內網 API 發生錯誤：{Operation}，StatusCode={StatusCode}",
                        operation,
                        (int)response.StatusCode);

                    return StatusCode(503, new
                    {
                        success = false,
                        message = $"{operation}服務目前無法使用"
                    });
                }

                // ==========================================
                // 5. 將內網 API 結果轉交 APP
                // ==========================================

                if (string.IsNullOrWhiteSpace(responseBody))
                {
                    return StatusCode((int)response.StatusCode);
                }

                return new ContentResult
                {
                    StatusCode = (int)response.StatusCode,
                    Content = responseBody,
                    ContentType =
                        response.Content.Headers.ContentType?.ToString()
                        ?? "application/json; charset=utf-8"
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "外網 API 無法連接內網 API：{Operation}",
                    operation);

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
                    "外網 API 內部服務設定錯誤：{Operation}",
                    operation);

                return StatusCode(503, new
                {
                    success = false,
                    message = "內部服務設定錯誤"
                });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(
                    "呼叫內網 API 逾時：{Operation}",
                    operation);

                return StatusCode(504, new
                {
                    success = false,
                    message = $"{operation}服務逾時"
                });
            }
        }

        // ==========================================
        // 判斷是否為內網 API Key 驗證失敗
        // ==========================================

        private static bool IsInternalServiceUnauthorized(
            string responseBody)
        {
            try
            {
                using var document =
                    JsonDocument.Parse(responseBody);

                if (document.RootElement.TryGetProperty(
                    "message",
                    out var message))
                {
                    return message.GetString() ==
                        "未授權的服務請求";
                }
            }
            catch (JsonException)
            {
                // 非預期 JSON 格式
            }

            return false;
        }
    }
}

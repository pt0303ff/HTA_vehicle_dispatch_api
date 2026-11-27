
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using vehicle_dispatch_app_api.Clients;

namespace vehicle_dispatch_app_api.Controllers;

[ApiController]
[Authorize]
[Route("api/dispatch")]
public class DispatchSyncController : ControllerBase
{
    private readonly InternalDispatchClient _internalDispatchClient;
    private readonly ILogger<DispatchSyncController> _logger;

    public DispatchSyncController(
        InternalDispatchClient internalDispatchClient,
        ILogger<DispatchSyncController> logger)
    {
        _internalDispatchClient = internalDispatchClient;
        _logger = logger;
    }

    // ==================================================
    // 共用：取得 APP 傳入的 JWT + DPoP
    // ==================================================

    private bool TryGetForwardingCredentials(
        out string authorizationHeader,
        out string dpopProof)
    {
        authorizationHeader = string.Empty;
        dpopProof = string.Empty;

        if (!Request.Headers.TryGetValue(
                "Authorization",
                out var authorizationValues))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue(
                "DPoP",
                out var dpopValues))
        {
            return false;
        }

        authorizationHeader =
            authorizationValues.ToString();

        dpopProof = dpopValues.ToString();

        return AuthenticationHeaderValue.TryParse(
                   authorizationHeader,
                   out var authorization)
               &&
               string.Equals(
                   authorization.Scheme,
                   "Bearer",
                   StringComparison.OrdinalIgnoreCase)
               &&
               !string.IsNullOrWhiteSpace(
                   authorization.Parameter)
               &&
               !string.IsNullOrWhiteSpace(dpopProof);
    }

    // ==================================================
    // 共用：取得 APP 原始公開 URI
    // ==================================================

    private string GetOriginalPublicUri()
    {
        return
            $"{Request.Scheme}://{Request.Host}" +
            $"{Request.PathBase}{Request.Path}";
    }

    // ==================================================
    // 共用：轉送 DPoP Nonce
    // ==================================================

    private void ForwardSecurityHeaders(
        HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(
                "DPoP-Nonce",
                out var nonceValues))
        {
            Response.Headers["DPoP-Nonce"] =
                nonceValues.FirstOrDefault();
        }

        if (response.Headers.TryGetValues(
                "WWW-Authenticate",
                out var authValues))
        {
            Response.Headers["WWW-Authenticate"] =
                authValues.FirstOrDefault();
        }

        if (response.Headers.TryGetValues(
                "Retry-After",
                out var retryValues))
        {
            Response.Headers["Retry-After"] =
                retryValues.FirstOrDefault();
        }
    }

    // ==================================================
    // POST: api/dispatch/sync
    // 接收 APP 離線任務同步資料
    // ==================================================

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        // ==========================================
        // 1. 確認 JSON 格式
        // ==========================================

        if (payload.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(new
            {
                success = false,
                message = "同步資料格式錯誤"
            });
        }

        // ==========================================
        // 2. 取得 JWT + DPoP
        // ==========================================

        if (!TryGetForwardingCredentials(
                out var authorizationHeader,
                out var dpopProof))
        {
            return Unauthorized(new
            {
                success = false,
                message = "缺少或無效的 JWT / DPoP 認證資訊"
            });
        }

        // ==========================================
        // 3. 取得 APP 原始公開 URI
        // ==========================================

        var originalUri = GetOriginalPublicUri();

        // ==========================================
        // 4. 呼叫內網 API
        // ==========================================

        try
        {
            using var response =
                await _internalDispatchClient.SyncExecutionAsync(
                    payload,
                    authorizationHeader,
                    dpopProof,
                    originalUri,
                    cancellationToken);

            // ======================================
            // 5. 轉送 DPoP 驗證相關 Header
            // ======================================

            ForwardSecurityHeaders(response);

            var responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            // ======================================
            // 6. 內網 API 路徑不存在
            // ======================================

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogError(
                    "內網同步 API 端點不存在");

                return StatusCode(502, new
                {
                    success = false,
                    message = "內部同步服務端點尚未建立"
                });
            }

            // ======================================
            // 7. 內網 API 請求逾時
            // ======================================

            if (response.StatusCode == HttpStatusCode.RequestTimeout ||
                response.StatusCode == HttpStatusCode.GatewayTimeout)
            {
                return StatusCode(504, new
                {
                    success = false,
                    message = "內部同步服務逾時"
                });
            }

            // ======================================
            // 8. 內網 API 發生伺服器錯誤
            // ======================================

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "內網同步 API 發生錯誤，StatusCode={StatusCode}",
                    (int)response.StatusCode);

                return StatusCode(503, new
                {
                    success = false,
                    message = "內部同步服務目前無法使用"
                });
            }

            // ======================================
            // 9. 原樣轉交內網 API 結果
            // 包含 400、401、403 等狀態碼
            // ======================================

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
                "外網 API 無法連接內網同步服務");

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
                "外網 API 同步服務設定錯誤");

            return StatusCode(503, new
            {
                success = false,
                message = "內部同步服務設定錯誤"
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "呼叫內網同步 API 逾時");

            return StatusCode(504, new
            {
                success = false,
                message = "同步服務逾時，請稍後重試"
            });
        }
    }
}

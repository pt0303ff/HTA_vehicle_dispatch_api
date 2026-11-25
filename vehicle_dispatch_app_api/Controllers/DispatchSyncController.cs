
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

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
        // 2. 取得 APP 傳入的 JWT
        // ==========================================

        if (!Request.Headers.TryGetValue(
                "Authorization",
                out var authorizationValues))
        {
            return Unauthorized(new
            {
                success = false,
                message = "缺少 Authorization Token"
            });
        }

        var authorizationHeader =
            authorizationValues.ToString();

        if (!AuthenticationHeaderValue.TryParse(
                authorizationHeader,
                out var authorization) ||
            !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(authorization.Parameter))
        {
            return Unauthorized(new
            {
                success = false,
                message = "Authorization Token 格式錯誤"
            });
        }

        // ==========================================
        // 3. 呼叫內網 API
        // ==========================================

        try
        {
            using var response =
                await _internalDispatchClient.SyncExecutionAsync(
                    payload,
                    authorization.ToString(),
                    cancellationToken);

            var responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            // ======================================
            // 4. 內網 API 路徑不存在
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
            // 5. 內網 API 請求逾時
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
            // 6. 內網 API 發生伺服器錯誤
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
            // 7. 原樣轉交內網 API 結果
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

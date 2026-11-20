using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using vehicle_dispatch_app_api.Clients;

namespace vehicle_dispatch_app_api.Controllers;

[ApiController]
[Route("api/dispatch")]
[Authorize]
public class DispatchController : ControllerBase
{
    private readonly InternalDispatchClient _internalDispatchClient;
    private readonly ILogger<DispatchController> _logger;

    public DispatchController(
        InternalDispatchClient internalDispatchClient,
        ILogger<DispatchController> logger)
    {
        _internalDispatchClient = internalDispatchClient;
        _logger = logger;
    }


    // ==================================================
    // GET: api/dispatch/tasks
    // ==================================================

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        CancellationToken cancellationToken)
    {
        // ==================================================
        // 從 JWT 取得 Driver ID
        // ==================================================

        var rawDriverId =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier)
            ??
            User.FindFirstValue("driverId")
            ??
            User.FindFirstValue("driver_id")
            ??
            User.FindFirstValue("sub");


        if (!Guid.TryParse(
                rawDriverId,
                out var driverId))
        {
            _logger.LogWarning(
                "JWT 中找不到有效的 Driver ID");

            return Unauthorized(new
            {
                success = false,
                message = "無法取得司機身分"
            });
        }


        // ==================================================
        // 呼叫內網 API
        // ==================================================

        try
        {
            using var response =
                await _internalDispatchClient.GetTasksAsync(
                    driverId,
                    cancellationToken);


            var responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);


            // ==================================================
            // 內網 API Key 驗證失敗
            // ==================================================

            if (
                response.StatusCode ==
                    HttpStatusCode.Unauthorized
                &&
                responseBody.Contains(
                    "未授權的服務請求",
                    StringComparison.Ordinal))
            {
                _logger.LogError(
                    "外網 API 呼叫內網派車 API 時驗證失敗");

                return StatusCode(502, new
                {
                    success = false,
                    message = "內部服務驗證失敗"
                });
            }


            // ==================================================
            // 其他內網錯誤
            // ==================================================

            if (
                response.StatusCode ==
                    HttpStatusCode.NotFound
                ||
                response.StatusCode ==
                    HttpStatusCode.Forbidden)
            {
                _logger.LogError(
                    "內網派車 API 端點異常，StatusCode={StatusCode}",
                    (int)response.StatusCode);

                return StatusCode(502, new
                {
                    success = false,
                    message = "內部服務端點異常"
                });
            }


            // ==================================================
            // 直接把內網 API 結果轉回 APP
            // ==================================================

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,

                Content = responseBody,

                ContentType =
                    "application/json; charset=utf-8"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "外網 API 無法連接內網派車服務");

            return StatusCode(502, new
            {
                success = false,
                message = "目前無法連線至內部派車服務"
            });
        }
    }

    // ==================================================
    // GET: api/dispatch/tasks/{dispatchNo}
    // 取得單筆任務詳細資訊
    // ==================================================

    [HttpGet("tasks/{dispatchNo}")]
    public async Task<IActionResult> GetTaskDetail(
        string dispatchNo,
        CancellationToken cancellationToken)
    {
        // JWT 的 sub 就是 DriverId
        // 因為目前 JWT 設定 MapInboundClaims = false

        var rawDriverId = User.FindFirstValue("sub");

        if (!Guid.TryParse(rawDriverId, out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "無法取得司機身分"
            });
        }

        try
        {
            using var response =
                await _internalDispatchClient.GetTaskDetailAsync(
                    dispatchNo,
                    driverId,
                    cancellationToken);

            var responseBody =
                await response.Content.ReadAsStringAsync(
                    cancellationToken);

            // 內網服務驗證失敗不能當成 APP JWT 過期
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogError(
                    "內網派車服務驗證失敗");

                return StatusCode(502, new
                {
                    success = false,
                    message = "內部服務驗證失敗"
                });
            }

            return new ContentResult
            {
                StatusCode = (int)response.StatusCode,
                Content = responseBody,
                ContentType = "application/json; charset=utf-8"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "無法連接內網派車服務");

            return StatusCode(502, new
            {
                success = false,
                message = "目前無法連線至內部派車服務"
            });
        }
    }
}
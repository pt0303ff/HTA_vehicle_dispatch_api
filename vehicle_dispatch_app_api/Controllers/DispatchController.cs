
using System.Net;
using System.Net.Http.Headers;
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
    // 共用：取得 JWT + DPoP
    // ==================================================

    private bool TryGetForwardingCredentials(
        out string authorizationHeader,
        out string dpopProof)
    {
        authorizationHeader = string.Empty;
        dpopProof = string.Empty;

        if (!Request.Headers.TryGetValue(
                "Authorization",
                out var authValues))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue(
                "DPoP",
                out var dpopValues))
        {
            return false;
        }

        authorizationHeader = authValues.ToString();
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
    // 共用：取得 Driver ID
    // ==================================================

    private bool TryGetDriverId(out Guid driverId)
    {
        driverId = Guid.Empty;

        var rawDriverId =
            User.FindFirstValue("sub")
            ??
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ??
            User.FindFirstValue("driverId")
            ??
            User.FindFirstValue("driver_id");

        return Guid.TryParse(rawDriverId, out driverId)
               && driverId != Guid.Empty;
    }

    // ==================================================
    // 共用：取得 APP 原始公開 URI
    // DPoP htu 不包含 Query String / Fragment
    // ==================================================

    private string GetOriginalPublicUri()
    {
        return
            $"{Request.Scheme}://{Request.Host}" +
            $"{Request.PathBase}{Request.Path}";
    }

    // ==================================================
    // 共用：轉送內網 DPoP 驗證相關 Response Headers
    // 特別是 DPoP-Nonce，APP 必須取得才能重新產生 Proof
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
    // 共用：原樣轉回內網 API Response
    // ==================================================

    private async Task<IActionResult> ForwardResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ForwardSecurityHeaders(response);

        var responseBody =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        return new ContentResult
        {
            StatusCode = (int)response.StatusCode,

            Content = responseBody,

            ContentType =
                response.Content.Headers.ContentType?.ToString()
                ?? "application/json; charset=utf-8"
        };
    }

    // ==================================================
    // GET: api/dispatch/tasks
    // 取得目前司機所有派車任務
    // ==================================================

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        CancellationToken cancellationToken)
    {
        // 1. 取得 JWT + DPoP

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

        // 2. 取得司機身分

        if (!TryGetDriverId(out var driverId))
        {
            _logger.LogWarning(
                "JWT 中找不到有效的 Driver ID");

            return Unauthorized(new
            {
                success = false,
                message = "無法取得司機身分"
            });
        }

        // 3. 取得 APP 原始公開 URI

        var originalUri = GetOriginalPublicUri();

        // 4. 呼叫內網 API

        try
        {
            using var response =
                await _internalDispatchClient.GetTasksAsync(
                    driverId,
                    authorizationHeader,
                    dpopProof,
                    originalUri,
                    cancellationToken);

            if (response.StatusCode ==
                HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "內網派車 API 驗證失敗，DriverId={DriverId}",
                    driverId);
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "內網派車 API 發生錯誤，StatusCode={StatusCode}",
                    (int)response.StatusCode);
            }

            // 直接轉送結果及 DPoP-Nonce
            return await ForwardResponseAsync(
                response,
                cancellationToken);
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
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "呼叫內網派車 API 逾時");

            return StatusCode(504, new
            {
                success = false,
                message = "內部派車服務逾時"
            });
        }
    }

    // ==================================================
    // GET: api/dispatch/tasks/{dispatchNo}
    // 取得單筆派車任務詳細資訊
    // ==================================================

    [HttpGet("tasks/{dispatchNo}")]
    public async Task<IActionResult> GetTaskDetail(
        string dispatchNo,
        CancellationToken cancellationToken)
    {
        // 1. 取得 JWT + DPoP

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

        // 2. 取得司機身分

        if (!TryGetDriverId(out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "無法取得司機身分"
            });
        }

        // 3. 取得 APP 原始公開 URI

        var originalUri = GetOriginalPublicUri();

        // 4. 呼叫內網 API

        try
        {
            using var response =
                await _internalDispatchClient.GetTaskDetailAsync(
                    dispatchNo,
                    driverId,
                    authorizationHeader,
                    dpopProof,
                    originalUri,
                    cancellationToken);

            if (response.StatusCode ==
                HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "內網派車詳細資訊 API 驗證失敗，DriverId={DriverId}",
                    driverId);
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogError(
                    "內網派車詳細資訊 API 發生錯誤，StatusCode={StatusCode}",
                    (int)response.StatusCode);
            }

            return await ForwardResponseAsync(
                response,
                cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(
                ex,
                "無法連接內網派車詳細資訊服務");

            return StatusCode(502, new
            {
                success = false,
                message = "目前無法連線至內部派車服務"
            });
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "呼叫內網派車詳細資訊 API 逾時");

            return StatusCode(504, new
            {
                success = false,
                message = "內部派車服務逾時"
            });
        }
    }
}

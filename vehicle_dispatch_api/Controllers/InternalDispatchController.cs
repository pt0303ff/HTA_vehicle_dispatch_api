
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using vehicle_dispatch_api.Services.Dispatch;

namespace vehicle_dispatch_api.Controllers;

[ApiController]
[Authorize]
[Route("api/internal/dispatch")]
public class InternalDispatchController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly DriverDispatchService _dispatchService;
    private readonly ILogger<InternalDispatchController> _logger;

    public InternalDispatchController(
        IConfiguration configuration,
        DriverDispatchService dispatchService,
        ILogger<InternalDispatchController> logger)
    {
        _configuration = configuration;
        _dispatchService = dispatchService;
        _logger = logger;
    }

    // ==================================================
    // 共用：驗證外網 API 的 Internal API Key
    // ==================================================

    private IActionResult? ValidateInternalApiKey(
        string? suppliedKey)
    {
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

        if (string.IsNullOrWhiteSpace(suppliedKey))
        {
            return Unauthorized(new
            {
                success = false,
                message = "未授權的服務請求"
            });
        }

        var expectedBytes =
            Encoding.UTF8.GetBytes(expectedKey);

        var suppliedBytes =
            Encoding.UTF8.GetBytes(suppliedKey);

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

        return null;
    }

    // ==================================================
    // 共用：從已驗證 JWT 取得 DriverId
    // ==================================================

    private bool TryGetAuthenticatedDriverId(
        out Guid driverId)
    {
        driverId = Guid.Empty;

        var driverIdValue =
            User.FindFirst("sub")?.Value;

        return Guid.TryParse(
                   driverIdValue,
                   out driverId)
               &&
               driverId != Guid.Empty;
    }

    // ==================================================
    // 共用：驗證內網 DPoP Middleware 已經執行
    // 此標記只能由 Middleware 在驗證成功後設定
    // ==================================================

    private bool IsDpopValidated()
    {
        return HttpContext.Items.TryGetValue(
                   "DpopValidated",
                   out var validated)
               &&
               validated is true;
    }

    // ==================================================
    // GET: api/internal/dispatch/tasks
    // 取得目前司機所有派車任務
    // ==================================================

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        [FromHeader(Name = "X-Internal-Api-Key")]
        string? suppliedKey,
        CancellationToken cancellationToken)
    {
        // 1. 驗證內部 API Key

        var keyError =
            ValidateInternalApiKey(suppliedKey);

        if (keyError != null)
            return keyError;

        // 2. 確認 DPoP 已通過 Middleware

        if (!IsDpopValidated())
        {
            _logger.LogWarning(
                "派車任務查詢未通過 DPoP Middleware");

            return Unauthorized(new
            {
                success = false,
                message = "DPoP 驗證尚未完成"
            });
        }

        // 3. 從 JWT 取得司機 ID

        if (!TryGetAuthenticatedDriverId(
                out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "JWT 司機身分資訊無效"
            });
        }

        // 4. 查詢派車任務

        try
        {
            var tasks =
                await _dispatchService.GetTasksAsync(
                    driverId,
                    cancellationToken);

            return Ok(new
            {
                success = true,
                message = "取得派車任務成功",
                tasks
            });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "取得司機派車任務時發生錯誤，DriverId={DriverId}",
                driverId);

            return StatusCode(500, new
            {
                success = false,
                message = "取得派車任務失敗"
            });
        }
    }

    // ==================================================
    // GET: api/internal/dispatch/tasks/{dispatchNo}
    // 取得單筆派車任務詳細資訊
    // ==================================================

    [HttpGet("tasks/{dispatchNo}")]
    public async Task<IActionResult> GetTaskDetail(
        string dispatchNo,

        [FromHeader(Name = "X-Internal-Api-Key")]
        string? suppliedKey,

        CancellationToken cancellationToken)
    {
        // 1. 驗證內部 API Key

        var keyError =
            ValidateInternalApiKey(suppliedKey);

        if (keyError != null)
            return keyError;

        // 2. 確認 DPoP 已通過 Middleware

        if (!IsDpopValidated())
        {
            _logger.LogWarning(
                "派車詳細資訊查詢未通過 DPoP Middleware");

            return Unauthorized(new
            {
                success = false,
                message = "DPoP 驗證尚未完成"
            });
        }

        // 3. 從 JWT 取得司機 ID

        if (!TryGetAuthenticatedDriverId(
                out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "JWT 司機身分資訊無效"
            });
        }

        // 4. 查詢單筆任務

        try
        {
            var result =
                await _dispatchService.GetTaskDetailAsync(
                    driverId,
                    dispatchNo,
                    cancellationToken);

            if (result == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = "找不到派車任務，或此任務不屬於目前司機"
                });
            }

            return Ok(new
            {
                success = true,
                message = "取得派車任務詳細資訊成功",
                task = result
            });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "取得派車詳細資訊失敗，DriverId={DriverId}, DispatchNo={DispatchNo}",
                driverId,
                dispatchNo);

            return StatusCode(500, new
            {
                success = false,
                message = "取得派車詳細資訊失敗"
            });
        }
    }
}

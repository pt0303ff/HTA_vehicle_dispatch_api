
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

using vehicle_dispatch_api.DTOs.Dispatch;
using vehicle_dispatch_api.Services.Dispatch;

namespace vehicle_dispatch_api.Controllers;

[ApiController]
[Authorize]
[Route("api/internal/dispatch")]
public sealed class InternalDispatchSyncController : ControllerBase
{
    private readonly DriverDispatchSyncService _syncService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalDispatchSyncController> _logger;

    public InternalDispatchSyncController(
        DriverDispatchSyncService syncService,
        IConfiguration configuration,
        ILogger<InternalDispatchSyncController> logger)
    {
        _syncService = syncService;
        _configuration = configuration;
        _logger = logger;
    }

    // ==================================================
    // 共用：驗證內部 API Key
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

        var authorized =
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
    // 共用：確認 DPoP Middleware 驗證成功
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
    // 共用：從 JWT 取得 DriverId
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
    // POST: api/internal/dispatch/sync
    // 接收外網 API 傳來的離線任務資料
    // ==================================================

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(
        [FromBody] DispatchSyncRequest request,
        CancellationToken cancellationToken)
    {
        // ==========================================
        // 1. 驗證內部 API Key
        // ==========================================

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

        var keyError =
            ValidateInternalApiKey(suppliedKey.ToString());

        if (keyError != null)
        {
            return keyError;
        }

        // ==========================================
        // 2. 確認 DPoP Middleware 已驗證成功
        // ==========================================

        if (!IsDpopValidated())
        {
            _logger.LogWarning(
                "離線任務同步未通過 DPoP 驗證");

            return Unauthorized(new
            {
                success = false,
                message = "DPoP 驗證尚未完成"
            });
        }

        // ==========================================
        // 3. 從 JWT 取得司機 ID
        // ==========================================

        if (!TryGetAuthenticatedDriverId(
                out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "JWT 司機身分資訊無效"
            });
        }

        // ==========================================
        // 4. 確認同步資料
        // ==========================================

        if (request == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "同步資料不可為空"
            });
        }

        // ==========================================
        // 5. 執行同步
        // ==========================================

        try
        {
            var result = await _syncService.SyncAsync(
                driverId,
                request,
                cancellationToken);

            return Ok(new
            {
                success = true,

                message = "任務同步成功",

                executionId = result.ExecutionId,

                insertedCheckIns = result.InsertedCheckIns,

                insertedEvents = result.InsertedEvents,

                receivedCheckInIds = result.ReceivedCheckInIds,

                receivedQueueIds = result.ReceivedQueueIds
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                success = false,
                message = ex.Message
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
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "派車同步資料發生衝突，DriverId={DriverId}",
                driverId);

            return Conflict(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SqlException ex)
            when (ex.Number == 2601 || ex.Number == 2627)
        {
            _logger.LogWarning(
                ex,
                "同步資料違反 SQL Server 唯一限制");

            return Conflict(new
            {
                success = false,
                message = "同步資料已存在或發生重複請求"
            });
        }
        catch (SqlException ex)
            when (ex.Number == 1205)
        {
            _logger.LogWarning(
                ex,
                "同步交易發生 Deadlock");

            return StatusCode(503, new
            {
                success = false,
                message = "同步服務暫時忙碌，請稍後重試"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "司機離線任務同步失敗，DriverId={DriverId}",
                driverId);

            return StatusCode(500, new
            {
                success = false,
                message = "任務同步服務發生錯誤"
            });
        }
    }
}

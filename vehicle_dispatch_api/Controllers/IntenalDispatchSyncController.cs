
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using vehicle_dispatch_api.DTOs.Dispatch;
using vehicle_dispatch_api.Services.Dispatch;
using Microsoft.Data.SqlClient;

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

        var expectedBytes =
            Encoding.UTF8.GetBytes(expectedKey);

        var suppliedBytes =
            Encoding.UTF8.GetBytes(suppliedKey.ToString());

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

        // ==========================================
        // 2. 從 JWT 取得司機 ID
        // ==========================================

        var driverIdValue =
            User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(driverIdValue, out var driverId) ||
            driverId == Guid.Empty)
        {
            return Unauthorized(new
            {
                success = false,
                message = "JWT 司機身分資訊無效"
            });
        }

        // ==========================================
        // 3. 執行同步
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

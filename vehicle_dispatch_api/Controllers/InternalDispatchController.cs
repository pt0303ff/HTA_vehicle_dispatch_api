using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Mvc;

using vehicle_dispatch_api.Services.Dispatch;

namespace vehicle_dispatch_api.Controllers;

[ApiController]
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
    // GET: api/internal/dispatch/tasks
    // ==================================================

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(
        [FromHeader(Name = "X-Internal-Api-Key")]
        string? suppliedKey,

        [FromHeader(Name = "X-Driver-Id")]
        string? driverIdHeader,

        CancellationToken cancellationToken)
    {
        // ==================================================
        // 1. 驗證內部 API Key
        // ==================================================

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


        // ==================================================
        // 2. 驗證 Driver ID
        // ==================================================

        if (!Guid.TryParse(
                driverIdHeader,
                out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "無效的司機身分"
            });
        }


        // ==================================================
        // 3. 查詢派車任務
        // ==================================================

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

        [FromHeader(Name = "X-Driver-Id")]
    string? driverIdHeader,

        CancellationToken cancellationToken)
    {
        // 1. 驗證內部 API Key

        var expectedKey =
            _configuration["InternalApi:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
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

        // 2. 驗證司機 ID

        if (!Guid.TryParse(driverIdHeader, out var driverId))
        {
            return Unauthorized(new
            {
                success = false,
                message = "無效的司機身分"
            });
        }

        // 3. 查詢任務

        try
        {
            var result = await _dispatchService.GetTaskDetailAsync(
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
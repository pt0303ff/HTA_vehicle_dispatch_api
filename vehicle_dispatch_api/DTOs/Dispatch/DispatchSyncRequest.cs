
using System.Text.Json;

namespace vehicle_dispatch_api.DTOs.Dispatch;

// ==================================================
// 整趟任務同步請求
// ==================================================

public sealed class DispatchSyncRequest
{
    public DispatchExecutionSyncDto? Execution { get; set; }

    public List<DispatchCheckInSyncDto> CheckIns { get; set; } = new();

    public List<DispatchSyncEventDto> Events { get; set; } = new();
}

// ==================================================
// 任務執行資料
// ==================================================

public sealed class DispatchExecutionSyncDto
{
    public Guid ExecutionId { get; set; }

    // 對應 vehicledispatch.dispatchno
    public string TaskId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    // SQLite 有此欄位，但 SQL Server 主表沒有
    // 保留在 TASK_STARTED Payload
    public DateTime? StartedAt { get; set; }

    public double? StartMileage { get; set; }

    public JsonElement? StartCargo { get; set; }

    public double? StartLatitude { get; set; }

    public double? StartLongitude { get; set; }

    public JsonElement? LoadedPalletNos { get; set; }

    public int CurrentDestinationIndex { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

// ==================================================
// 目的地抵達紀錄
// ==================================================

public sealed class DispatchCheckInSyncDto
{
    public Guid CheckInId { get; set; }

    public Guid ExecutionId { get; set; }

    public Guid DestinationId { get; set; }

    public string DestinationName { get; set; } = string.Empty;

    public int DestinationSequence { get; set; }

    public DateTime? ArrivedAt { get; set; }

    public double Mileage { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public JsonElement? UnloadedPalletNos { get; set; }

    public DateTime? CreatedAt { get; set; }
}

// ==================================================
// 同步事件
// ==================================================

public sealed class DispatchSyncEventDto
{
    public Guid QueueId { get; set; }

    public Guid ExecutionId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public JsonElement? PayloadJson { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int AttemptCount { get; set; }

    public string? LastError { get; set; }
}

// ==================================================
// 同步結果
// ==================================================

public sealed class DispatchSyncResult
{
    public Guid ExecutionId { get; set; }

    public int InsertedCheckIns { get; set; }

    public int InsertedEvents { get; set; }

    public List<Guid> ReceivedCheckInIds { get; set; } = new();

    public List<Guid> ReceivedQueueIds { get; set; } = new();
}

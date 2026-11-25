namespace vehicle_dispatch_api.DTOs.Dispatch;

public sealed class DriverTaskResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<DriverTaskDto> Tasks { get; set; } = new();
}

/// <summary>
/// APP 任務列表資料。
/// Status 回傳派車單原始狀態，手機可依 Flutter SQLite 的執行進度顯示。
/// </summary>
public sealed class DriverTaskDto
{
    public string DispatchId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string? VehicleId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public DateTime? ScheduledEndDate { get; set; }
    public string? Purpose { get; set; }
    public bool HasCargo { get; set; }
    public DriverTaskOriginDto Origin { get; set; } = new();
    public string? FromPlace { get; set; }
    public string? ToPlace { get; set; }
    public string? ToAddress { get; set; }
    public decimal? ToLatitude { get; set; }
    public decimal? ToLongitude { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public List<DriverTaskStopDto> Destinations { get; set; } = new();
    public List<DriverTaskCargoDto> Pallets { get; set; } = new();
    public string? FromAddress { get; set; }
    public decimal? FromLatitude { get; set; }
    public decimal? FromLongitude { get; set; }
}

/// <summary>出發地資料，欄位名稱對應 Flutter TaskOrigin。</summary>
public sealed class DriverTaskOriginDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

// ==================================================
// SQL 查詢用 DTO
// ==================================================

public sealed class DriverTaskDbRow
{
    public Guid DispatchId { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public Guid? VehicleId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public DateTime? ScheduledEndDate { get; set; }
    public string? Purpose { get; set; }
    public bool HasCargo { get; set; }
    public string? FromPlace { get; set; }
    public string? FromAddress { get; set; }
    public decimal? FromLatitude { get; set; }
    public decimal? FromLongitude { get; set; }
    public string? ToPlace { get; set; }
    public string? ToAddress { get; set; }
    public decimal? ToLatitude { get; set; }
    public decimal? ToLongitude { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Remark { get; set; }
}

public sealed class DriverTaskStopDbRow
{
    public Guid StopId { get; set; }
    public Guid DispatchId { get; set; }
    public int Seq { get; set; }
    public string? StopType { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
    public DateTime? Eta { get; set; }
    public string? Remark { get; set; }
    public decimal? StartKm { get; set; }
    public decimal? EndKm { get; set; }
    public decimal? DistanceKm { get; set; }
    public decimal? FactorGkm { get; set; }
    public decimal? Co2Kg { get; set; }
    public DateTime? ArriveDate { get; set; }
    public string? ReportRemark { get; set; }
    public DateTime? ReportDate { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? Latitude { get; set; }
}

/// <summary>只用來取得 END 在資料庫中的原始 seq，不會作為中途停靠點回傳。</summary>
public sealed class DriverTaskEndSeqDbRow
{
    public Guid DispatchId { get; set; }
    public int Seq { get; set; }
}

public sealed class DriverTaskCargoDbRow
{
    public Guid DispatchId { get; set; }
    public string? PalletId { get; set; }
    public string? PalletNo { get; set; }
    public string? ItemNo { get; set; }
    public string? ItemName { get; set; }
    public string? Spec { get; set; }
    public decimal? Qty { get; set; }
    public int? StopSeq { get; set; }
    public int? SortOrder { get; set; }
}

// ==================================================
// APP 目的地
// ==================================================

public sealed class DriverTaskStopDto
{
    // JSON 預設 camelCase：stopId / seq，與 Flutter TaskDestination 對齊。
    public string StopId { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string? StopType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public DateTime? Eta { get; set; }
    public string? Remark { get; set; }
    public decimal? StartKm { get; set; }
    public decimal? EndKm { get; set; }
    public decimal? DistanceKm { get; set; }
    public decimal? FactorGkm { get; set; }
    public decimal? Co2Kg { get; set; }
    public DateTime? ArriveDate { get; set; }
    public decimal? Longitude { get; set; }
    public decimal? Latitude { get; set; }
}

// ==================================================
// APP 棧板
// ==================================================

public sealed class DriverTaskCargoDto
{
    public string? PalletId { get; set; }
    public string? PalletNo { get; set; }
    public string? ItemNo { get; set; }
    public string? ItemName { get; set; }
    public string? Spec { get; set; }
    public decimal? Qty { get; set; }
    public int? StopSeq { get; set; }
    public int? SortOrder { get; set; }
}

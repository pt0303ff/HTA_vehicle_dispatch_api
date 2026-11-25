namespace vehicle_dispatch_api.DTOs.Dispatch;

// ==================================================
// 單筆派車任務詳細資訊
// ==================================================

public sealed class DriverTaskDetailDto
{
    public Guid DispatchId { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public string? DriverName { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public DateTime? ScheduledEndDate { get; set; }
    public string? Purpose { get; set; }
    public bool HasCargo { get; set; }
    public string? CargoRemark { get; set; }

    // 保留既有欄位，避免舊版呼叫端受影響。
    public string? FromPlace { get; set; }
    public string? ToPlace { get; set; }

    // 起點與終點資料均來自 dbo.vehicledispatch。
    public DriverTaskOriginDto Origin { get; set; } = new();
    public string? FromAddress { get; set; }
    public decimal? FromLatitude { get; set; }
    public decimal? FromLongitude { get; set; }
    public string? ToAddress { get; set; }
    public decimal? ToLatitude { get; set; }
    public decimal? ToLongitude { get; set; }

    // Status 回傳派車單原始狀態，手機可依 Flutter SQLite 的執行進度顯示。
    public string Status { get; set; } = string.Empty;
    public string? Remark { get; set; }
    public List<DriverTaskStopDetailDto> Stops { get; set; } = new();
    public DriverTaskStopDetailDto? FinalDestination { get; set; }
    public List<DriverTaskPalletDetailDto> Pallets { get; set; } = new();
}

// ==================================================
// SQL 查詢用：派車單
// ==================================================

public sealed class DriverTaskDetailDbRow
{
    public Guid DispatchId { get; set; }
    public string TaskId { get; set; } = string.Empty;
    public Guid? VehicleId { get; set; }
    public string? VehiclePlate { get; set; }
    public Guid DriverId { get; set; }
    public string? DriverName { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public DateTime? ScheduledEndDate { get; set; }
    public string? Purpose { get; set; }
    public bool HasCargo { get; set; }
    public string? CargoRemark { get; set; }
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

// ==================================================
// SQL 查詢用：貨物棧板
// ==================================================

public sealed class DriverTaskPalletDbRow
{
    public Guid Id { get; set; }
    public Guid DispatchId { get; set; }
    public string? CargoType { get; set; }
    public string? ProjectCode { get; set; }
    public string? MoCode { get; set; }
    public string? ItemNo { get; set; }
    public string? ItemName { get; set; }
    public string? Spec { get; set; }
    public decimal? Qty { get; set; }
    public Guid? PalletId { get; set; }
    public string? PalletNo { get; set; }
    public int? StopSeq { get; set; }
    public int? SortOrder { get; set; }
}

// ==================================================
// API 回傳：中途停靠點 / 最終終點
// ==================================================

public sealed class DriverTaskStopDetailDto
{
    public string StopId { get; set; } = string.Empty;
    public int Seq { get; set; }
    public string? StopType { get; set; }
    public string? DestName { get; set; }
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

// ==================================================
// API 回傳：棧板
// ==================================================

public sealed class DriverTaskPalletDetailDto
{
    public Guid Id { get; set; }
    public string? CargoType { get; set; }
    public string? ProjectCode { get; set; }
    public string? MoCode { get; set; }
    public string? ItemNo { get; set; }
    public string? ItemName { get; set; }
    public string? Spec { get; set; }
    public decimal? Qty { get; set; }
    public Guid? PalletId { get; set; }
    public string? PalletNo { get; set; }
    public int? StopSeq { get; set; }
    public int? SortOrder { get; set; }
}

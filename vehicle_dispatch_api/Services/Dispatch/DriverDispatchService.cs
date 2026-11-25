using Microsoft.EntityFrameworkCore;
using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.DTOs.Dispatch;

namespace vehicle_dispatch_api.Services.Dispatch;

public class DriverDispatchService
{
    private readonly ApplicationDbContext _db;

    public DriverDispatchService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ==================================================
    // 取得司機任務列表
    // 支援新舊派車單狀態，CANCEL 不回傳。
    // IN_PROGRESS 是手機端 SQLite 的執行狀態，不寫回 vehicledispatch.status。
    // ==================================================
    public async Task<List<DriverTaskDto>> GetTasksAsync(
        Guid driverId,
        CancellationToken cancellationToken = default)
    {
        if (driverId == Guid.Empty)
        {
            return new List<DriverTaskDto>();
        }

        // 1. 派車單基本資料：司機指派依據固定使用 driver_id。
        var taskRows = await _db.Database
            .SqlQuery<DriverTaskDbRow>($"""
                SELECT
                    d.id AS DispatchId,
                    d.dispatchno AS TaskId,
                    d.vehicleid AS VehicleId,
                    v.plateno AS VehiclePlate,
                    dr.name AS DriverName,
                    d.startdate AS ScheduledDate,
                    d.enddate AS ScheduledEndDate,
                    d.purpose AS Purpose,
                    CONVERT(bit, ISNULL(d.hascargo, 0)) AS HasCargo,
                    d.fromplace AS FromPlace,
                    d.fromaddress AS FromAddress,
                    TRY_CONVERT(decimal(18,6), d.fromlatitude) AS FromLatitude,
                    TRY_CONVERT(decimal(18,6), d.fromlongitude) AS FromLongitude,
                    d.toplace AS ToPlace,
                    d.toaddress AS ToAddress,
                    TRY_CONVERT(decimal(18,6), d.tolatitude) AS ToLatitude,
                    TRY_CONVERT(decimal(18,6), d.tolongitude) AS ToLongitude,
                    d.status AS Status,
                    d.remark AS Remark
                FROM dbo.vehicledispatch d
                LEFT JOIN dbo.vehicle v
                    ON v.id = d.vehicleid
                LEFT JOIN dbo.vehicledriver dr
                    ON dr.driver_id = d.driver_id
                WHERE
                    d.driver_id = {driverId}
                    AND d.departdate IS NOT NULL 
                    AND d.status IN ('APPLY', 'DONE')
                ORDER BY
                    d.startdate ASC,
                    d.dispatchno ASC
                """)
            .ToListAsync(cancellationToken);

        if (taskRows.Count == 0)
        {
            return new List<DriverTaskDto>();
        }

        // 2. 只取得 STOP 作為中途停靠點；END 不會被加入 Destinations。
        var stopRows = await _db.Database
            .SqlQuery<DriverTaskStopDbRow>($"""
                SELECT
                    s.id AS StopId,
                    s.dispatchid AS DispatchId,
                    s.seq AS Seq,
                    s.stoptype AS StopType,
                    s.destname AS Name,
                    s.address AS Address,
                    s.eta AS Eta,
                    s.remark AS Remark,
                    TRY_CONVERT(decimal(18,3), s.startkm) AS StartKm,
                    TRY_CONVERT(decimal(18,3), s.endkm) AS EndKm,
                    TRY_CONVERT(decimal(18,3), s.distancekm) AS DistanceKm,
                    TRY_CONVERT(decimal(18,3), s.factorgkm) AS FactorGkm,
                    TRY_CONVERT(decimal(18,3), s.co2kg) AS Co2Kg,
                    s.arrivedate AS ArriveDate,
                    s.reportremark AS ReportRemark,
                    s.reportdate AS ReportDate,
                    TRY_CONVERT(decimal(18,8), s.longitude) AS Longitude,
                    TRY_CONVERT(decimal(18,8), s.latitude) AS Latitude
                FROM dbo.vehicledispatchstop s
                INNER JOIN dbo.vehicledispatch d
                    ON d.id = s.dispatchid
                WHERE
                    d.driver_id = {driverId}
                    AND d.status IN ('APPLY', 'DONE', 'PENDING', 'IN_PROGRESS', 'COMPLETED')
                    AND UPPER(LTRIM(RTRIM(ISNULL(s.stoptype, '')))) = 'STOP'
                ORDER BY s.dispatchid, s.seq
                """)
            .ToListAsync(cancellationToken);

        // 3. END 的 seq 只用來對應最終卸貨站的棧板 stopseq；不回傳成中途站。
        var endSeqRows = await _db.Database
            .SqlQuery<DriverTaskEndSeqDbRow>($"""
                SELECT
                    s.dispatchid AS DispatchId,
                    MAX(s.seq) AS Seq
                FROM dbo.vehicledispatchstop s
                INNER JOIN dbo.vehicledispatch d
                    ON d.id = s.dispatchid
                WHERE
                    d.driver_id = {driverId}
                    AND d.status IN ('APPLY', 'DONE', 'PENDING', 'IN_PROGRESS', 'COMPLETED')
                    AND UPPER(LTRIM(RTRIM(ISNULL(s.stoptype, '')))) = 'END'
                GROUP BY s.dispatchid
                """)
            .ToListAsync(cancellationToken);

        // 4. 棧板資料：stopseq 與 sortorder 都必須由 DB 原值帶回。
        var cargoRows = await _db.Database
            .SqlQuery<DriverTaskCargoDbRow>($"""
                SELECT
                    c.dispatchid AS DispatchId,
                    CONVERT(nvarchar(36), c.palletid) AS PalletId,
                    c.palletno AS PalletNo,
                    c.itemno AS ItemNo,
                    c.itemname AS ItemName,
                    c.spec AS Spec,
                    TRY_CONVERT(decimal(18,3), c.qty) AS Qty,
                    c.stopseq AS StopSeq,
                    c.sortorder AS SortOrder
                FROM dbo.vehicledispatchcargo c
                INNER JOIN dbo.vehicledispatch d
                    ON d.id = c.dispatchid
                WHERE
                    d.driver_id = {driverId}
                    AND d.status IN ('APPLY', 'DONE', 'PENDING', 'IN_PROGRESS', 'COMPLETED')
                ORDER BY
                    c.dispatchid,
                    CASE WHEN c.stopseq IS NULL THEN 1 ELSE 0 END,
                    c.stopseq,
                    CASE WHEN c.sortorder IS NULL THEN 1 ELSE 0 END,
                    c.sortorder,
                    c.palletno
                """)
            .ToListAsync(cancellationToken);

        // 5. 組合 APP 任務。
        var result = new List<DriverTaskDto>();

        foreach (var row in taskRows)
        {
            var task = new DriverTaskDto
            {
                DispatchId = row.DispatchId.ToString(),
                TaskId = row.TaskId,
                VehicleId = row.VehicleId?.ToString(),
                VehiclePlate = row.VehiclePlate,
                DriverName = row.DriverName,
                ScheduledDate = row.ScheduledDate,
                ScheduledEndDate = row.ScheduledEndDate,
                Purpose = row.Purpose,
                HasCargo = row.HasCargo,
                FromPlace = row.FromPlace,
                FromAddress = row.FromAddress,
                FromLatitude = row.FromLatitude,
                FromLongitude = row.FromLongitude,
                ToPlace = row.ToPlace,
                ToAddress = row.ToAddress,
                ToLatitude = row.ToLatitude,
                ToLongitude = row.ToLongitude,
                Status = row.Status,
                Remark = row.Remark,
                Origin = new DriverTaskOriginDto
                {
                    Name = row.FromPlace ?? string.Empty,
                    Address = row.FromAddress ?? string.Empty,
                    Latitude = row.FromLatitude,
                    Longitude = row.FromLongitude
                }
            };

            var taskStops = stopRows
                .Where(x => x.DispatchId == row.DispatchId)
                .OrderBy(x => x.Seq)
                .ToList();

            var destinations = taskStops.Select(x => new DriverTaskStopDto
            {
                StopId = x.StopId.ToString(),
                // 保留資料庫 seq，因為 cargo.stopseq 就是依此欄位對應。
                Seq = x.Seq,
                StopType = "STOP",
                Name = x.Name ?? string.Empty,
                Address = x.Address ?? string.Empty,
                Eta = x.Eta,
                Remark = x.Remark,
                StartKm = x.StartKm,
                EndKm = x.EndKm,
                DistanceKm = x.DistanceKm,
                FactorGkm = x.FactorGkm,
                Co2Kg = x.Co2Kg,
                ArriveDate = x.ArriveDate,
                Longitude = x.Longitude,
                Latitude = x.Latitude
            }).ToList();

            // 最終終點 destination_id 固定使用 vehicledispatch.id。
            // Seq 優先使用 END 原始 seq；沒有 END 時才使用最後 STOP seq + 1。
            var endSeq = endSeqRows
                .Where(x => x.DispatchId == row.DispatchId)
                .Select(x => (int?)x.Seq)
                .FirstOrDefault();
            var finalSeq = endSeq
                ?? (taskStops.Count > 0 ? taskStops.Max(x => x.Seq) + 1 : 1);

            destinations.Add(new DriverTaskStopDto
            {
                StopId = row.DispatchId.ToString(),
                Seq = finalSeq,
                StopType = "FINAL",
                Name = row.ToPlace ?? string.Empty,
                Address = row.ToAddress ?? string.Empty,
                Longitude = row.ToLongitude,
                Latitude = row.ToLatitude
            });

            task.Destinations = destinations;

            task.Pallets = cargoRows
                .Where(x => x.DispatchId == row.DispatchId)
                .Select(x => new DriverTaskCargoDto
                {
                    PalletId = x.PalletId,
                    PalletNo = x.PalletNo,
                    ItemNo = x.ItemNo,
                    ItemName = x.ItemName,
                    Spec = x.Spec,
                    Qty = x.Qty,
                    StopSeq = x.StopSeq,
                    SortOrder = x.SortOrder
                })
                .ToList();

            result.Add(task);
        }

        return result;
    }

    // ==================================================
    // 取得單筆派車任務詳細資訊
    // ==================================================
    public async Task<DriverTaskDetailDto?> GetTaskDetailAsync(
        Guid driverId,
        string dispatchNo,
        CancellationToken cancellationToken = default)
    {
        if (driverId == Guid.Empty || string.IsNullOrWhiteSpace(dispatchNo))
        {
            return null;
        }

        // 1. 派車單基本資料；CANCEL 不提供司機查看。
        var taskRows = await _db.Database
            .SqlQuery<DriverTaskDetailDbRow>($"""
                SELECT TOP (1)
                    d.id AS DispatchId,
                    d.dispatchno AS TaskId,
                    d.vehicleid AS VehicleId,
                    v.plateno AS VehiclePlate,
                    d.driver_id AS DriverId,
                    dr.name AS DriverName,
                    d.startdate AS ScheduledDate,
                    d.enddate AS ScheduledEndDate,
                    d.purpose AS Purpose,
                    CONVERT(bit, ISNULL(d.hascargo, 0)) AS HasCargo,
                    d.cargoremark AS CargoRemark,
                    d.fromplace AS FromPlace,
                    d.fromaddress AS FromAddress,
                    TRY_CONVERT(decimal(18,6), d.fromlatitude) AS FromLatitude,
                    TRY_CONVERT(decimal(18,6), d.fromlongitude) AS FromLongitude,
                    d.toplace AS ToPlace,
                    d.toaddress AS ToAddress,
                    TRY_CONVERT(decimal(18,6), d.tolatitude) AS ToLatitude,
                    TRY_CONVERT(decimal(18,6), d.tolongitude) AS ToLongitude,
                    d.status AS Status,
                    d.remark AS Remark
                FROM dbo.vehicledispatch d
                LEFT JOIN dbo.vehicle v
                    ON v.id = d.vehicleid
                LEFT JOIN dbo.vehicledriver dr
                    ON dr.driver_id = d.driver_id
                WHERE
                    d.dispatchno = {dispatchNo}
                    AND d.driver_id = {driverId}
                    AND d.status IN ('APPLY', 'DONE', 'PENDING', 'IN_PROGRESS', 'COMPLETED')
                """)
            .ToListAsync(cancellationToken);

        if (taskRows.Count == 0)
        {
            return null;
        }

        var taskRow = taskRows[0];

        // 2. 只查 STOP 作為中途站；END 僅另查 seq，不會重複顯示。
        var stopRows = await _db.Database
            .SqlQuery<DriverTaskStopDbRow>($"""
                SELECT
                    s.id AS StopId,
                    s.dispatchid AS DispatchId,
                    s.seq AS Seq,
                    s.stoptype AS StopType,
                    s.destname AS Name,
                    s.address AS Address,
                    s.eta AS Eta,
                    s.remark AS Remark,
                    TRY_CONVERT(decimal(18,3), s.startkm) AS StartKm,
                    TRY_CONVERT(decimal(18,3), s.endkm) AS EndKm,
                    TRY_CONVERT(decimal(18,3), s.distancekm) AS DistanceKm,
                    TRY_CONVERT(decimal(18,3), s.factorgkm) AS FactorGkm,
                    TRY_CONVERT(decimal(18,3), s.co2kg) AS Co2Kg,
                    s.arrivedate AS ArriveDate,
                    s.reportremark AS ReportRemark,
                    s.reportdate AS ReportDate,
                    TRY_CONVERT(decimal(18,8), s.longitude) AS Longitude,
                    TRY_CONVERT(decimal(18,8), s.latitude) AS Latitude
                FROM dbo.vehicledispatchstop s
                WHERE
                    s.dispatchid = {taskRow.DispatchId}
                    AND UPPER(LTRIM(RTRIM(ISNULL(s.stoptype, '')))) = 'STOP'
                ORDER BY s.seq ASC
                """)
            .ToListAsync(cancellationToken);

        var endSeqRows = await _db.Database
            .SqlQuery<DriverTaskEndSeqDbRow>($"""
                SELECT
                    s.dispatchid AS DispatchId,
                    MAX(s.seq) AS Seq
                FROM dbo.vehicledispatchstop s
                WHERE
                    s.dispatchid = {taskRow.DispatchId}
                    AND UPPER(LTRIM(RTRIM(ISNULL(s.stoptype, '')))) = 'END'
                GROUP BY s.dispatchid
                """)
            .ToListAsync(cancellationToken);

        var endSeq = endSeqRows.FirstOrDefault()?.Seq
            ?? (stopRows.Count > 0 ? stopRows.Max(x => x.Seq) + 1 : 1);

        // 3. 棧板：保留 stopseq 與 sortorder 原始值。
        var palletRows = await _db.Database
            .SqlQuery<DriverTaskPalletDbRow>($"""
                SELECT
                    c.id AS Id,
                    c.dispatchid AS DispatchId,
                    c.cargotype AS CargoType,
                    c.projectcode AS ProjectCode,
                    c.mocode AS MoCode,
                    c.itemno AS ItemNo,
                    c.itemname AS ItemName,
                    c.spec AS Spec,
                    TRY_CONVERT(decimal(18,3), c.qty) AS Qty,
                    c.palletid AS PalletId,
                    c.palletno AS PalletNo,
                    c.stopseq AS StopSeq,
                    c.sortorder AS SortOrder
                FROM dbo.vehicledispatchcargo c
                WHERE c.dispatchid = {taskRow.DispatchId}
                ORDER BY
                    CASE WHEN c.stopseq IS NULL THEN 1 ELSE 0 END,
                    c.stopseq,
                    CASE WHEN c.sortorder IS NULL THEN 1 ELSE 0 END,
                    c.sortorder,
                    c.id
                """)
            .ToListAsync(cancellationToken);

        // 4. 組合詳細資訊。
        return new DriverTaskDetailDto
        {
            DispatchId = taskRow.DispatchId,
            TaskId = taskRow.TaskId,
            VehiclePlate = taskRow.VehiclePlate,
            DriverName = taskRow.DriverName,
            ScheduledDate = taskRow.ScheduledDate,
            ScheduledEndDate = taskRow.ScheduledEndDate,
            Purpose = taskRow.Purpose,
            HasCargo = taskRow.HasCargo,
            CargoRemark = taskRow.CargoRemark,
            FromPlace = taskRow.FromPlace,
            ToPlace = taskRow.ToPlace,
            Origin = new DriverTaskOriginDto
            {
                Name = taskRow.FromPlace ?? string.Empty,
                Address = taskRow.FromAddress ?? string.Empty,
                Latitude = taskRow.FromLatitude,
                Longitude = taskRow.FromLongitude
            },
            FromAddress = taskRow.FromAddress,
            FromLatitude = taskRow.FromLatitude,
            FromLongitude = taskRow.FromLongitude,
            ToAddress = taskRow.ToAddress,
            ToLatitude = taskRow.ToLatitude,
            ToLongitude = taskRow.ToLongitude,
            Status = taskRow.Status,
            Remark = taskRow.Remark,
            Stops = stopRows.Select(x => new DriverTaskStopDetailDto
            {
                StopId = x.StopId.ToString(),
                Seq = x.Seq,
                StopType = "STOP",
                DestName = x.Name,
                Address = x.Address,
                Eta = x.Eta,
                Remark = x.Remark,
                StartKm = x.StartKm,
                EndKm = x.EndKm,
                DistanceKm = x.DistanceKm,
                FactorGkm = x.FactorGkm,
                Co2Kg = x.Co2Kg,
                ArriveDate = x.ArriveDate,
                ReportRemark = x.ReportRemark,
                ReportDate = x.ReportDate,
                Longitude = x.Longitude,
                Latitude = x.Latitude
            }).ToList(),
            FinalDestination = new DriverTaskStopDetailDto
            {
                // 最終終點 destination_id 使用 vehicledispatch.id。
                StopId = taskRow.DispatchId.ToString(),
                Seq = endSeq,
                StopType = "FINAL",
                DestName = taskRow.ToPlace,
                Address = taskRow.ToAddress,
                Longitude = taskRow.ToLongitude,
                Latitude = taskRow.ToLatitude
            },
            Pallets = palletRows.Select(x => new DriverTaskPalletDetailDto
            {
                Id = x.Id,
                CargoType = x.CargoType,
                ProjectCode = x.ProjectCode,
                MoCode = x.MoCode,
                ItemNo = x.ItemNo,
                ItemName = x.ItemName,
                Spec = x.Spec,
                Qty = x.Qty,
                PalletId = x.PalletId,
                PalletNo = x.PalletNo,
                StopSeq = x.StopSeq,
                SortOrder = x.SortOrder
            }).ToList()
        };
    }
}

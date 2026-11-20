using Microsoft.EntityFrameworkCore;

using vehicle_dispatch_api.DTOs.Dispatch;

using vehicle_dispatch_api.Data;

namespace vehicle_dispatch_api.Services.Dispatch;

public class DriverDispatchService
{
    private readonly ApplicationDbContext _db;

    public DriverDispatchService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ==================================================
    // 取得司機目前可以看到的派車任務
    // ==================================================

    public async Task<List<DriverTaskDto>> GetTasksAsync(
        Guid driverId,
        CancellationToken cancellationToken = default)
    {
        if (driverId == Guid.Empty)
        {
            return new List<DriverTaskDto>();
        }

        // ==================================================
        // 1. 取得派車單
        //
        // APPLY：
        //   申請中，司機不能看到
        //
        // PENDING：
        //   待執行
        //
        // IN_PROGRESS：
        //   執行中
        //
        // COMPLETED：
        //   已完成
        // ==================================================

        var taskRows = await _db.Database
            .SqlQuery<DriverTaskDbRow>($"""
                SELECT
                    d.id AS DispatchId,
                    d.dispatchno AS TaskId,

                    d.vehicleid AS VehicleId,
                    v.plateno AS VehiclePlate,

                    d.startdate AS ScheduledDate,
                    d.enddate AS ScheduledEndDate,

                    d.purpose AS Purpose,

                    CONVERT(
                        bit,
                        ISNULL(d.hascargo, 0)
                    ) AS HasCargo,

                    d.fromplace AS FromPlace,
                    d.toplace AS ToPlace,

                    d.status AS Status,
                    d.remark AS Remark

                FROM dbo.vehicledispatch d

                LEFT JOIN dbo.vehicle v
                    ON v.id = d.vehicleid

                WHERE
                    d.driveruid = {driverId}

                    AND d.status IN
                    (
                        'PENDING',
                        'IN_PROGRESS',
                        'COMPLETED'
                    )

                ORDER BY
                    d.startdate ASC,
                    d.dispatchno ASC
                """)
            .ToListAsync(cancellationToken);


        if (taskRows.Count == 0)
        {
            return new List<DriverTaskDto>();
        }


        // ==================================================
        // 2. 取得所有目的地 / 停靠點
        // ==================================================

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

                    CONVERT(decimal(18,3), s.startkm)
                        AS StartKm,

                    CONVERT(decimal(18,3), s.endkm)
                        AS EndKm,

                    CONVERT(decimal(18,3), s.distancekm)
                        AS DistanceKm,

                    CONVERT(decimal(18,3), s.factorgkm)
                        AS FactorGkm,

                    CONVERT(decimal(18,3), s.co2kg)
                        AS Co2Kg,

                    s.arrivedate AS ArriveDate,

                    -- 回報資訊
                    s.reportremark AS ReportRemark,

                    s.reportdate AS ReportDate,

                    --GPS座標
                    CONVERT(decimal(18,8), s.longitude)
                        AS Longitude,

                    CONVERT(decimal(18,8), s.latitude)
                        AS Latitude

                FROM dbo.vehicledispatchstop s

                INNER JOIN dbo.vehicledispatch d
                    ON d.id = s.dispatchid

                WHERE
                    d.driveruid = {driverId}

                    AND d.status IN
                    (
                        'PENDING',
                        'IN_PROGRESS',
                        'COMPLETED'
                    )

                ORDER BY
                    s.dispatchid,
                    s.seq
                """)
            .ToListAsync(cancellationToken);


        // ==================================================
        // 3. 取得所有棧板
        // ==================================================

        var cargoRows = await _db.Database
            .SqlQuery<DriverTaskCargoDbRow>($"""
                SELECT
                    c.dispatchid AS DispatchId,

                    CONVERT(
                        nvarchar(36),
                        c.palletid
                    ) AS PalletId,

                    c.palletno AS PalletNo,

                    c.itemno AS ItemNo,
                    c.itemname AS ItemName,
                    c.spec AS Spec,

                    TRY_CONVERT(decimal(18,3), c.qty)
                        AS Qty,

                    c.stopseq AS StopSeq

                FROM dbo.vehicledispatchcargo c

                INNER JOIN dbo.vehicledispatch d
                    ON d.id = c.dispatchid

                WHERE
                    d.driveruid = {driverId}

                    AND d.status IN
                    (
                        'PENDING',
                        'IN_PROGRESS',
                        'COMPLETED'
                    )

                ORDER BY
                    c.dispatchid,
                    c.sortorder,
                    c.palletno
                """)
            .ToListAsync(cancellationToken);


        // ==================================================
        // 4. 組合成 APP 任務資料
        // ==================================================

        var result = new List<DriverTaskDto>();

        foreach (var row in taskRows)
        {
            var task = new DriverTaskDto
            {
                DispatchId = row.DispatchId.ToString(),

                TaskId = row.TaskId,

                VehicleId = row.VehicleId?.ToString(),

                VehiclePlate = row.VehiclePlate,

                ScheduledDate = row.ScheduledDate,

                ScheduledEndDate = row.ScheduledEndDate,

                Purpose = row.Purpose,

                HasCargo = row.HasCargo,

                FromPlace = row.FromPlace,

                ToPlace = row.ToPlace,

                Status = row.Status,

                Remark = row.Remark
            };


            // ==================================================
            // 目的地
            // ==================================================

            task.Destinations = stopRows
                .Where(x =>
                    x.DispatchId == row.DispatchId)
                .OrderBy(x => x.Seq)
                .Select(x => new DriverTaskStopDto
                {
                    StopId = x.StopId.ToString(),

                    Seq = x.Seq,

                    StopType = x.StopType,

                    Name = x.Name ?? string.Empty,

                    Address = x.Address,

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
                })
                .ToList();


            // ==================================================
            // 棧板
            // ==================================================

            task.Pallets = cargoRows
                 .Where(x =>
                     x.DispatchId == row.DispatchId)
                 .Select(x => new DriverTaskCargoDto
                 {
                     PalletId = x.PalletId,

                     PalletNo = x.PalletNo,

                     ItemNo = x.ItemNo,

                     ItemName = x.ItemName,

                     Spec = x.Spec,

                     Qty = x.Qty,

                     StopSeq = x.StopSeq
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
        if (driverId == Guid.Empty ||
            string.IsNullOrWhiteSpace(dispatchNo))
        {
            return null;
        }

        // ==========================================
        // 1. 查詢派車單基本資訊
        // ==========================================

        var taskRows = await _db.Database
            .SqlQuery<DriverTaskDetailDbRow>($"""
            SELECT TOP (1)

                d.id AS DispatchId,
                d.dispatchno AS TaskId,

                d.vehicleid AS VehicleId,
                v.plateno AS VehiclePlate,

                d.driveruid AS DriverId,
                dr.name AS DriverName,

                d.startdate AS ScheduledDate,
                d.enddate AS ScheduledEndDate,

                d.purpose AS Purpose,
                CONVERT(bit, ISNULL(d.hascargo, 0))
                    AS HasCargo,

                d.cargoremark AS CargoRemark,

                d.fromplace AS FromPlace,
                d.toplace AS ToPlace,

                d.status AS Status,
                d.remark AS Remark

            FROM dbo.vehicledispatch d

            LEFT JOIN dbo.vehicle v
                ON v.id = d.vehicleid

            LEFT JOIN dbo.vehicledriver dr
                ON dr.driver_id = d.driveruid

            WHERE
                d.dispatchno = {dispatchNo}

                AND d.driveruid = {driverId}

                AND d.status IN
                (
                    'PENDING',
                    'IN_PROGRESS',
                    'COMPLETED'
                )
            """)
            .ToListAsync(cancellationToken);

        if (taskRows.Count == 0)
        {
            return null;
        }

        var taskRow = taskRows[0];

        // ==========================================
        // 2. 查詢所有停靠點
        // 不先假設 StopType 的意義
        // ==========================================

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

                TRY_CONVERT(decimal(18,3), s.startkm)
                    AS StartKm,

                TRY_CONVERT(decimal(18,3), s.endkm)
                    AS EndKm,

                TRY_CONVERT(decimal(18,3), s.distancekm)
                    AS DistanceKm,

                TRY_CONVERT(decimal(18,3), s.factorgkm)
                    AS FactorGkm,

                TRY_CONVERT(decimal(18,3), s.co2kg)
                    AS Co2Kg,

                s.arrivedate AS ArriveDate,

                s.reportremark AS ReportRemark,
                s.reportdate AS ReportDate,

                TRY_CONVERT(decimal(18,8), s.longitude)
                    AS Longitude,

                TRY_CONVERT(decimal(18,8), s.latitude)
                    AS Latitude

            FROM dbo.vehicledispatchstop s

            WHERE s.dispatchid = {taskRow.DispatchId}

            ORDER BY s.seq ASC
            """)
            .ToListAsync(cancellationToken);

        // ==========================================
        // 3. 查詢棧板清單
        // ==========================================


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

            TRY_CONVERT(decimal(18,3), c.qty)
                AS Qty,

            c.palletid AS PalletId,

            c.palletno AS PalletNo,

            c.stopseq AS StopSeq,

            c.sortorder AS SortOrder

        FROM dbo.vehicledispatchcargo c

        WHERE c.dispatchid = {taskRow.DispatchId}

        ORDER BY
            CASE WHEN c.sortorder IS NULL THEN 1 ELSE 0 END,
            c.sortorder ASC,
            c.id ASC

        """)
            .ToListAsync(cancellationToken);


        // ==========================================
        // 4. 組合詳細資訊
        // ==========================================

        var result = new DriverTaskDetailDto
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

            Status = taskRow.Status,

            Remark = taskRow.Remark,

            Stops = stopRows.Select(x =>
                new DriverTaskStopDetailDto
                {
                    StopId = x.StopId.ToString(),

                    Seq = x.Seq,

                    StopType = x.StopType,

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

            Pallets = palletRows.Select(x =>
                new DriverTaskPalletDetailDto
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

        return result;
    }
}
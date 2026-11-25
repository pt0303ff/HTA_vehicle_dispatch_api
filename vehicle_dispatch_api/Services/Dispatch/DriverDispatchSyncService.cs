using System.Data;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using vehicle_dispatch_api.Data;
using vehicle_dispatch_api.DTOs.Dispatch;

namespace vehicle_dispatch_api.Services.Dispatch;

public sealed class DriverDispatchSyncService
{
    private readonly ApplicationDbContext _db;

    public DriverDispatchSyncService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ==================================================
    // 主要同步入口
    // ==================================================

    public async Task<DispatchSyncResult> SyncAsync(
        Guid driverId,
        DispatchSyncRequest request,
        CancellationToken cancellationToken = default)
    {
        if (driverId == Guid.Empty)
            throw new ArgumentException("司機 ID 不正確");

        var execution = request.Execution
            ?? throw new ArgumentException("缺少任務執行資料");

        if (execution.ExecutionId == Guid.Empty)
            throw new ArgumentException("Execution ID 不正確");

        if (string.IsNullOrWhiteSpace(execution.TaskId))
            throw new ArgumentException("缺少派車單編號");

        var status = execution.Status.Trim().ToUpperInvariant();

        if (status != "IN_PROGRESS" && status != "COMPLETED")
            throw new ArgumentException("任務狀態不正確");

        var executionId = execution.ExecutionId;

        var taskId = execution.TaskId.Trim();

        var startedAt = RequiredDate(
            execution.StartedAt, "StartedAt");

        var createdAt = RequiredDate(
            execution.CreatedAt, "CreatedAt");

        var updatedAt = RequiredDate(
            execution.UpdatedAt, "UpdatedAt");

        var completedAt = ToUtcMilliseconds(
            execution.CompletedAt);

        if (status == "COMPLETED" && completedAt == null)
            throw new ArgumentException("完成任務缺少 CompletedAt");

        if (execution.CurrentDestinationIndex < 0)
            throw new ArgumentException("目的地進度不正確");

        int? startMileage = ToMileage(
            execution.StartMileage,
            "StartMileage");

        var startLatitude = ToCoordinate(
            execution.StartLatitude, -90, 90, "StartLatitude");

        var startLongitude = ToCoordinate(
            execution.StartLongitude, -180, 180, "StartLongitude");

        ValidateCoordinatePair(
            startLatitude, startLongitude, "出發 GPS");

        var startCargoJson = ReadOptionalJson(
            execution.StartCargo, "StartCargo");

        var loadedPallets = ReadStringArray(
            execution.LoadedPalletNos, "LoadedPalletNos");

        if (loadedPallets.Count == 0)
            throw new ArgumentException("載運棧板不可為空");

        var loadedSet = new HashSet<string>(
            loadedPallets,
            StringComparer.OrdinalIgnoreCase);

        if (loadedSet.Count != loadedPallets.Count)
            throw new ArgumentException("載運棧板有重複編號");

        var checkIns = request.CheckIns ?? new();

        var events = request.Events ?? new();

        // ==========================================
        // 1. 開始 SQL Transaction
        // ==========================================

        await using var transaction =
            await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        // ==========================================
        // 2. 查詢派車單
        // task_id = dispatchno
        // JWT driverId = vehicledispatch.driver_id
        // ==========================================

        var dispatchRows = await _db.Database
            .SqlQuery<DispatchSyncDispatchRow>($"""
                SELECT TOP (1)

                    d.id AS DispatchId,

                    d.dispatchno AS DispatchNo,

                    d.toplace AS ToPlace,

                    d.status AS Status

                FROM dbo.vehicledispatch d
                    WITH (UPDLOCK, HOLDLOCK)

                WHERE
                    d.dispatchno = {taskId}

                    AND d.driver_id = {driverId}

                    AND d.status IN ('APPLY', 'DONE')
                """)
            .ToListAsync(cancellationToken);

        if (dispatchRows.Count == 0)
        {
            throw new KeyNotFoundException(
                "找不到派車單，或目前司機無權限同步此任務");
        }

        var dispatch = dispatchRows[0];

        var dispatchId = dispatch.DispatchId;

        var dispatchStatus = dispatch.Status.Trim().ToUpperInvariant();

        // DONE 是派車單的最終狀態，不允許再接受較舊的執行中快照。
        // COMPLETED 的重送則允許通過，確保同步逾時後可以安全重試。
        if (dispatchStatus == "DONE" && status != "COMPLETED")
        {
            throw new InvalidOperationException(
                "此派車單已完成，不接受較舊的執行中資料");
        }

        if (string.IsNullOrWhiteSpace(dispatch.ToPlace))
            throw new ArgumentException("派車單缺少最終目的地");

        // ==========================================
        // 3. 查詢中途停靠點
        // 不包含起點及最終終點
        // ==========================================

        var stops = await _db.Database
            .SqlQuery<DispatchSyncStopRow>($"""
                SELECT
                    s.id AS StopId,
                    s.seq AS Sequence,
                    s.destname AS DestinationName
                FROM dbo.vehicledispatchstop s
                    WITH (UPDLOCK, HOLDLOCK)
                WHERE
                    s.dispatchid = {dispatchId}

                    AND UPPER(
                        LTRIM(RTRIM(ISNULL(s.stoptype, '')))
                    ) = 'STOP'

                ORDER BY s.seq ASC
                """)
    .ToListAsync(cancellationToken);

        // 最後一站一定是 vehicledispatch 的 toplace
        int totalDestinations = stops.Count + 1;

        if (execution.CurrentDestinationIndex > totalDestinations)
            throw new ArgumentException("目的地進度超出派車單範圍");

        if (checkIns.Count != execution.CurrentDestinationIndex)
        {
            throw new ArgumentException(
                "目的地進度與抵達紀錄數量不一致");
        }

        // ==========================================
        // 4. 查詢派車單棧板清單
        // ==========================================

        var assignedPallets = await _db.Database
            .SqlQuery<string>($"""
                SELECT
                    c.palletno AS Value

                FROM dbo.vehicledispatchcargo c

                WHERE
                    c.dispatchid = {dispatchId}

                    AND c.palletno IS NOT NULL

                    AND LTRIM(RTRIM(c.palletno)) <> ''
                """)
            .ToListAsync(cancellationToken);

        var assignedSet = new HashSet<string>(
            assignedPallets,
            StringComparer.OrdinalIgnoreCase);

        if (!assignedSet.IsSupersetOf(loadedSet))
        {
            throw new ArgumentException(
                "載運棧板包含不屬於此派車單的棧板");
        }

        // ==========================================
        // 5. 驗證抵達紀錄
        // ==========================================

        if (checkIns.Select(x => x.CheckInId).Distinct().Count()
            != checkIns.Count)
        {
            throw new ArgumentException("CheckIn ID 重複");
        }

        var parsedCheckIns = new List<ParsedCheckIn>();

        var alreadyUnloaded = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var previousMileage = startMileage;

        var orderedCheckIns = checkIns
            .OrderBy(x => x.DestinationSequence)
            .ToList();

        for (int i = 0; i < orderedCheckIns.Count; i++)
        {
            var item = orderedCheckIns[i];

            int expectedSequence = i + 1;

            if (item.CheckInId == Guid.Empty ||
                item.ExecutionId != executionId ||
                item.DestinationId == Guid.Empty)
            {
                throw new ArgumentException("抵達紀錄 GUID 不正確");
            }

            if (item.DestinationSequence != expectedSequence)
            {
                throw new ArgumentException(
                    "目的地抵達順序不連續");
            }

            if (string.IsNullOrWhiteSpace(item.DestinationName))
                throw new ArgumentException("目的地名稱不可為空");

            var arrivedAt = RequiredDate(
                item.ArrivedAt, "ArrivedAt");

            var checkInCreatedAt = RequiredDate(
                item.CreatedAt, "CheckIn.CreatedAt");

            int mileage = ToRequiredMileage(
                item.Mileage, "CheckIn.Mileage");

            if (previousMileage.HasValue &&
                mileage < previousMileage.Value)
            {
                throw new ArgumentException(
                    "抵達里程不可小於前次打卡里程");
            }

            previousMileage = mileage;

            var latitude = ToCoordinate(
                item.Latitude, -90, 90, "CheckIn.Latitude");

            var longitude = ToCoordinate(
                item.Longitude, -180, 180, "CheckIn.Longitude");

            ValidateCoordinatePair(
                latitude, longitude, "抵達 GPS");

            // --------------------------------------
            // 判斷中途停靠點或最終終點
            // --------------------------------------

            Guid expectedDestinationId;

            string expectedDestinationName;

            if (expectedSequence <= stops.Count)
            {
                // 中途停靠點
                var stop = stops[expectedSequence - 1];

                expectedDestinationId = stop.StopId;

                expectedDestinationName =
                    stop.DestinationName ?? string.Empty;
            }
            else
            {
                // 最終終點
                expectedDestinationId = dispatchId;

                expectedDestinationName = dispatch.ToPlace!;
            }

            if (item.DestinationId != expectedDestinationId)
            {
                throw new ArgumentException(
                    $"第 {expectedSequence} 站 destination_id 不正確");
            }

            if (!string.Equals(
                item.DestinationName.Trim(),
                expectedDestinationName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"第 {expectedSequence} 站目的地名稱不一致");
            }

            var unloadedPallets = ReadStringArray(
                item.UnloadedPalletNos,
                "UnloadedPalletNos");

            foreach (var pallet in unloadedPallets)
            {
                if (!loadedSet.Contains(pallet))
                {
                    throw new ArgumentException(
                        $"卸貨棧板不屬於本次載運清單：{pallet}");
                }

                if (!alreadyUnloaded.Add(pallet))
                {
                    throw new ArgumentException(
                        $"棧板重複卸貨：{pallet}");
                }
            }

            parsedCheckIns.Add(new ParsedCheckIn
            {
                Source = item,
                ArrivedAt = arrivedAt,
                CreatedAt = checkInCreatedAt,
                Mileage = mileage,
                Latitude = latitude,
                Longitude = longitude,
                UnloadedJson = JsonSerializer.Serialize(
                    unloadedPallets)
            });
        }

        // ==========================================
        // 6. 完成任務時檢查所有目的地與棧板
        // ==========================================

        if (status == "COMPLETED")
        {
            if (execution.CurrentDestinationIndex != totalDestinations)
            {
                throw new ArgumentException(
                    "任務完成前必須完成所有目的地抵達打卡");
            }

            if (!loadedSet.SetEquals(alreadyUnloaded))
            {
                throw new ArgumentException(
                    "任務完成時仍有棧板尚未卸貨");
            }
        }

        // ==========================================
        // 7. 驗證同步事件
        // ==========================================

        if (status == "COMPLETED" &&
            !events.Any(x => x.EventType == "TASK_COMPLETED"))
        {
            throw new ArgumentException("完成任務缺少 TASK_COMPLETED 事件");
        }

        if (events.Select(x => x.QueueId).Distinct().Count()
            != events.Count)
        {
            throw new ArgumentException("Queue ID 重複");
        }

        var allowedEventTypes = new HashSet<string>
        {
            "TASK_STARTED",
            "DESTINATION_ARRIVED",
            "TASK_COMPLETED"
        };

        var parsedEvents = new List<ParsedEvent>();

        foreach (var item in events)
        {
            if (item.QueueId == Guid.Empty ||
                item.ExecutionId != executionId)
            {
                throw new ArgumentException("同步事件 GUID 不正確");
            }

            if (!allowedEventTypes.Contains(item.EventType))
                throw new ArgumentException("不支援的事件類型");

            if (item.AttemptCount < 0)
                throw new ArgumentException("AttemptCount 不正確");

            var eventCreatedAt = RequiredDate(
                item.CreatedAt, "Event.CreatedAt");

            var payloadJson = ReadJsonObject(
                item.PayloadJson, "PayloadJson");

            using var document = JsonDocument.Parse(payloadJson);

            var root = document.RootElement;

            // 驗證 Payload executionId
            if (!root.TryGetProperty(
                    "executionId",
                    out var payloadExecutionId) ||
                payloadExecutionId.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(
                    payloadExecutionId.GetString(),
                    out var payloadId) ||
                payloadId != executionId)
            {
                throw new ArgumentException(
                    "事件 Payload Execution ID 不一致");
            }

            // 驗證 Payload eventType
            if (!root.TryGetProperty(
                    "eventType",
                    out var payloadEventType) ||
                payloadEventType.GetString() != item.EventType)
            {
                throw new ArgumentException(
                    "事件類型與 Payload 不一致");
            }

            // DESTINATION_ARRIVED 必須對應抵達紀錄
            if (item.EventType == "DESTINATION_ARRIVED")
            {
                if (!root.TryGetProperty(
                        "destination",
                        out var destination))
                {
                    throw new ArgumentException(
                        "抵達事件缺少 destination");
                }

                var eventId = destination
                    .GetProperty("id")
                    .GetString();

                var eventName = destination
                    .GetProperty("name")
                    .GetString();

                var eventSequence = destination
                    .GetProperty("sequence")
                    .GetInt32();

                var matched = parsedCheckIns.FirstOrDefault(
                    x => x.Source.DestinationSequence == eventSequence);

                if (matched == null ||
                    eventId != matched.Source.DestinationId.ToString() ||
                    eventName != matched.Source.DestinationName)
                {
                    throw new ArgumentException(
                        "抵達事件與抵達紀錄不一致");
                }
            }

            if (item.EventType == "TASK_COMPLETED" &&
                status != "COMPLETED")
            {
                throw new ArgumentException(
                    "完成事件與任務狀態不一致");
            }

            parsedEvents.Add(new ParsedEvent
            {
                Source = item,
                CreatedAt = eventCreatedAt,
                PayloadJson = payloadJson
            });
        }

        // ==========================================
        // 8. 檢查並寫入任務執行主檔
        //    (同一派車單只能有一筆 Execution)
        // ==========================================

        var now = DateTime.UtcNow;

        var loadedJson = JsonSerializer.Serialize(loadedPallets);

        var existingExecutions = await _db.Database
            .SqlQuery<DispatchSyncExecutionRow>($"""
                SELECT

                    execution_id AS ExecutionId,

                    dispatch_id AS DispatchId,

                    status AS Status,

                    current_destination_index
                        AS CurrentDestinationIndex,

                    completed_at AS CompletedAt,

                    loaded_pallet_nos AS LoadedPalletNos

                FROM dbo.vehicledispatchexecution
                    WITH (UPDLOCK, HOLDLOCK)

                WHERE dispatch_id = {dispatchId}
                """)
            .ToListAsync(cancellationToken);

        if (existingExecutions.Count == 0)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.vehicledispatchexecution
                (
                    execution_id,
                    dispatch_id,
                    status,
                    startmile,
                    startcargo,
                    start_latitude,
                    start_longitude,
                    loaded_pallet_nos,
                    current_destination_index,
                    completed_at,
                    sync_status,
                    synced_at,
                    created_at,
                    updated_at
                )
                VALUES
                (
                    {executionId},
                    {dispatchId},
                    {status},
                    {startMileage},
                    {startCargoJson},
                    {startLatitude},
                    {startLongitude},
                    {loadedJson},
                    {execution.CurrentDestinationIndex},
                    {completedAt},
                    N'synced',
                    {now},
                    {createdAt},
                    {updatedAt}
                )
                """, cancellationToken);
        }
        else
        {
            var existing = existingExecutions[0];

            if (existing.ExecutionId != executionId)
            {
                throw new InvalidOperationException(
                    "此派車單已經存在其他任務執行紀錄，" +
                    "不可建立新的 Execution ID");
            }

            var existingPallets = ParseStoredStringArray(
                existing.LoadedPalletNos);

            if (!loadedSet.SetEquals(existingPallets))
            {
                throw new InvalidOperationException(
                    "相同 Execution ID 的載運棧板資料不一致");
            }

            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE dbo.vehicledispatchexecution

                SET
                    status =
                        CASE
                            WHEN status = N'COMPLETED'
                                THEN status
                            ELSE {status}
                        END,

                    startmile = COALESCE(
                        startmile,
                        {startMileage}
                    ),

                    startcargo = COALESCE(
                        startcargo,
                        {startCargoJson}
                    ),

                    start_latitude = COALESCE(
                        start_latitude,
                        {startLatitude}
                    ),

                    start_longitude = COALESCE(
                        start_longitude,
                        {startLongitude}
                    ),

                    current_destination_index =
                        CASE
                            WHEN current_destination_index >
                                {execution.CurrentDestinationIndex}
                            THEN current_destination_index
                            ELSE {execution.CurrentDestinationIndex}
                        END,

                    completed_at = COALESCE(
                        completed_at,
                        {completedAt}
                    ),

                    sync_status = N'synced',

                    synced_at = {now},

                    updated_at =
                        CASE
                            WHEN updated_at > {updatedAt}
                            THEN updated_at
                            ELSE {updatedAt}
                        END

                WHERE execution_id = {executionId}
                """, cancellationToken);
        }

        // ==========================================
        // 9. 寫入抵達紀錄
        // ==========================================

        var receivedCheckInIds = new List<Guid>();

        int insertedCheckIns = 0;

        foreach (var parsed in parsedCheckIns)
        {
            var item = parsed.Source;

            var checkInId = item.CheckInId;
            var destinationId = item.DestinationId;
            var destinationName = item.DestinationName;
            var sequence = item.DestinationSequence;
            var arrivedAt = parsed.ArrivedAt;
            var mileage = parsed.Mileage;
            var latitude = parsed.Latitude;
            var longitude = parsed.Longitude;
            var unloadedJson = parsed.UnloadedJson;
            var checkInCreatedAt = parsed.CreatedAt;

            var idExists = await CountAsync($"""
                SELECT COUNT(*) AS Value

                FROM dbo.vehicledispatch_checkin

                WHERE checkin_id = {checkInId}
                """, cancellationToken);

            var sameRecord = await CountAsync($"""
                SELECT COUNT(*) AS Value

                FROM dbo.vehicledispatch_checkin

                WHERE
                    checkin_id = {checkInId}

                    AND execution_id = {executionId}

                    AND destination_id = {destinationId}

                    AND destination_name = {destinationName}

                    AND destination_sequence = {sequence}

                    AND arrived_at = {arrivedAt}

                    AND mileage = {mileage}

                    AND (
                        latitude = {latitude}
                        OR (latitude IS NULL AND {latitude} IS NULL)
                    )

                    AND (
                        longitude = {longitude}
                        OR (longitude IS NULL AND {longitude} IS NULL)
                    )

                    AND unloaded_pallet_nos = {unloadedJson}

                    AND created_at = {checkInCreatedAt}
                """, cancellationToken);

            if (idExists > 0 && sameRecord == 0)
            {
                throw new InvalidOperationException(
                    "抵達紀錄 ID 已存在，但內容不一致");
            }

            if (idExists == 0)
            {
                var sequenceExists = await CountAsync($"""
                    SELECT COUNT(*) AS Value

                    FROM dbo.vehicledispatch_checkin

                    WHERE
                        execution_id = {executionId}

                        AND destination_sequence = {sequence}
                    """, cancellationToken);

                if (sequenceExists > 0)
                {
                    throw new InvalidOperationException(
                        $"第 {sequence} 站已存在其他抵達紀錄");
                }

                await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.vehicledispatch_checkin
                    (
                        checkin_id,
                        execution_id,
                        destination_id,
                        destination_name,
                        destination_sequence,
                        arrived_at,
                        mileage,
                        latitude,
                        longitude,
                        unloaded_pallet_nos,
                        sync_status,
                        created_at,
                        synced_at
                    )
                    VALUES
                    (
                        {checkInId},
                        {executionId},
                        {destinationId},
                        {destinationName},
                        {sequence},
                        {arrivedAt},
                        {mileage},
                        {latitude},
                        {longitude},
                        {unloadedJson},
                        N'synced',
                        {checkInCreatedAt},
                        {now}
                    )
                    """, cancellationToken);

                insertedCheckIns++;
            }

            receivedCheckInIds.Add(checkInId);
        }

        // ==========================================
        // 10. 寫入同步事件佇列
        // ==========================================

        var receivedQueueIds = new List<Guid>();

        int insertedEvents = 0;

        foreach (var parsed in parsedEvents)
        {
            var item = parsed.Source;

            var queueId = item.QueueId;
            var eventType = item.EventType;
            var payloadJson = parsed.PayloadJson;
            var eventCreatedAt = parsed.CreatedAt;
            var attemptCount = item.AttemptCount;
            var lastError = item.LastError;

            var idExists = await CountAsync($"""
                SELECT COUNT(*) AS Value

                FROM dbo.vehicledispatchsync_queue

                WHERE queue_id = {queueId}
                """, cancellationToken);

            var sameRecord = await CountAsync($"""
                SELECT COUNT(*) AS Value

                FROM dbo.vehicledispatchsync_queue

                WHERE
                    queue_id = {queueId}

                    AND execution_id = {executionId}

                    AND event_type = {eventType}

                    AND payload_json = {payloadJson}

                    AND created_at = {eventCreatedAt}

                    AND attempt_count = {attemptCount}

                    AND (
                        last_error = {lastError}
                        OR (last_error IS NULL AND {lastError} IS NULL)
                    )
                """, cancellationToken);

            if (idExists > 0 && sameRecord == 0)
            {
                throw new InvalidOperationException(
                    "Queue ID 已存在，但事件內容不一致");
            }

            if (idExists == 0)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.vehicledispatchsync_queue
                    (
                        queue_id,
                        execution_id,
                        event_type,
                        payload_json,
                        created_at,
                        attempt_count,
                        last_error
                    )
                    VALUES
                    (
                        {queueId},
                        {executionId},
                        {eventType},
                        {payloadJson},
                        {eventCreatedAt},
                        {attemptCount},
                        {lastError}
                    )
                    """, cancellationToken);

                insertedEvents++;
            }

            receivedQueueIds.Add(queueId);
        }

        // ==========================================
        // 11. 更新派車單正式狀態
        // 只有整趟任務完成且前述目的地／棧板驗證與資料寫入均成功，
        // 才將 vehicledispatch.status 從 APPLY 更新為 DONE。
        // 此更新與 execution/check-in/queue 寫入位於同一個 SQL Transaction。
        // ==========================================

        if (status == "COMPLETED")
        {
            var updatedDispatchRows = await _db.Database
                .ExecuteSqlInterpolatedAsync($"""
                    UPDATE dbo.vehicledispatch
                    SET status = N'DONE'
                    WHERE
                        id = {dispatchId}
                        AND driver_id = {driverId}
                        AND status IN (N'APPLY', N'DONE')
                    """, cancellationToken);

            if (updatedDispatchRows != 1)
            {
                throw new InvalidOperationException(
                    "派車單狀態更新失敗，請確認派車單仍為 APPLY 或 DONE");
            }
        }

        // ==========================================
        // 12. 提交 Transaction
        // ==========================================

        await transaction.CommitAsync(cancellationToken);

        return new DispatchSyncResult
        {
            ExecutionId = executionId,

            InsertedCheckIns = insertedCheckIns,

            InsertedEvents = insertedEvents,

            ReceivedCheckInIds = receivedCheckInIds,

            ReceivedQueueIds = receivedQueueIds
        };
    }

    // ==================================================
    // SQL COUNT
    // ==================================================

    private Task<int> CountAsync(
        FormattableString sql,
        CancellationToken cancellationToken)
    {
        return _db.Database
            .SqlQuery<int>(sql)
            .SingleAsync(cancellationToken);
    }

    // ==================================================
    // JSON Array
    // ==================================================

    private static List<string> ReadStringArray(
        JsonElement? value,
        string fieldName)
    {
        if (!value.HasValue ||
            value.Value.ValueKind == JsonValueKind.Null ||
            value.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException($"{fieldName} 不可為空");
        }

        var element = value.Value;

        if (element.ValueKind == JsonValueKind.String)
        {
            var json = element.GetString();

            using var document = JsonDocument.Parse(
                json ?? "[]");

            element = document.RootElement;

            return ReadArrayElement(element, fieldName);
        }

        return ReadArrayElement(element, fieldName);
    }

    private static List<string> ReadArrayElement(
        JsonElement element,
        string fieldName)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{fieldName} 必須是 JSON Array");

        var result = new List<string>();

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"{fieldName} 格式錯誤");

            var value = item.GetString();

            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{fieldName} 不可有空白項目");

            result.Add(value);
        }

        return result;
    }

    private static List<string> ParseStoredStringArray(string json)
    {
        using var document = JsonDocument.Parse(json);

        return ReadArrayElement(
            document.RootElement,
            "LoadedPalletNos");
    }

    // ==================================================
    // JSON Object
    // ==================================================

    private static string ReadJsonObject(
        JsonElement? value,
        string fieldName)
    {
        if (!value.HasValue ||
            value.Value.ValueKind == JsonValueKind.Null ||
            value.Value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException($"{fieldName} 不可為空");
        }

        var element = value.Value;

        var json = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();

        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException($"{fieldName} 不可為空");

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{fieldName} 必須是 JSON Object");

        return json;
    }

    private static string? ReadOptionalJson(
        JsonElement? value,
        string fieldName)
    {
        if (!value.HasValue ||
            value.Value.ValueKind == JsonValueKind.Null ||
            value.Value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var element = value.Value;

        var json = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();

        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var document = JsonDocument.Parse(json);

        var kind = document.RootElement.ValueKind;

        if (kind != JsonValueKind.Object &&
            kind != JsonValueKind.Array)
        {
            throw new ArgumentException($"{fieldName} JSON 格式不正確");
        }

        return json;
    }

    // ==================================================
    // 里程轉換：SQL INT
    // ==================================================

    private static int? ToMileage(
        double? value,
        string fieldName)
    {
        if (!value.HasValue)
            return null;

        if (!double.IsFinite(value.Value) ||
            value.Value < 0 ||
            value.Value > int.MaxValue ||
            value.Value != Math.Truncate(value.Value))
        {
            throw new ArgumentException(
                $"{fieldName} 必須是非負整數");
        }

        return checked((int)value.Value);
    }

    private static int ToRequiredMileage(
        double value,
        string fieldName)
    {
        return ToMileage(value, fieldName)
            ?? throw new ArgumentException($"{fieldName} 不可為空");
    }

    // ==================================================
    // GPS：SQL DECIMAL(9,6)
    // ==================================================

    private static decimal? ToCoordinate(
        double? value,
        double min,
        double max,
        string fieldName)
    {
        if (!value.HasValue)
            return null;

        if (!double.IsFinite(value.Value) ||
            value.Value < min ||
            value.Value > max)
        {
            throw new ArgumentException($"{fieldName} 座標不正確");
        }

        return decimal.Round(
            (decimal)value.Value,
            6,
            MidpointRounding.AwayFromZero);
    }

    private static void ValidateCoordinatePair(
        decimal? latitude,
        decimal? longitude,
        string fieldName)
    {
        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException(
                $"{fieldName} 緯度與經度必須同時提供或同時為空");
        }
    }

    // ==================================================
    // UTC 時間
    // ==================================================

    private static DateTime RequiredDate(
        DateTime? value,
        string fieldName)
    {
        if (!value.HasValue)
            throw new ArgumentException($"{fieldName} 不可為空");

        return ToUtcMilliseconds(value.Value);
    }

    private static DateTime ToUtcMilliseconds(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,

            DateTimeKind.Local => value.ToUniversalTime(),

            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        // SQL Server DATETIME2(3)
        var ticks = (
            (utc.Ticks + TimeSpan.TicksPerMillisecond / 2)
            / TimeSpan.TicksPerMillisecond
        ) * TimeSpan.TicksPerMillisecond;

        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private static DateTime? ToUtcMilliseconds(DateTime? value)
    {
        return value.HasValue
            ? ToUtcMilliseconds(value.Value)
            : null;
    }

    // ==================================================
    // 內部 SQL 查詢資料
    // ==================================================

    public sealed class DispatchSyncDispatchRow
    {
        public Guid DispatchId { get; set; }

        public string DispatchNo { get; set; } = string.Empty;

        public string? ToPlace { get; set; }

        public string Status { get; set; } = string.Empty;
    }

    public sealed class DispatchSyncStopRow
    {
        public Guid StopId { get; set; }

        public int Sequence { get; set; }

        public string? DestinationName { get; set; }
    }

    public sealed class DispatchSyncExecutionRow
    {
        public Guid ExecutionId { get; set; }

        public Guid DispatchId { get; set; }

        public string Status { get; set; } = string.Empty;

        public int CurrentDestinationIndex { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string LoadedPalletNos { get; set; } = "[]";
    }

    private sealed class ParsedCheckIn
    {
        public DispatchCheckInSyncDto Source { get; set; } = null!;

        public DateTime ArrivedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public int Mileage { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        public string UnloadedJson { get; set; } = "[]";
    }

    private sealed class ParsedEvent
    {
        public DispatchSyncEventDto Source { get; set; } = null!;

        public DateTime CreatedAt { get; set; }

        public string PayloadJson { get; set; } = string.Empty;
    }
}
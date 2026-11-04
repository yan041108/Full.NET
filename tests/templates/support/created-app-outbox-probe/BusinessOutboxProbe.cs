using System.Data.Common;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Serialization.MemoryPack;

internal static class BusinessOutboxProbe
{
    // 只读验收业务 API 写出的事件；按载荷 UnitId 精确匹配，避免同租户其他变更误报。
    private const string OutboxColumns = """
        SELECT Id, SchemaVersion, ContentType, Payload, Attempts,
               CASE WHEN ProcessedAtUtc IS NULL THEN 0 ELSE 1 END AS IsProcessed,
               CASE WHEN DeadLetteredAtUtc IS NULL THEN 0 ELSE 1 END AS IsDeadLettered,
               DeadLetterReasonCode,
               CASE WHEN LockId IS NULL AND LockedUntilUtc IS NULL THEN 1 ELSE 0 END AS IsLeaseReleased,
               CASE WHEN NextAttemptAtUtc IS NULL THEN 1 ELSE 0 END AS IsRetryCleared
        FROM fn_outbox_message
        """;

    private const string FindOutboxSql = OutboxColumns +
        "\nWHERE TenantId = @TenantId AND MessageType = @MessageType";
    private const string OutboxByIdSql = OutboxColumns + "\nWHERE Id = @MessageId";

    private const string ProjectionSql = """
        SELECT Name, IsActive, SourceVersion
        FROM fn_identity_organization_unit_projection
        WHERE TenantId = @TenantId AND UnitId = @UnitId
        """;

    public static async Task<BusinessOutboxState> WaitAsync(
        DbConnection connection,
        Guid tenantId,
        Guid unitId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        BusinessOutboxState? last = null;
        do
        {
            last = await ReadAsync(connection, tenantId, unitId, last?.MessageId);
            // 成功终态还必须有消费方投影；单凭 Outbox 已处理不足以证明跨模块结果。
            if (last is { IsProcessed: 1, ProjectionName: not null }
                || last is { IsDeadLettered: 1 })
            {
                return last;
            }

            await Task.Delay(250);
        } while (DateTimeOffset.UtcNow < deadline);

        return last ?? throw new TimeoutException(
            $"No Organization Outbox event found for tenant {tenantId:D}, unit {unitId:D}.");
    }

    private static async Task<BusinessOutboxState?> ReadAsync(
        DbConnection connection,
        Guid tenantId,
        Guid unitId,
        Guid? messageId)
    {
        BusinessOutboxState? matching = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = messageId.HasValue ? OutboxByIdSql : FindOutboxSql;
            command.CommandTimeout = 10;
            if (messageId.HasValue)
            {
                AddParameter(command, "MessageId", messageId.Value);
            }
            else
            {
                AddParameter(command, "TenantId", tenantId);
                AddParameter(command, "MessageType", IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged);
            }
            await using var reader = await command.ExecuteReaderAsync();
            var serializer = new MemoryPackIntegrationEventSerializer();
            while (await reader.ReadAsync())
            {
                var payload = serializer.Deserialize<IdentityOrganizationUnitChangedIntegrationEvent>(
                    (byte[])reader["Payload"]);
                if (payload.UnitId != unitId)
                {
                    continue;
                }

                if (payload.TenantId != tenantId || matching is not null)
                {
                    throw new InvalidOperationException("Organization Outbox payload has a mismatched tenant or duplicate unit event.");
                }

                matching = new BusinessOutboxState
                {
                    TenantId = tenantId,
                    UnitId = unitId,
                    MessageId = reader.GetGuid(reader.GetOrdinal("Id")),
                    MessageType = IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
                    SchemaVersion = Convert.ToInt32(reader["SchemaVersion"]),
                    ContentType = Convert.ToString(reader["ContentType"])!,
                    PayloadName = payload.Name,
                    PayloadVersion = payload.Version,
                    Attempts = Convert.ToInt32(reader["Attempts"]),
                    IsProcessed = Convert.ToInt64(reader["IsProcessed"]),
                    IsDeadLettered = Convert.ToInt64(reader["IsDeadLettered"]),
                    DeadLetterReasonCode = reader["DeadLetterReasonCode"] is DBNull
                        ? null : Convert.ToString(reader["DeadLetterReasonCode"]),
                    IsLeaseReleased = Convert.ToInt64(reader["IsLeaseReleased"]),
                    IsRetryCleared = Convert.ToInt64(reader["IsRetryCleared"]),
                };
            }
        }

        if (matching is null)
        {
            return null;
        }

        await using var projectionCommand = connection.CreateCommand();
        projectionCommand.CommandText = ProjectionSql;
        projectionCommand.CommandTimeout = 10;
        AddParameter(projectionCommand, "TenantId", tenantId);
        AddParameter(projectionCommand, "UnitId", unitId);
        await using var projectionReader = await projectionCommand.ExecuteReaderAsync();
        if (await projectionReader.ReadAsync())
        {
            matching.ProjectionName = Convert.ToString(projectionReader["Name"]);
            matching.ProjectionVersion = Convert.ToInt64(projectionReader["SourceVersion"]);
            matching.ProjectionIsActive = Convert.ToInt64(projectionReader["IsActive"]);
        }

        return matching;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

internal sealed class BusinessOutboxState
{
    public Guid TenantId { get; init; }
    public Guid UnitId { get; init; }
    public Guid MessageId { get; init; }
    public string MessageType { get; init; } = "";
    public int SchemaVersion { get; init; }
    public string ContentType { get; init; } = "";
    public string PayloadName { get; init; } = "";
    public long PayloadVersion { get; init; }
    public int Attempts { get; init; }
    public long IsProcessed { get; init; }
    public long IsDeadLettered { get; init; }
    public string? DeadLetterReasonCode { get; init; }
    public long IsLeaseReleased { get; init; }
    public long IsRetryCleared { get; init; }
    public string? ProjectionName { get; set; }
    public long? ProjectionVersion { get; set; }
    public long? ProjectionIsActive { get; set; }
}

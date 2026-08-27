using System.Data;
using System.Data.Common;

namespace Full.NET.Data.Dapper.Outbox;

/// <summary>
/// 传统 Outbox INSERT 的固定强类型命令 Plan，不经过 DynamicParameters 或运行时形状查找。
/// </summary>
internal sealed class OutboxInsertTypedCommandPlan()
    : DapperTypedCommandPlan<OutboxMessage>(Sql)
{
    internal static OutboxInsertTypedCommandPlan Instance { get; } = new();

    internal const string Sql =
        """
        INSERT INTO fn_outbox_message
            (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc, Attempts)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAtUtc, 0)
        """;

    protected override void AddParameters(DbCommand command)
    {
        AddParameter(command, "Id", DbType.Guid);
        AddParameter(command, "MessageType");
        AddParameter(command, "SchemaVersion");
        AddParameter(command, "ContentType");
        AddParameter(command, "TenantId", DbType.Guid);
        AddParameter(command, "TraceId");
        AddParameter(command, "Payload");
        AddParameter(command, "OccurredAtUtc");
    }

    protected override void UpdateParameters(DbCommand command, OutboxMessage args)
    {
        SetAssignedGuid(command.Parameters[0], args.Id);
        command.Parameters[1].Value = args.MessageType;
        command.Parameters[2].Value = args.SchemaVersion;
        command.Parameters[3].Value = args.ContentType;
        SetOptionalAssignedGuid(command.Parameters[4], args.TenantId);
        command.Parameters[5].Value = AsValue(args.TraceId);
        command.Parameters[6].Value = args.Payload;
        SetUtcDateTimeOffset(command.Parameters[7], args.OccurredAtUtc);
    }
}

/// <summary>
/// 追加式 Outbox INSERT 的固定强类型命令 Plan，显式覆盖 CDC 元数据的全部参数槽。
/// </summary>
internal sealed class AppendOnlyOutboxInsertTypedCommandPlan()
    : DapperTypedCommandPlan<AppendOnlyOutboxMessage>(Sql)
{
    internal static AppendOnlyOutboxInsertTypedCommandPlan Instance { get; } = new();

    internal const string Sql =
        """
        INSERT INTO fn_messaging_outbox_event
            (Id, MessageType, SchemaVersion, ContentType, TenantId, PartitionKey,
             CorrelationId, CausationId, TraceParent, Producer, Payload, OccurredAtUtc)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @PartitionKey,
             @CorrelationId, @CausationId, @TraceParent, @Producer, @Payload, @OccurredAtUtc)
        """;

    protected override void AddParameters(DbCommand command)
    {
        AddParameter(command, "Id", DbType.Guid);
        AddParameter(command, "MessageType");
        AddParameter(command, "SchemaVersion");
        AddParameter(command, "ContentType");
        AddParameter(command, "TenantId", DbType.Guid);
        AddParameter(command, "PartitionKey");
        AddParameter(command, "CorrelationId");
        AddParameter(command, "CausationId", DbType.Guid);
        AddParameter(command, "TraceParent");
        AddParameter(command, "Producer");
        AddParameter(command, "Payload");
        AddParameter(command, "OccurredAtUtc");
    }

    protected override void UpdateParameters(
        DbCommand command,
        AppendOnlyOutboxMessage args)
    {
        SetAssignedGuid(command.Parameters[0], args.Id);
        command.Parameters[1].Value = args.MessageType;
        command.Parameters[2].Value = args.SchemaVersion;
        command.Parameters[3].Value = args.ContentType;
        SetOptionalAssignedGuid(command.Parameters[4], args.TenantId);
        command.Parameters[5].Value = args.PartitionKey;
        command.Parameters[6].Value = AsValue(args.CorrelationId);
        SetOptionalAssignedGuid(command.Parameters[7], args.CausationId);
        command.Parameters[8].Value = AsValue(args.TraceParent);
        command.Parameters[9].Value = args.Producer;
        command.Parameters[10].Value = args.Payload;
        SetUtcDateTimeOffset(command.Parameters[11], args.OccurredAtUtc);
    }
}

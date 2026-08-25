#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper.Inbox;
using Full.NET.Data.Dapper.Outbox;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper 基础设施自身的 Native AOT 行物化注册，避免依赖业务模块启动顺序。
/// </summary>
internal static class DapperAotInfrastructureRegistration
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (RegistrationLock)
        {
            if (_registered)
            {
                return;
            }

            DapperAotMaterializerRegistry.Register<InboxClaimRow>(reader =>
                new InboxClaimRow(
                    reader.GetString(0),
                    reader.GetFieldValue<byte[]>(1)));
            DapperAotMaterializerRegistry.Register<InboxBatchPrecheckRow>(reader =>
                new InboxBatchPrecheckRow(
                    reader.GetInt32(0),
                    AotDataReaderExtensions.ReadNullableString(reader, 1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2)));
            DapperAotParameterRegistry.Register<OutboxMessage>(BindOutboxMessage);
            DapperAotParameterRegistry.Register<AppendOnlyOutboxMessage>(
                BindAppendOnlyOutboxMessage);
            DapperAotStaticCommandPlanRegistry.Register(
                "outbox.insert",
                [
                    "Id",
                    "MessageType",
                    "SchemaVersion",
                    "ContentType",
                    "TenantId",
                    "TraceId",
                    "Payload",
                    "OccurredAtUtc",
                ]);
            DapperAotStaticCommandPlanRegistry.Register(
                "messaging.outbox.append",
                [
                    "Id",
                    "MessageType",
                    "SchemaVersion",
                    "ContentType",
                    "TenantId",
                    "PartitionKey",
                    "CorrelationId",
                    "CausationId",
                    "TraceParent",
                    "Producer",
                    "Payload",
                    "OccurredAtUtc",
                ]);
            _registered = true;
        }
    }

    private static DynamicParameters BindOutboxMessage(OutboxMessage message)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", message.Id);
        parameters.Add("MessageType", message.MessageType);
        parameters.Add("SchemaVersion", message.SchemaVersion);
        parameters.Add("ContentType", message.ContentType);
        parameters.Add("TenantId", message.TenantId);
        parameters.Add("TraceId", message.TraceId);
        parameters.Add("Payload", message.Payload);
        parameters.Add("OccurredAtUtc", message.OccurredAtUtc);
        return parameters;
    }

    private static DynamicParameters BindAppendOnlyOutboxMessage(
        AppendOnlyOutboxMessage message)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", message.Id);
        parameters.Add("MessageType", message.MessageType);
        parameters.Add("SchemaVersion", message.SchemaVersion);
        parameters.Add("ContentType", message.ContentType);
        parameters.Add("TenantId", message.TenantId);
        parameters.Add("PartitionKey", message.PartitionKey);
        parameters.Add("CorrelationId", message.CorrelationId);
        parameters.Add("CausationId", message.CausationId);
        parameters.Add("TraceParent", message.TraceParent);
        parameters.Add("Producer", message.Producer);
        parameters.Add("Payload", message.Payload);
        parameters.Add("OccurredAtUtc", message.OccurredAtUtc);
        return parameters;
    }
}
#endif

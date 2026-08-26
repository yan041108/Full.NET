using System.Data;
using System.Data.Common;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.Dapper.Outbox;
using global::Dapper;
using Microsoft.Data.SqlClient;

namespace Full.NET.Benchmarks.Data;

/// <summary>
/// 为 Outbox insert 命令路径提供一致的 SQL、参数、超时、连接/事务附加与成功回收流程。
/// </summary>
internal static class OutboxInsertCommandBenchmarkHarness
{
    internal const string StatementName = "outbox.insert";
    internal const int CommandTimeoutSeconds = 30;

    internal const string Sql =
        """
        INSERT INTO fn_outbox_message
            (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAtUtc, Attempts)
        VALUES
            (@Id, @MessageType, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAtUtc, 0)
        """;

    internal static readonly string[] ParameterNames =
    [
        "Id",
        "MessageType",
        "SchemaVersion",
        "ContentType",
        "TenantId",
        "TraceId",
        "Payload",
        "OccurredAtUtc",
    ];

    internal static OutboxMessage CreateSampleMessage()
    {
        var payload = new byte[256];
        Random.Shared.NextBytes(payload);
        return new OutboxMessage(
            Guid.CreateVersion7(),
            "benchmark.outbox.write-profile",
            1,
            "application/x-memorypack",
            Guid.CreateVersion7(),
            "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            payload,
            DateTimeOffset.UtcNow);
    }

    internal static DynamicParameters BindDynamicParameters(OutboxMessage message)
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

    internal static void RegisterPlan() =>
        DapperAotStaticCommandPlanRegistry.Register(
            StatementName,
            ParameterNames);

    internal static int FinalizeTypedCommand(
        DbCommand command,
        SqlConnection connection,
        DbTransaction? transaction,
        OutboxInsertTypedCommandPrototype prototype)
    {
        command.Connection = connection;
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        var count = command.Parameters.Count;
        if (!prototype.TryRecycle(command))
        {
            command.Dispose();
        }

        return count;
    }

    internal static int FinalizeCommand(
        DbCommand command,
        SqlConnection connection,
        DbTransaction? transaction,
        DapperAotCommandFactory? factory)
    {
        command.Connection = connection;
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        var count = command.Parameters.Count;
        if (factory?.TryRecycle(command) != true)
        {
            command.Dispose();
        }

        return count;
    }

    internal static int CreateBindDispose(
        SqlConnection connection,
        SqlTransaction? transaction,
        DynamicParameters parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Sql;
        command.CommandType = CommandType.Text;
        foreach (var name in parameters.ParameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = parameters.Get<object>(name) ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        command.Connection = connection;
        command.Transaction = transaction;
        command.CommandTimeout = CommandTimeoutSeconds;
        return command.Parameters.Count;
    }
}

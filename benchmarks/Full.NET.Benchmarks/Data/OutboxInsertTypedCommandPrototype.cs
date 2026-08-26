using System.Data;
using System.Data.Common;
using Full.NET.Data.Dapper.Outbox;
using Microsoft.Data.SqlClient;

namespace Full.NET.Benchmarks.Data;

/// <summary>
/// P4 原型：按 ordinal 从强类型 Outbox DTO 更新参数，不创建 DynamicParameters，不调用 Get&lt;object&gt;。
/// </summary>
internal sealed class OutboxInsertTypedCommandPrototype
{
    private readonly string _sql;
    private DbCommand? _storage;

    public OutboxInsertTypedCommandPrototype(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        _sql = sql;
    }

    public DbCommand GetCommand(
        SqlConnection connection,
        OutboxMessage message,
        CommandType commandType)
    {
        var command = TryReuse() ?? CreateNew(connection, commandType);
        UpdateParametersOrdinal(command, message);
        return command;
    }

    public bool TryRecycle(DbCommand command)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            parameter.Value = DBNull.Value;
        }

        command.Transaction = null;
        command.Connection = null;
        return TryRecycleInterlocked(ref _storage, command);
    }

    private DbCommand CreateNew(SqlConnection connection, CommandType commandType)
    {
        var command = connection.CreateCommand();
        command.CommandText = _sql;
        command.CommandType = commandType;
        AddParameters(command);
        return command;
    }

    private static void AddParameters(DbCommand command)
    {
        AddParameter(command, "Id");
        AddParameter(command, "MessageType");
        AddParameter(command, "SchemaVersion");
        AddParameter(command, "ContentType");
        AddParameter(command, "TenantId");
        AddParameter(command, "TraceId");
        AddParameter(command, "Payload");
        AddParameter(command, "OccurredAtUtc");
    }

    private static void AddParameter(DbCommand command, string name)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static void UpdateParametersOrdinal(DbCommand command, OutboxMessage message)
    {
        command.Parameters[0].Value = message.Id;
        command.Parameters[1].Value = message.MessageType;
        command.Parameters[2].Value = message.SchemaVersion;
        command.Parameters[3].Value = message.ContentType;
        command.Parameters[4].Value = (object?)message.TenantId ?? DBNull.Value;
        command.Parameters[5].Value = (object?)message.TraceId ?? DBNull.Value;
        command.Parameters[6].Value = message.Payload;
        command.Parameters[7].Value = message.OccurredAtUtc;
    }

    private DbCommand? TryReuse()
    {
        while (true)
        {
            var command = _storage;
            if (command is null)
            {
                return null;
            }

            if (Interlocked.CompareExchange(ref _storage, null, command) == command)
            {
                return command;
            }
        }
    }

    private static bool TryRecycleInterlocked(ref DbCommand? storage, DbCommand command)
    {
        var existing = storage;
        if (existing is null
            && Interlocked.CompareExchange(ref storage, command, null) is null)
        {
            return true;
        }

        return false;
    }
}

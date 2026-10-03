using System.Data.Common;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MemoryPack;
using Microsoft.Data.SqlClient;
using MySqlConnector;

const string insertSql = """
    INSERT INTO fn_outbox_message
        (Id, MessageType, SchemaVersion, ContentType, TenantId, TraceId,
         Payload, OccurredAtUtc, Attempts)
    VALUES
        (@Id, @MessageType, 1, @ContentType, NULL, @TraceId,
         @Payload, @OccurredAtUtc, 0)
    """;
const string stateSql = """
    SELECT Id, Attempts,
           CASE WHEN ProcessedAtUtc IS NULL THEN 0 ELSE 1 END AS IsProcessed,
           CASE WHEN DeadLetteredAtUtc IS NULL THEN 0 ELSE 1 END AS IsDeadLettered,
           DeadLetterReasonCode,
           CASE WHEN LockId IS NULL AND LockedUntilUtc IS NULL THEN 1 ELSE 0 END AS IsLeaseReleased,
           CASE WHEN NextAttemptAtUtc IS NULL THEN 1 ELSE 0 END AS IsRetryCleared
    FROM fn_outbox_message
    WHERE Id = @Id
    """;

if (args.Length is < 1 or > 3 || args[0] is not ("enqueue" or "state" or "wait" or "business-wait"))
{
    throw new ArgumentException("Expected enqueue, state <message-id>, wait <message-id>, or business-wait <tenant-id> <unit-id>.");
}

var provider = Environment.GetEnvironmentVariable("Database__Provider")
    ?? throw new InvalidOperationException("Database__Provider is required.");
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__app")
    ?? throw new InvalidOperationException("ConnectionStrings__app is required.");
await using DbConnection connection = provider switch
{
    "SqlServer" => new SqlConnection(connectionString),
    "MySql" => new MySqlConnection(MySqlConnectionStringPolicy.Create(
        connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false)),
    _ => throw new InvalidOperationException($"Unsupported provider '{provider}'."),
};

if (args[0] == "business-wait")
{
    if (args.Length != 3 || !Guid.TryParse(args[1], out var tenantId)
        || !Guid.TryParse(args[2], out var unitId))
    {
        throw new ArgumentException("business-wait requires valid tenant and unit IDs.");
    }

    await connection.OpenAsync();
    var result = await BusinessOutboxProbe.WaitAsync(connection, tenantId, unitId);
    Console.WriteLine("OUTBOX_PROBE " + JsonSerializer.Serialize(result));
}
else if (args[0] == "enqueue")
{
    if (args.Length != 1) throw new ArgumentException("enqueue takes no message ID.");
    var id = Guid.CreateVersion7();
    var serializer = new MemoryPackIntegrationEventSerializer();
    var payload = serializer.Serialize(new TenantChangedIntegrationEvent(
        Guid.CreateVersion7(), "created-app-outbox-probe.example.test"));
    var occurredAtUtc = DateTimeOffset.UtcNow;
    await using var command = connection.CreateCommand();
    command.CommandText = insertSql;
    AddParameter(command, "Id", id);
    AddParameter(command, "MessageType", "fullnet.tenancy.tenant.changed");
    AddParameter(command, "ContentType", serializer.ContentType);
    AddParameter(command, "TraceId", id.ToString("N"));
    AddParameter(command, "Payload", payload);
    AddParameter(command, "OccurredAtUtc", provider == "MySql"
        ? (object)occurredAtUtc.UtcDateTime
        : occurredAtUtc);
    await connection.OpenAsync();
    await command.ExecuteNonQueryAsync();
    Console.WriteLine("OUTBOX_PROBE " + JsonSerializer.Serialize(new { id }));
}
else
{
    if (args.Length != 2 || !Guid.TryParse(args[1], out var id))
    {
        throw new ArgumentException("state and wait require a valid message ID.");
    }

    await connection.OpenAsync();
    var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
    OutboxProbeState state;
    do
    {
        await using var command = connection.CreateCommand();
        command.CommandText = stateSql;
        AddParameter(command, "Id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Outbox message {id:D} was not found.");
        }

        state = new OutboxProbeState
        {
            Id = id,
            Attempts = Convert.ToInt32(reader["Attempts"]),
            IsProcessed = Convert.ToInt64(reader["IsProcessed"]),
            IsDeadLettered = Convert.ToInt64(reader["IsDeadLettered"]),
            DeadLetterReasonCode = reader["DeadLetterReasonCode"] is DBNull
                ? null : Convert.ToString(reader["DeadLetterReasonCode"]),
            IsLeaseReleased = Convert.ToInt64(reader["IsLeaseReleased"]),
            IsRetryCleared = Convert.ToInt64(reader["IsRetryCleared"]),
        };
        if (args[0] == "state" || state.IsProcessed == 1 || state.IsDeadLettered == 1)
        {
            break;
        }

        await Task.Delay(250);
    } while (DateTimeOffset.UtcNow < deadline);
    Console.WriteLine("OUTBOX_PROBE " + JsonSerializer.Serialize(state));
}

static void AddParameter(DbCommand command, string name, object value)
{
    var parameter = command.CreateParameter();
    parameter.ParameterName = name;
    parameter.Value = value;
    command.Parameters.Add(parameter);
}

internal sealed class OutboxProbeState
{
    public Guid Id { get; init; }
    public int Attempts { get; init; }
    public long IsProcessed { get; init; }
    public long IsDeadLettered { get; init; }
    public string? DeadLetterReasonCode { get; init; }
    public long IsLeaseReleased { get; init; }
    public long IsRetryCleared { get; init; }
}

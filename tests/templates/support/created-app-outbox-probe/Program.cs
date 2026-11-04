using System.Data.Common;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

if (args.Length is < 1 or > 3 || args[0] is not ("business-wait" or "schema-upgrade-unaccount" or "schema-upgrade-comment-state"))
{
    throw new ArgumentException("Expected business-wait <tenant-id> <unit-id>, schema-upgrade-unaccount, or schema-upgrade-comment-state.");
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

if (args[0] == "schema-upgrade-unaccount")
{
    if (args.Length != 1) throw new ArgumentException("schema-upgrade-unaccount takes no arguments.");
    await connection.OpenAsync();
    var recovery = await SchemaUpgradeProbe.UnaccountAsync(connection, provider);
    Console.WriteLine("OUTBOX_PROBE " + JsonSerializer.Serialize(new { removed = recovery.Removed, commentRemoved = recovery.CommentRemoved }));
}
else if (args[0] == "schema-upgrade-comment-state")
{
    if (args.Length != 1) throw new ArgumentException("schema-upgrade-comment-state takes no arguments.");
    await connection.OpenAsync();
    var present = await SchemaUpgradeProbe.ReadCommentAsync(connection, provider);
    Console.WriteLine("OUTBOX_PROBE " + JsonSerializer.Serialize(new { present }));
}
else if (args[0] == "business-wait")
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

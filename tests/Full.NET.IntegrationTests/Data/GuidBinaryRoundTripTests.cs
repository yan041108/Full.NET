using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using MySqlConnector;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class GuidBinaryRoundTripTests
{
    private const string CanonicalText = "01890f4e-7c2a-7abc-8def-0123456789ab";
    private const string ExpectedHex = "01890F4E7C2A7ABC8DEF0123456789AB";
    private static readonly Guid FixedGuid = Guid.Parse(CanonicalText);
    private static readonly MySqlContainer Container = new MySqlBuilder("mysql:8.0")
        .WithDatabase("fullnet")
        .WithUsername("fullnet")
        .WithPassword("FullNet_Test!123")
        .Build();

    [ClassInitialize]
    public static Task StartAsync(TestContext _) => Container.StartAsync();

    [ClassCleanup]
    public static async Task CleanupAsync() => await Container.DisposeAsync();

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Dapper_round_trip_preserves_guid_and_hex()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync(
            "CREATE TEMPORARY TABLE guid_round_trip (Id BINARY(16) NOT NULL PRIMARY KEY)");
        await connection.ExecuteAsync(
            "INSERT INTO guid_round_trip (Id) VALUES (@Id)",
            new { Id = FixedGuid });

        var actual = await connection.QuerySingleAsync<Guid>(
            "SELECT Id FROM guid_round_trip");
        var hex = await connection.QuerySingleAsync<string>(
            "SELECT HEX(Id) FROM guid_round_trip");

        Assert.AreEqual(FixedGuid, actual);
        Assert.AreEqual(ExpectedHex, hex);
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_MySql_functions_match_driver_bytes()
    {
        await using var connection = await OpenConnectionAsync();

        var result = await connection.QuerySingleAsync<GuidFunctionRow>(
            """
            SELECT LOWER(BIN_TO_UUID(@Id, 0)) AS CanonicalText,
                   HEX(UUID_TO_BIN(@CanonicalText, 0)) AS FunctionHex,
                   HEX(@Id) AS DriverHex
            """,
            new { Id = FixedGuid, CanonicalText });

        Assert.AreEqual(CanonicalText, result.CanonicalText);
        Assert.AreEqual(ExpectedHex, result.FunctionHex);
        Assert.AreEqual(ExpectedHex, result.DriverHex);
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Primary_and_foreign_keys_join_with_guid_parameters()
    {
        var childId = Guid.Parse("019822d3-0700-7000-8000-000000000201");
        await using var connection = await OpenConnectionAsync();
        // MySQL 禁止临时表参与外键，因此该用例在隔离容器中创建普通表并在结束时回收。
        try
        {
            await connection.ExecuteAsync(
                """
                CREATE TABLE guid_parent (Id BINARY(16) NOT NULL PRIMARY KEY);
                CREATE TABLE guid_child (
                    Id BINARY(16) NOT NULL PRIMARY KEY,
                    ParentId BINARY(16) NOT NULL,
                    CONSTRAINT FK_guid_child_ParentId
                        FOREIGN KEY (ParentId) REFERENCES guid_parent (Id));
                """);
            await connection.ExecuteAsync(
                "INSERT INTO guid_parent (Id) VALUES (@Id)",
                new { Id = FixedGuid });
            await connection.ExecuteAsync(
                "INSERT INTO guid_child (Id, ParentId) VALUES (@Id, @ParentId)",
                new { Id = childId, ParentId = FixedGuid });

            var joinedParentId = await connection.QuerySingleAsync<Guid>(
                """
                SELECT parent.Id
                FROM guid_parent AS parent
                INNER JOIN guid_child AS child ON child.ParentId = parent.Id
                WHERE child.Id = @ChildId
                """,
                new { ChildId = childId });

            Assert.AreEqual(FixedGuid, joinedParentId);
        }
        finally
        {
            await connection.ExecuteAsync(
                "DROP TABLE IF EXISTS guid_child; DROP TABLE IF EXISTS guid_parent;");
        }
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Application_gate_rejects_empty_guid()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync(
            "CREATE TEMPORARY TABLE guid_empty_gate (Id BINARY(16) NOT NULL PRIMARY KEY)");

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            InsertAssignedGuidAsync(connection, Guid.Empty));
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Time_swap_bytes_do_not_match_target_contract()
    {
        await using var connection = await OpenConnectionAsync();

        var swappedHex = await connection.QuerySingleAsync<string>(
            "SELECT HEX(UUID_TO_BIN(@CanonicalText, 1))",
            new { CanonicalText });

        Assert.AreNotEqual(ExpectedHex, swappedHex);
    }

    private static async Task<MySqlConnection> OpenConnectionAsync()
    {
        var connectionString = MySqlConnectionStringPolicy.Create(
            Container.GetConnectionString(),
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false);
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static Task InsertAssignedGuidAsync(MySqlConnection connection, Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("持久化标识必须由应用预先分配。", nameof(id));
        }

        return connection.ExecuteAsync(
            "INSERT INTO guid_empty_gate (Id) VALUES (@Id)",
            new { Id = id });
    }

    private sealed class GuidFunctionRow
    {
        public string CanonicalText { get; init; } = string.Empty;

        public string FunctionHex { get; init; } = string.Empty;

        public string DriverHex { get; init; } = string.Empty;
    }
}

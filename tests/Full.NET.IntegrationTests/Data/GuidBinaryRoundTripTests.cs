using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using System.Text.Json;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class GuidBinaryRoundTripTests
{
    private static readonly IReadOnlyDictionary<string, UuidStorageVector> StorageVectors =
        LoadStorageVectors();
    private static readonly UuidStorageVector FixedVector = StorageVectors["readable-boundaries"];
    private static string _connectionString = null!;

    [ClassInitialize]
    public static async Task StartAsync(TestContext _)
    {
        RegisterDapperBoundary();
        _connectionString = await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Dapper_round_trip_preserves_guid_and_hex()
    {
        await using var connection = await OpenConnectionAsync();
        await connection.ExecuteAsync(
            "CREATE TEMPORARY TABLE guid_round_trip (Id BINARY(16) NOT NULL PRIMARY KEY)");
        await connection.ExecuteAsync(
            "INSERT INTO guid_round_trip (Id) VALUES (@Id)",
            new { Id = FixedVector.Guid });

        var actual = await connection.QuerySingleAsync<Guid>(
            "SELECT Id FROM guid_round_trip");
        var hex = await connection.QuerySingleAsync<string>(
            "SELECT HEX(Id) FROM guid_round_trip");

        Assert.AreEqual(FixedVector.Guid, actual);
        Assert.AreEqual(FixedVector.Hex, hex, ignoreCase: true);
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
            new { Id = FixedVector.Guid, CanonicalText = FixedVector.Uuid });

        Assert.AreEqual(FixedVector.Uuid, result.CanonicalText);
        Assert.AreEqual(FixedVector.Hex, result.FunctionHex, ignoreCase: true);
        Assert.AreEqual(FixedVector.Hex, result.DriverHex, ignoreCase: true);
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Primary_and_foreign_keys_join_with_guid_parameters()
    {
        var childId = StorageVectors["seed-run-sample"].Guid;
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
                new { Id = FixedVector.Guid });
            await connection.ExecuteAsync(
                "INSERT INTO guid_child (Id, ParentId) VALUES (@Id, @ParentId)",
                new { Id = childId, ParentId = FixedVector.Guid });

            var joinedParentId = await connection.QuerySingleAsync<Guid>(
                """
                SELECT parent.Id
                FROM guid_parent AS parent
                INNER JOIN guid_child AS child ON child.ParentId = parent.Id
                WHERE child.Id = @ChildId
                """,
                new { ChildId = childId });

            Assert.AreEqual(FixedVector.Guid, joinedParentId);
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
            connection.ExecuteAsync(
                "INSERT INTO guid_empty_gate (Id) VALUES (@Id)",
                new { Id = Guid.Empty }));

        var persistedCount = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM guid_empty_gate");
        Assert.AreEqual(0, persistedCount);
    }

    [TestMethod]
    public async Task GuidBinaryRoundTrip_Time_swap_bytes_do_not_match_target_contract()
    {
        await using var connection = await OpenConnectionAsync();

        var swappedHex = await connection.QuerySingleAsync<string>(
            "SELECT HEX(UUID_TO_BIN(@CanonicalText, 1))",
            new { CanonicalText = FixedVector.Uuid });

        Assert.AreNotEqual(FixedVector.Hex, swappedHex, ignoreCase: true);
    }

    private static async Task<MySqlConnection> OpenConnectionAsync()
    {
        var connectionString = MySqlConnectionStringPolicy.Create(
            _connectionString,
            MySqlGuidStorageMode.Binary16,
            allowUserVariables: false);
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static void RegisterDapperBoundary()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.MySql.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                    "Server=localhost;Database=fullnet;User ID=fullnet;Password=integration-test",
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    MySqlGuidStorageMode.Binary16.ToString(),
            })
            .Build();
        _ = new ServiceCollection().AddFullNetDapper(configuration, "Testing");
    }

    private static IReadOnlyDictionary<string, UuidStorageVector> LoadStorageVectors()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var contractPath = Path.Combine(
                directory.FullName,
                "contracts",
                "database",
                "uuid-storage-v1.json");
            if (File.Exists(contractPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(contractPath));
                return document.RootElement
                    .GetProperty("vectors")
                    .EnumerateArray()
                    .Select(element =>
                    {
                        var name = element.GetProperty("name").GetString()
                            ?? throw new InvalidDataException("UUID 契约向量缺少 name。");
                        var uuid = element.GetProperty("uuid").GetString()
                            ?? throw new InvalidDataException("UUID 契约向量缺少 uuid。");
                        var hex = element.GetProperty("hex").GetString()
                            ?? throw new InvalidDataException("UUID 契约向量缺少 hex。");
                        return new UuidStorageVector(name, uuid, Guid.Parse(uuid), hex);
                    })
                    .ToDictionary(vector => vector.Name, StringComparer.Ordinal);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("找不到 UUID 存储契约 uuid-storage-v1.json。");
    }

    private sealed class GuidFunctionRow
    {
        public string CanonicalText { get; init; } = string.Empty;

        public string FunctionHex { get; init; } = string.Empty;

        public string DriverHex { get; init; } = string.Empty;
    }

    private sealed record UuidStorageVector(string Name, string Uuid, Guid Guid, string Hex);
}

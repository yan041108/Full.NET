using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Redis;

namespace Full.NET.IntegrationTests;

/// <summary>
/// 整个测试程序集只启动一个 SQL Server 和一个 MySQL 容器，每个测试在共享实例上创建独立数据库。
/// 集成测试的墙钟瓶颈是容器启动而非 SQL 执行；共享容器把「每测一容器」降为「每测一库」，
/// 在保持数据库级隔离的同时消除绝大部分容器 boot 开销。
/// </summary>
[TestClass]
public static class SharedDatabaseFixture
{
    private const string SqlServerImage =
        "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

    private const string MySqlImage = "mysql:8.0";
    private const string RedisImage = "redis:8.6";

    // SQL Server 的 sa 与 MySQL 的 root 均使用该口令；MySQL 应用账户与官方表命名保持一致。
    private const string Password = "FullNet_Test!123";

    private const string MySqlAppUser = "fullnet";

    private static MsSqlContainer? _sqlServer;
    private static MySqlContainer? _mySql;
    private static RedisContainer? _redis;

    private static MsSqlContainer SqlServerContainer =>
        _sqlServer ?? throw new InvalidOperationException(
            "共享 SQL Server 容器尚未初始化。");

    private static MySqlContainer MySqlContainer =>
        _mySql ?? throw new InvalidOperationException(
            "共享 MySQL 容器尚未初始化。");

    private static RedisContainer RedisContainer =>
        _redis ?? throw new InvalidOperationException(
            "共享 Redis 容器尚未初始化。");

    [AssemblyInitialize]
    public static async Task InitializeAsync(TestContext testContext)
    {
        _ = testContext;
        _sqlServer = new MsSqlBuilder(SqlServerImage)
            .WithPassword(Password)
            .Build();
        _mySql = new MySqlBuilder(MySqlImage)
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet")
            .WithUsername(MySqlAppUser)
            .WithPassword(Password)
            .Build();
        _redis = new RedisBuilder(RedisImage)
            .Build();

        // 两个数据库引擎与 Redis 的 boot 相互独立，并行启动进一步缩短程序集初始化时间。
        await Task.WhenAll(
            _sqlServer.StartAsync(),
            _mySql.StartAsync(),
            _redis.StartAsync());
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
            _redis = null;
        }

        if (_mySql is not null)
        {
            await _mySql.DisposeAsync();
            _mySql = null;
        }

        if (_sqlServer is not null)
        {
            await _sqlServer.DisposeAsync();
            _sqlServer = null;
        }
    }

    /// <summary>
    /// 在共享 SQL Server 实例上创建一个隔离数据库，返回指向该库的连接串。
    /// </summary>
    public static async Task<string> CreateSqlServerDatabaseAsync()
    {
        var baseConnectionString = SqlServerContainer.GetConnectionString();
        var databaseName = CreateDatabaseName();
        await using (var admin = new SqlConnection(baseConnectionString))
        {
            await admin.ExecuteAsync($"CREATE DATABASE [{databaseName}];");
        }

        return new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    /// <summary>
    /// 在共享 MySQL 实例上以 root 创建隔离数据库并授权应用账户，返回应用账户连接串。
    /// </summary>
    public static async Task<string> CreateMySqlDatabaseAsync()
    {
        var appConnectionString = MySqlContainer.GetConnectionString();
        var databaseName = CreateDatabaseName();

        // 官方 mysql 镜像只授予应用账户其初始库的权限；建库与授权必须由 root 完成。
        var rootConnectionString = new MySqlConnectionStringBuilder(appConnectionString)
        {
            UserID = "root",
            Password = Password,
            Database = string.Empty,
        }.ConnectionString;
        await using (var root = new MySqlConnection(rootConnectionString))
        {
            await root.ExecuteAsync($"CREATE DATABASE `{databaseName}`;");
            await root.ExecuteAsync(
                $"GRANT ALL PRIVILEGES ON `{databaseName}`.* "
                + $"TO '{MySqlAppUser}'@'%'; FLUSH PRIVILEGES;");
        }

        return new MySqlConnectionStringBuilder(appConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;
    }

    /// <summary>
    /// 返回共享 Redis 容器的连接串，供需要 Backplane/分布式缓存的测试宿主复用。
    /// </summary>
    public static string GetRedisConnectionString() => RedisContainer.GetConnectionString();

    // 库名需短于 MySQL 的 64 字符上限并且是合法标识符；固定前缀 + N 格式 GUID 满足两库要求。
    private static string CreateDatabaseName() =>
        "fullnet_it_" + Guid.NewGuid().ToString("N");
}

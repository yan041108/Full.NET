using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Redis;

namespace Full.NET.IntegrationTests;

/// <summary>
/// 整个测试程序集按需启动并复用 SQL Server、MySQL 和 Redis 容器，
/// 每个测试仍在共享数据库实例上创建独立数据库，避免聚焦运行承担无关容器的启动成本。
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

    internal const string MySqlRootPassword = Password;

    private const string MySqlAppUser = "fullnet";

    private static MsSqlContainer? _sqlServer;
    private static MySqlContainer? _mySql;
    private static RedisContainer? _redis;
    private static readonly SemaphoreSlim SqlServerStartLock = new(1, 1);
    private static readonly SemaphoreSlim MySqlStartLock = new(1, 1);
    private static readonly SemaphoreSlim RedisStartLock = new(1, 1);

    [AssemblyInitialize]
    public static void Initialize(TestContext testContext)
    {
        // 仅保留程序集级清理生命周期；具体依赖由首个消费者异步启动。
        _ = testContext;
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

        await Messaging.KafkaFixture.DisposeAsync();
        await Messaging.CdcDebeziumPipelineFixture.DisposeAsync();
    }

    /// <summary>
    /// 在共享 SQL Server 实例上创建一个隔离数据库，返回指向该库的连接串。
    /// </summary>
    public static async Task<string> CreateSqlServerDatabaseAsync()
    {
        var container = await GetOrStartSqlServerAsync();
        var baseConnectionString = container.GetConnectionString();
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
        var container = await GetOrStartMySqlAsync();
        var appConnectionString = container.GetConnectionString();
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
                + $"TO '{MySqlAppUser}'@'%'; "
                + $"GRANT REPLICATION SLAVE, REPLICATION CLIENT ON *.* "
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
    public static async Task<string> GetRedisConnectionStringAsync()
    {
        var container = await GetOrStartRedisAsync();
        return container.GetConnectionString();
    }

    private static async Task<MsSqlContainer> GetOrStartSqlServerAsync()
    {
        if (_sqlServer is not null)
        {
            return _sqlServer;
        }

        await SqlServerStartLock.WaitAsync();
        try
        {
            if (_sqlServer is not null)
            {
                return _sqlServer;
            }

            var container = new MsSqlBuilder(SqlServerImage)
                .WithPassword(Password)
                .Build();
            try
            {
                await container.StartAsync();
                _sqlServer = container;
                return container;
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }
        finally
        {
            SqlServerStartLock.Release();
        }
    }

    private static async Task<MySqlContainer> GetOrStartMySqlAsync()
    {
        if (_mySql is not null)
        {
            return _mySql;
        }

        await MySqlStartLock.WaitAsync();
        try
        {
            if (_mySql is not null)
            {
                return _mySql;
            }

            var container = new MySqlBuilder(MySqlImage)
                .WithCommand("--log-bin-trust-function-creators=1")
                .WithCommand("--log-bin=mysql-bin")
                .WithCommand("--binlog-format=ROW")
                .WithCommand("--binlog-row-image=FULL")
                .WithCommand("--server-id=1840172600")
                .WithDatabase("fullnet")
                .WithUsername(MySqlAppUser)
                .WithPassword(Password)
                .Build();
            try
            {
                await container.StartAsync();
                _mySql = container;
                return container;
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }
        finally
        {
            MySqlStartLock.Release();
        }
    }

    private static async Task<RedisContainer> GetOrStartRedisAsync()
    {
        if (_redis is not null)
        {
            return _redis;
        }

        await RedisStartLock.WaitAsync();
        try
        {
            if (_redis is not null)
            {
                return _redis;
            }

            var container = new RedisBuilder(RedisImage).Build();
            try
            {
                await container.StartAsync();
                _redis = container;
                return container;
            }
            catch
            {
                await container.DisposeAsync();
                throw;
            }
        }
        finally
        {
            RedisStartLock.Release();
        }
    }

    // 库名需短于 MySQL 的 64 字符上限并且是合法标识符；固定前缀 + N 格式 GUID 满足两库要求。
    private static string CreateDatabaseName() =>
        "fullnet_it_" + Guid.NewGuid().ToString("N");
}

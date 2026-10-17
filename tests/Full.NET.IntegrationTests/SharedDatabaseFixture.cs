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
    private static readonly OwnedTestDatabases Databases = new();
    private static int _closing;
    private static readonly SemaphoreSlim SqlServerStartLock = new(1, 1);
    private static readonly SemaphoreSlim MySqlStartLock = new(1, 1);
    private static readonly SemaphoreSlim RedisStartLock = new(1, 1);

    /// <summary>
    /// CI 默认销毁容器；本地默认复用，避免每次 inner 都重新拉起 SQL Server/MySQL。
    /// 设置 <c>FULLNET_TESTCONTAINERS_REUSE=0</c> 可强制关闭复用。
    /// </summary>
    internal static bool ReuseContainers =>
        !IsCiEnvironment
        && !string.Equals(
            Environment.GetEnvironmentVariable("FULLNET_TESTCONTAINERS_REUSE"),
            "0",
            StringComparison.Ordinal);

    internal static bool IsCiEnvironment =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable("GITHUB_ACTIONS"),
            "true",
            StringComparison.OrdinalIgnoreCase);

    [AssemblyInitialize]
    public static void Initialize(TestContext testContext)
    {
        // 仅保留程序集级清理生命周期；具体依赖由首个消费者异步启动。
        _ = testContext;
        if (ReuseContainers)
        {
            // Testcontainers 默认关闭 reuse；必须在 Build() 前打开，本地 inner 才能接回已有容器。
            Environment.SetEnvironmentVariable("TESTCONTAINERS_REUSE_ENABLE", "true");
        }
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        // 先封堵晚启动；容器移除再持启动锁等待已经开始的启动动作。
        Volatile.Write(ref _closing, 1);
        var failures = new List<Exception>();
        async Task AttemptAsync(Func<Task> cleanup)
        {
            try { await cleanup(); }
            catch (Exception error) { failures.Add(error); }
        }

        // 复用容器仍须释放本次运行的临时库；schema 模板与其他进程的库不在登记簿中。
        if (ReuseContainers)
        {
            await AttemptAsync(Databases.CleanupAsync);
        }
        else
        {
            // 私有容器成功移除即释放其中全部数据库，避免逐库删除后再重复销毁容器。
            await AttemptAsync(() => Databases.RetireAsync(
                () => WithStartLockAsync(MySqlStartLock, async () =>
                {
                    if (_mySql is not null) { await _mySql.DisposeAsync(); _mySql = null; }
                }),
                () => WithStartLockAsync(SqlServerStartLock, async () =>
                {
                    if (_sqlServer is not null) { await _sqlServer.DisposeAsync(); _sqlServer = null; }
                })));
            await AttemptAsync(() => WithStartLockAsync(RedisStartLock, async () =>
            {
                if (_redis is not null) { await _redis.DisposeAsync(); _redis = null; }
            }));
        }

        await AttemptAsync(async () => await Messaging.KafkaFixture.DisposeAsync());
        await AttemptAsync(async () => await Messaging.CdcDebeziumPipelineFixture.DisposeAsync());
        if (failures.Count > 0) throw new AggregateException("测试资源清理失败。", failures);
    }

    private static async Task WithStartLockAsync(SemaphoreSlim gate, Func<Task> cleanup)
    {
        await gate.WaitAsync();
        try { await cleanup(); }
        finally { gate.Release(); }
    }

    private static void EnsureOpen()
    {
        if (Volatile.Read(ref _closing) != 0)
            throw new InvalidOperationException("测试资源已进入清理，不能重新启动容器。");
    }

    /// <summary>
    /// 在共享 SQL Server 实例上创建一个隔离数据库，返回指向该库的连接串。
    /// </summary>
    public static Task<string> CreateSqlServerDatabaseAsync() => CreateSqlServerDatabaseAsync(Databases);

    internal static async Task<string> CreateSqlServerDatabaseAsync(OwnedTestDatabases owner)
    {
        var container = await GetOrStartSqlServerAsync();
        var baseConnectionString = container.GetConnectionString();
        var databaseName = await owner.CreateAsync(async name =>
        {
            await using var admin = new SqlConnection(baseConnectionString);
            await admin.ExecuteAsync($"CREATE DATABASE [{name}];");
        }, name => DropSqlServerDatabaseAsync(baseConnectionString, name));

        return new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName,
        }.ConnectionString;
    }

    /// <summary>
    /// 在共享 MySQL 实例上以 root 创建隔离数据库并授权应用账户，返回应用账户连接串。
    /// </summary>
    public static Task<string> CreateMySqlDatabaseAsync() => CreateMySqlDatabaseAsync(Databases);

    internal static async Task<string> CreateMySqlDatabaseAsync(OwnedTestDatabases owner)
    {
        var container = await GetOrStartMySqlAsync();
        var appConnectionString = container.GetConnectionString();

        // 官方 mysql 镜像只授予应用账户其初始库的权限；建库与授权必须由 root 完成。
        var rootConnectionString = new MySqlConnectionStringBuilder(appConnectionString)
        {
            UserID = "root",
            Password = Password,
            Database = string.Empty,
        }.ConnectionString;
        var databaseName = await owner.CreateAsync(async name =>
        {
            await using var root = new MySqlConnection(rootConnectionString);
            await root.ExecuteAsync($"CREATE DATABASE `{name}`;");
        }, name => DropMySqlDatabaseAsync(rootConnectionString, appConnectionString, name), async name =>
        {
            await using var root = new MySqlConnection(rootConnectionString);
            await root.ExecuteAsync(
                $"GRANT ALL PRIVILEGES ON `{name}`.* "
                + $"TO '{MySqlAppUser}'@'%'; "
                + $"GRANT REPLICATION SLAVE, REPLICATION CLIENT ON *.* "
                + $"TO '{MySqlAppUser}'@'%'; FLUSH PRIVILEGES;");
        });

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
        EnsureOpen();
        if (_sqlServer is not null)
        {
            return _sqlServer;
        }

        await SqlServerStartLock.WaitAsync();
        try
        {
            EnsureOpen();
            if (_sqlServer is not null)
            {
                return _sqlServer;
            }

            var builder = new MsSqlBuilder(SqlServerImage).WithPassword(Password);
            if (ReuseContainers)
            {
                // 复用同一容器实例，测试库名仍按 GUID 隔离，不共享可变业务数据。
                builder = builder.WithReuse(true).WithName("fullnet-it-mssql");
            }

            var container = builder.Build();
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
        EnsureOpen();
        if (_mySql is not null)
        {
            return _mySql;
        }

        await MySqlStartLock.WaitAsync();
        try
        {
            EnsureOpen();
            if (_mySql is not null)
            {
                return _mySql;
            }

            var builder = new MySqlBuilder(MySqlImage)
                .WithCommand("--log-bin-trust-function-creators=1")
                .WithCommand("--log-bin=mysql-bin")
                .WithCommand("--binlog-format=ROW")
                .WithCommand("--binlog-row-image=FULL")
                .WithCommand("--server-id=1840172600")
                .WithDatabase("fullnet")
                .WithUsername(MySqlAppUser)
                .WithPassword(Password);
            if (ReuseContainers)
            {
                builder = builder.WithReuse(true).WithName("fullnet-it-mysql");
            }

            var container = builder.Build();
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
        EnsureOpen();
        if (_redis is not null)
        {
            return _redis;
        }

        await RedisStartLock.WaitAsync();
        try
        {
            EnsureOpen();
            if (_redis is not null)
            {
                return _redis;
            }

            var builder = new RedisBuilder(RedisImage);
            if (ReuseContainers)
            {
                builder = builder.WithReuse(true).WithName("fullnet-it-redis");
            }

            var container = builder.Build();
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

    /// <summary>
    /// 返回指向 SQL Server master 的连接串，供模板克隆在库级执行 BACKUP/RESTORE。
    /// </summary>
    internal static async Task<string> GetSqlServerMasterConnectionStringAsync()
    {
        var container = await GetOrStartSqlServerAsync();
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString())
        {
            InitialCatalog = "master",
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// 返回 MySQL root 连接串，供模板克隆建库、授权和跨库 COPY。
    /// </summary>
    internal static async Task<string> GetMySqlRootConnectionStringAsync()
    {
        var container = await GetOrStartMySqlAsync();
        var builder = new MySqlConnectionStringBuilder(container.GetConnectionString())
        {
            UserID = "root",
            Password = Password,
            Database = string.Empty,
        };
        return builder.ConnectionString;
    }

    internal static string GetSqlServerDatabaseName(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString).InitialCatalog;

    internal static string GetMySqlDatabaseName(string connectionString) =>
        new MySqlConnectionStringBuilder(connectionString).Database;

    internal static string QuoteSqlServerIdent(string name) =>
        "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    internal static string QuoteMySqlIdent(string name) =>
        "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    internal static string MySqlApplicationUserName => MySqlAppUser;

    private static async Task DropSqlServerDatabaseAsync(string serverConnectionString, string name)
    {
        var target = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = name }.ConnectionString;
        using (var pool = new SqlConnection(target)) SqlConnection.ClearPool(pool);
        var master = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "master" }.ConnectionString;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var admin = new SqlConnection(master);
        await admin.OpenAsync(deadline.Token);
        var quoted = QuoteSqlServerIdent(name);
        await admin.ExecuteAsync(new CommandDefinition($"""
            IF DB_ID(@Name) IS NOT NULL
            BEGIN
                ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE {quoted};
            END;
            """, new { Name = name }, commandTimeout: 60, cancellationToken: deadline.Token));
    }

    private static async Task DropMySqlDatabaseAsync(string rootConnectionString, string appConnectionString, string name)
    {
        var target = new MySqlConnectionStringBuilder(appConnectionString) { Database = name }.ConnectionString;
        using (var pool = new MySqlConnection(target)) MySqlConnection.ClearPool(pool);
        using (var pool = new MySqlConnection(new MySqlConnectionStringBuilder(rootConnectionString) { Database = name }.ConnectionString))
            MySqlConnection.ClearPool(pool);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var root = new MySqlConnection(rootConnectionString);
        await root.OpenAsync(deadline.Token);
        await root.ExecuteAsync(new CommandDefinition($"DROP DATABASE IF EXISTS {QuoteMySqlIdent(name)};",
            commandTimeout: 60, cancellationToken: deadline.Token));
        // MySQL 删除库不会删除库级授权；仅撤销该库的授权，不触碰应用账户的全局复制权限。
        var grant = await root.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM mysql.db WHERE User = @User AND Host = '%' AND Db = @Name;",
            new { User = MySqlAppUser, Name = name }, commandTimeout: 60, cancellationToken: deadline.Token));
        if (grant > 0)
            await root.ExecuteAsync(new CommandDefinition(
                $"REVOKE ALL PRIVILEGES ON {QuoteMySqlIdent(name)}.* FROM '{MySqlAppUser}'@'%';",
                commandTimeout: 60, cancellationToken: deadline.Token));
    }
}

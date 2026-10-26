using Full.NET.IntegrationTests.Infrastructure;
using Dapper;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
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
    private static NativeContainerResourceProfile.Lease? _sqlServerLease;
    private static MySqlContainer? _mySql;
    private static NativeContainerResourceProfile.Lease? _mySqlLease;
    private static RedisContainer? _redis;
    private static NativeContainerResourceProfile.Lease? _redisLease;
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

    /// <summary>
    /// 显式有界验收只创建本批容器；启用时必须同时关闭复用，避免改变共享实例。
    /// </summary>
    internal static bool BoundedContainers =>
        string.Equals(
            Environment.GetEnvironmentVariable("FULLNET_TESTCONTAINERS_BOUNDED"),
            "1",
            StringComparison.Ordinal);

    private static string? _boundedSession;

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
        if (BoundedContainers && ReuseContainers)
        {
            throw new InvalidOperationException(
                "有界容器验收要求 FULLNET_TESTCONTAINERS_REUSE=0，不能复用共享实例。");
        }

        if (BoundedContainers)
        {
            // 外层有界 Ryuk 必须先完成 ACK；批次标签只用于回收本批资源，不接管共享实例。
            var session = Environment.GetEnvironmentVariable("FULLNET_BOUNDED_RESOURCE_SESSION");
            if (!Guid.TryParseExact(session, "D", out var sessionId) || sessionId == Guid.Empty
                || session != sessionId.ToString("D")
                || Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED") != "true"
                || Environment.GetEnvironmentVariable("FULLNET_NATIVE_RESOURCE_PROFILE") is not null)
            {
                throw new InvalidOperationException("有界验收要求外层独享 Ryuk 会话，且不能混用 Native 资源配置。");
            }

            _boundedSession = session;
        }

        _ = NativeContainerResourceProfile.Current;
        if (ReuseContainers)
        {
            // Testcontainers 默认关闭 reuse；必须在 Build() 前打开，本地 inner 才能接回已有容器。
            Environment.SetEnvironmentVariable("TESTCONTAINERS_REUSE_ENABLE", "true");
        }
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (!ReuseContainers)
        {
            if (_redis is not null)
            {
                if (_redisLease is { } lease) await lease.DisposeContainerAsync(_redis);
                else await _redis.DisposeAsync();
                _redisLease = null;
                _redis = null;
            }

            if (_mySql is not null)
            {
                if (_mySqlLease is { } lease) await lease.DisposeContainerAsync(_mySql);
                else await _mySql.DisposeAsync();
                _mySqlLease = null;
                _mySql = null;
            }

            if (_sqlServer is not null)
            {
                if (_sqlServerLease is { } lease) await lease.DisposeContainerAsync(_sqlServer);
                else await _sqlServer.DisposeAsync();
                _sqlServerLease = null;
                _sqlServer = null;
            }
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

    internal static void ApplyBoundedOwnership(IDictionary<string, string> labels)
    {
        if (_boundedSession is null) throw new InvalidOperationException("有界资源会话尚未初始化。");
        labels["org.testcontainers.resource-reaper-session"] = _boundedSession;
        labels["fullnet.bounded-owner"] = _boundedSession;
    }

    /// <summary>新建消息测试容器始终设置硬限额；独享验收另附外层会话，不接管既有容器。</summary>
    internal static void ApplyMessagingContainerResources(CreateContainerParameters parameters, string role, int cpuCount = 1)
    {
        if (cpuCount is < 1 or > 2 || role != "elasticsearch" && cpuCount != 1)
            throw new ArgumentOutOfRangeException(nameof(cpuCount), "只有 ES 容量档允许显式一核或两核。");
        var budget = role switch
        {
            "kafka" => (Memory: 2L << 30, Cpu: 1_000_000_000L),
            "connect" => (Memory: 2L << 30, Cpu: 1_000_000_000L),
            "elasticsearch" => (Memory: 2L << 30, Cpu: cpuCount * 1_000_000_000L),
            "collector" => (Memory: 512L << 20, Cpu: 500_000_000L),
            _ => throw new ArgumentException("未知消息测试资源角色。", nameof(role)),
        };
        var limits = parameters.HostConfig ??= new();
        limits.Memory = budget.Memory;
        limits.MemorySwap = budget.Memory;
        limits.NanoCPUs = budget.Cpu;
        if (BoundedContainers) ApplyBoundedOwnership(parameters.Labels);
        if (role == "elasticsearch") parameters.Labels["fullnet.logging.es-cpus"] = cpuCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

            var builder = new MsSqlBuilder(SqlServerImage).WithPassword(Password);
            if (BoundedContainers)
            {
                // 限额随创建请求提交，不能等容器启动后再补限额；依赖必须已由外层准备。
                builder = builder.WithImagePullPolicy(PullPolicy.Never)
                    // CU14 不识别 cgroup v2 内存预算；保留容器硬上限与 SQL 内部预算之间的余量。
                    .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "2048")
                    .WithCreateParameterModifier(parameters =>
                    {
                        var limits = parameters.HostConfig ??= new();
                        limits.Memory = 3L * 1024 * 1024 * 1024;
                        limits.MemorySwap = limits.Memory;
                        limits.NanoCPUs = 2_000_000_000;
                        ApplyBoundedOwnership(parameters.Labels);
                    });
            }

            if (ReuseContainers)
            {
                // 复用同一容器实例，测试库名仍按 GUID 隔离，不共享可变业务数据。
                builder = builder.WithReuse(true).WithName("fullnet-it-mssql");
            }

            var lease = NativeContainerResourceProfile.Current?.Acquire("sqlserver");
            MsSqlContainer container;
            try
            {
                if (lease is not null) builder = builder.WithCreateParameterModifier(lease.Apply);
                container = builder.Build();
            }
            catch
            {
                lease?.Dispose();
                throw;
            }
            try
            {
                await container.StartAsync();
                if (lease is not null) await lease.CaptureAsync(container.Id);
                _sqlServerLease = lease;
                _sqlServer = container;
                return container;
            }
            catch
            {
                if (lease is not null) await lease.DisposeContainerAsync(container);
                else await container.DisposeAsync();
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

            var builder = new MySqlBuilder(MySqlImage)
                .WithCommand("--log-bin-trust-function-creators=1")
                .WithCommand("--log-bin=mysql-bin")
                .WithCommand("--binlog-format=ROW")
                .WithCommand("--binlog-row-image=FULL")
                .WithCommand("--server-id=1840172600")
                .WithDatabase("fullnet")
                .WithUsername(MySqlAppUser)
                .WithPassword(Password);
            if (BoundedContainers)
            {
                // 同时限制内存与交换总量，防止双库并行验收把压力转移到宿主交换空间。
                builder = builder.WithImagePullPolicy(PullPolicy.Never)
                    .WithCreateParameterModifier(parameters =>
                    {
                        var limits = parameters.HostConfig ??= new();
                        limits.Memory = 1536L * 1024 * 1024;
                        limits.MemorySwap = limits.Memory;
                        limits.NanoCPUs = 1_000_000_000;
                        ApplyBoundedOwnership(parameters.Labels);
                    });
            }

            if (ReuseContainers)
            {
                builder = builder.WithReuse(true).WithName("fullnet-it-mysql");
            }

            var lease = NativeContainerResourceProfile.Current?.Acquire("mysql");
            MySqlContainer container;
            try
            {
                if (lease is not null) builder = builder.WithCreateParameterModifier(lease.Apply);
                container = builder.Build();
            }
            catch
            {
                lease?.Dispose();
                throw;
            }
            try
            {
                await container.StartAsync();
                if (lease is not null) await lease.CaptureAsync(container.Id);
                _mySqlLease = lease;
                _mySql = container;
                return container;
            }
            catch
            {
                if (lease is not null) await lease.DisposeContainerAsync(container);
                else await container.DisposeAsync();
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

            var builder = new RedisBuilder(RedisImage);
            if (BoundedContainers)
            {
                // 这是测试容器预算，不修改缓存服务或业务的容量配置。
                builder = builder.WithImagePullPolicy(PullPolicy.Never)
                    .WithCreateParameterModifier(parameters =>
                    {
                        var limits = parameters.HostConfig ??= new();
                        limits.Memory = 256L * 1024 * 1024;
                        limits.MemorySwap = limits.Memory;
                        limits.NanoCPUs = 500_000_000;
                        ApplyBoundedOwnership(parameters.Labels);
                    });
            }

            if (ReuseContainers)
            {
                builder = builder.WithReuse(true).WithName("fullnet-it-redis");
            }

            var lease = NativeContainerResourceProfile.Current?.Acquire("redis");
            RedisContainer container;
            try
            {
                if (lease is not null) builder = builder.WithCreateParameterModifier(lease.Apply);
                container = builder.Build();
            }
            catch
            {
                lease?.Dispose();
                throw;
            }
            try
            {
                await container.StartAsync();
                if (lease is not null) await lease.CaptureAsync(container.Id);
                _redisLease = lease;
                _redis = container;
                return container;
            }
            catch
            {
                if (lease is not null) await lease.DisposeContainerAsync(container);
                else await container.DisposeAsync();
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

    // 库名需短于 MySQL 的 64 字符上限并且是合法标识符；固定前缀 + N 格式 GUID 满足两库要求。
    private static string CreateDatabaseName() =>
        "fullnet_it_" + Guid.NewGuid().ToString("N");
}

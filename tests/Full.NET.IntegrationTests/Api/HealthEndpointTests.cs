using System.Net;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Observability;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Realtime.SignalR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Api;

[TestClass]
public sealed class HealthEndpointTests
{
    [TestMethod]
    public void MapFullNetHealthEndpoints_requires_ready_and_startup_checks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHealthChecks();
        var app = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => app.MapFullNetHealthEndpoints());
        StringAssert.Contains(exception.Message, "ready");
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task HealthEndpoints_ready_and_startup_return_ok_after_migrations(
        DatabaseProvider provider)
    {
        var connectionString = await CreateDatabaseAsync(provider);
        await MigrateAsync(provider, connectionString);
        using var factory = HealthEndpointApiFactory.Create(
            provider,
            connectionString);
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var startup = await client.GetAsync("/health/startup");
        using var live = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, startup.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
    }

    [TestMethod]
    public async Task HealthEndpoints_ready_returns_service_unavailable_when_database_is_unreachable()
    {
        using var factory = HealthEndpointApiFactory.Create(
            DatabaseProvider.SqlServer,
            "Server=127.0.0.1,1;Database=fullnet_health_missing;User Id=sa;Password=FullNet_Test!123;TrustServerCertificate=True;Connect Timeout=1");
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var startup = await client.GetAsync("/health/startup");
        using var live = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, startup.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task HealthEndpoints_startup_returns_service_unavailable_when_schema_contract_is_missing(
        DatabaseProvider provider)
    {
        var connectionString = await CreateDatabaseAsync(provider);
        var mySqlMode = provider == DatabaseProvider.MySql
            ? MySqlGuidStorageMode.LegacyChar36.ToString()
            : MySqlGuidStorageMode.Binary16.ToString();
        using var factory = HealthEndpointApiFactory.Create(
            provider,
            connectionString,
            mySqlGuidStorageMode: mySqlMode);
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        using var startup = await client.GetAsync("/health/startup");
        using var live = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, startup.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
    }

    [TestMethod]
    public async Task HealthEndpoints_ready_returns_service_unavailable_when_redis_is_configured_but_unreachable()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await MigrateAsync(DatabaseProvider.SqlServer, connectionString);
        await using var app = await StartMinimalHealthHostAsync(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.SqlServer.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                MySqlGuidStorageMode.Binary16.ToString(),
            [$"{CacheOptions.SectionName}:RedisConnectionString"] =
                "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500",
        });
        var cacheOptions = app.Services
            .GetRequiredService<IOptions<CacheOptions>>()
            .Value;
        Assert.AreEqual(
            "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500",
            cacheOptions.RedisConnectionString);
        var registrations = app.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations
            .Where(registration => registration.Tags.Contains("ready"))
            .Select(registration => registration.Name)
            .ToArray();
        CollectionAssert.Contains(registrations, "database-connectivity");
        CollectionAssert.Contains(registrations, "distributed-cache");
        using var client = app.GetTestClient();

        using var firstReady = await client.GetAsync("/health/ready");
        using var ready = await client.GetAsync("/health/ready");
        using var startup = await client.GetAsync("/health/startup");
        using var live = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, firstReady.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, startup.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
    }

    [TestMethod]
    public async Task HealthEndpoints_ready_returns_service_unavailable_when_realtime_backplane_is_unreachable()
    {
        var connectionString = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        await MigrateAsync(DatabaseProvider.SqlServer, connectionString);
        await using var app = await StartMinimalHealthHostAsync(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.SqlServer.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                MySqlGuidStorageMode.Binary16.ToString(),
            [$"{RealtimeOptions.SectionName}:RedisBackplaneConnectionString"] =
                "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500",
        });
        var registrations = app.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations
            .Where(registration => registration.Tags.Contains("ready"))
            .Select(registration => registration.Name)
            .ToArray();
        CollectionAssert.Contains(registrations, "database-connectivity");
        CollectionAssert.Contains(registrations, "realtime-backplane");
        CollectionAssert.DoesNotContain(registrations, "distributed-cache");
        using var client = app.GetTestClient();

        using var firstReady = await client.GetAsync("/health/ready");
        using var ready = await client.GetAsync("/health/ready");
        using var startup = await client.GetAsync("/health/startup");
        using var live = await client.GetAsync("/health/live");

        Assert.AreEqual(HttpStatusCode.OK, firstReady.StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, startup.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, live.StatusCode);
    }

    private static async Task<WebApplication> StartMinimalHealthHostAsync(
        Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddFullNetServiceDefaults();
        builder.Services.AddFullNetDapper(builder.Configuration, "Testing");
        builder.Services.AddFullNetCaching(builder.Configuration, "Testing");
        builder.Services.AddFullNetRealtimeSignalR(builder.Configuration, "Testing");

        var app = builder.Build();
        app.MapFullNetHealthEndpoints();
        await app.StartAsync();
        return app;
    }

    private static Task<string> CreateDatabaseAsync(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
        DatabaseProvider.MySql => SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    private static Task MigrateAsync(
        DatabaseProvider provider,
        string connectionString) => new DbUpMigrationRunner(
        Options.Create(new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        }),
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = "test-health-endpoints",
        }),
        MigrationContractOptionFactory.NamingOptions()).MigrateAsync();

    private sealed class HealthEndpointApiFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _settings;

        private HealthEndpointApiFactory(Dictionary<string, string?> settings)
        {
            _settings = settings;
        }

        public static HealthEndpointApiFactory Create(
            DatabaseProvider provider,
            string connectionString,
            string mySqlGuidStorageMode = nameof(MySqlGuidStorageMode.Binary16),
            string? redisConnectionString = null)
        {
            var settings = new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] = mySqlGuidStorageMode,
                ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
                ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
                ["Identity:AllowedOrigins:0"] = "http://localhost",
                ["Tenancy:HostDomains:0"] = "localhost",
                ["Files:Local:RootPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "fullnet-files-health",
                    Guid.NewGuid().ToString("N")),
            };
            if (!string.IsNullOrWhiteSpace(redisConnectionString))
            {
                settings[$"{Full.NET.Caching.Fusion.CacheOptions.SectionName}:RedisConnectionString"] =
                    redisConnectionString;
                settings[$"{RealtimeOptions.SectionName}:AllowSharedRedisInDevelopment"] = "true";
                settings["ConnectionStrings:redis"] = redisConnectionString;
            }

            return new HealthEndpointApiFactory(settings);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(Path.Combine(
                FindRepositoryRoot(),
                "src",
                "Hosts",
                "Full.NET.Host.Api"));
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(_settings));
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the Full.NET repository root.");
        }
    }
}

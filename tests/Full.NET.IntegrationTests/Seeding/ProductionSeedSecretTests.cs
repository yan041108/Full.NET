using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Caching.Fusion;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Tenancy;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Serialization.MessagePack;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Seeding;

/// <summary>
/// Production Baseline 缺少 Bootstrap Secret 时必须在写入业务数据前失败。
/// </summary>
[TestClass]
public sealed class ProductionSeedSecretTests
{
    [TestMethod]
    public async Task SqlServer_production_baseline_rejects_missing_bootstrap_secret()
    {
        await VerifyMissingSecretAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_production_baseline_rejects_missing_bootstrap_secret()
    {
        await VerifyMissingSecretAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyMissingSecretAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var options = new DatabaseOptions
        {
            Provider = databaseProvider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };

        var migration = new DbUpMigrationRunner(
            Options.Create(options),
            NullLoggerFactory.Instance,
            Options.Create(new UuidBinaryContractOptions
            {
                MaintenanceMode = true,
                BackupVerified = true,
                LegacyWritersStopped = true,
                DestructiveDdlApprovalId = "test-production-seed-secret-009",
            }),
            MigrationContractOptionFactory.NamingOptions());
        Assert.IsTrue((await migration.MigrateAsync()).Successful);

        await using var services = BuildProductionServicesWithoutPassword(options);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISeedOrchestrator>()
            .RunAsync(SeedProfile.Baseline);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(
            SeedContributionErrorCodes.BootstrapSecretMissing,
            result.Error!.Code);
        Assert.AreEqual(
            0L,
            await CountAsync(
                options,
                "fn_identity_user",
                "ScopeKey = 'host' AND Username = @Username",
                new { Username = "seed-administrator" }));
        Assert.AreEqual(1L, await CountAsync(options, "fn_seed_run"));
        await using var connection = CreateConnection(options);
        var runStatus = await connection.QuerySingleAsync<string>(
            "SELECT Status FROM fn_seed_run");
        Assert.AreEqual(SeedExecutionStatuses.Failed, runStatus);
        var itemError = await connection.QuerySingleAsync<string>(
            """
            SELECT ErrorCode
            FROM fn_seed_run_item
            WHERE Contributor = 'identity.host_administrator'
            """);
        Assert.AreEqual(SeedContributionErrorCodes.BootstrapSecretMissing, itemError);
    }

    private static ServiceProvider BuildProductionServicesWithoutPassword(
        DatabaseOptions options)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = options.Provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = options.ConnectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    options.MySqlGuidStorageMode.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                [$"{SeedOptions.SectionName}:DefaultLocale"] = "zh-CN",
                ["Identity:EnableTokenEndpoints"] = "false",
                ["Identity:Bootstrap:Username"] = "seed-administrator",
                ["Identity:Bootstrap:Password"] = "",
                ["Identity:Bootstrap:DisplayName"] = "种子管理员",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new ProductionHostEnvironment());
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddFullNetModularity();
        services.AddFullNetDapper(configuration, "Production");
        services.AddFullNetMessagePack();
        services.AddFullNetCaching(configuration, "Production");
        services.AddFullNetSeeding(configuration);
        services.AddFullNetModule<IdentityModule>(configuration);
        services.AddFullNetModule<TenancyModule>(configuration);
        services.AddFullNetModule<OrganizationModule>(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static async Task<long> CountAsync(
        DatabaseOptions options,
        string table,
        string? predicate = null,
        object? parameters = null)
    {
        await using var connection = CreateConnection(options);
        var sql = $"SELECT COUNT(*) FROM {table}";
        if (!string.IsNullOrWhiteSpace(predicate))
        {
            sql += $" WHERE {predicate}";
        }

        return await connection.QuerySingleAsync<long>(sql, parameters);
    }

    private static DbConnection CreateConnection(DatabaseOptions options) => options.Provider switch
    {
        DatabaseProvider.SqlServer => new SqlConnection(options.ConnectionString),
        DatabaseProvider.MySql => new MySqlConnection(
            MySqlConnectionStringPolicy.Create(
                options.ConnectionString,
                MySqlGuidStorageMode.Binary16,
                allowUserVariables: false)),
        _ => throw new ArgumentOutOfRangeException(nameof(options.Provider)),
    };

    private sealed class ProductionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Full.NET.IntegrationTests.Seeding";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Full.NET.Abstractions.Results.Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException("Seed secret fixture does not map API results.");

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException("Seed secret fixture does not map API exceptions.");
    }
}

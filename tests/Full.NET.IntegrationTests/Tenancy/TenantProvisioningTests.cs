using System.Data.Common;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
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
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MemoryPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Tenancy;

[TestClass]
public sealed class TenantProvisioningTests
{
    [TestMethod]
    public async Task SqlServer_provisioning_is_atomic_without_cache_outbox()
    {
        await VerifyProviderAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_is_atomic_without_cache_outbox()
    {
        await VerifyProviderAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyProviderAsync(
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
        var migrationRunner = new DbUpMigrationRunner(
            Options.Create(options),
            NullLoggerFactory.Instance,
            ContractOptions(),
            MigrationContractOptionFactory.NamingOptions());
        await migrationRunner.MigrateAsync();

        var configuration = CreateConfiguration(options);
        await using (var services = BuildServices(configuration))
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var provisioning = scope.ServiceProvider
                .GetRequiredService<ITenantProvisioningService>();

            var invalid = await provisioning.ProvisionAsync(
                new ProvisionTenantRequest(null!, null!, null!));
            Assert.IsFalse(invalid.IsSuccess);
            Assert.IsNotNull(invalid.Error);
            Assert.AreEqual("validation.failed", invalid.Error.Code);
            Assert.IsNotNull(invalid.Error.ValidationErrors);
            Assert.AreEqual(3, invalid.Error.ValidationErrors.Count);

            var result = await provisioning.ProvisionAsync(
                new ProvisionTenantRequest(
                    " ACME ",
                    " Acme Corporation ",
                    " ACME.LOCALHOST "));

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual("acme", result.Value.Identifier);
            Assert.AreEqual("Acme Corporation", result.Value.Name);
            Assert.AreEqual("acme.localhost", result.Value.Domain);
            Assert.AreEqual("zh-CN", result.Value.DefaultLocale);

            var duplicate = await provisioning.ProvisionAsync(
                new ProvisionTenantRequest(
                    "acme",
                    "Another Acme",
                    "other.localhost"));

            Assert.IsFalse(duplicate.IsSuccess);
            Assert.IsNotNull(duplicate.Error);
            Assert.AreEqual("tenancy.identifier_exists", duplicate.Error.Code);

            // Expand/Cutover：开通不再写入缓存专用 Outbox；兼容 Handler 仅排空存量消息。
            Assert.AreEqual(
                0L,
                await CountAsync(
                    databaseProvider,
                    connectionString,
                    "fn_outbox_message",
                    "MessageType = 'fullnet.tenancy.tenant.provisioned'"));
        }

        Assert.AreEqual(
            1L,
            await CountAsync(databaseProvider, connectionString, "fn_tenancy_tenant"));
        Assert.AreEqual(
            0L,
            await CountAsync(databaseProvider, connectionString, "fn_outbox_message"));
    }

    private static IConfiguration CreateConfiguration(DatabaseOptions options) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = options.Provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = options.ConnectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    options.MySqlGuidStorageMode.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            })
            .Build();

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        // 该夹具只验证非 HTTP 事务切片；显式替身用于满足完整模块的授权结果映射依赖。
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddFullNetModularity();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMemoryPack();
        services.AddFullNetCaching(configuration, "Test");
        services.AddSingleton<
            ITenantOrganizationUnitDirectory,
            EmptyTenantOrganizationUnitDirectory>();
        services.AddSingleton<
            IIdentityOrganizationUnitDirectory,
            EmptyIdentityOrganizationUnitDirectory>();
        services.AddSingleton<
            IIdentityOrganizationUnitProjectionSource,
            EmptyIdentityOrganizationUnitProjectionSource>();
        services.AddFullNetModule<IdentityModule>(configuration);
        services.AddFullNetModule<TenancyModule>(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private sealed class EmptyTenantOrganizationUnitDirectory
        : ITenantOrganizationUnitDirectory
    {
        public Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class EmptyIdentityOrganizationUnitDirectory
        : IIdentityOrganizationUnitDirectory
    {
        public Task<IdentityOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class EmptyIdentityOrganizationUnitProjectionSource
        : IIdentityOrganizationUnitProjectionSource
    {
        public Task<Result<IdentityOrganizationUnitProjectionPage>> ListAsync(
            Guid tenantId,
            Guid? afterUnitId,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IdentityOrganizationUnitProjectionPage>.Success(
                new IdentityOrganizationUnitProjectionPage([], null, false)));
    }

    private static async Task<long> CountAsync(
        DatabaseProvider databaseProvider,
        string connectionString,
        string table,
        string? predicate = null)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        var sql = $"SELECT COUNT(*) FROM {table}";
        if (!string.IsNullOrWhiteSpace(predicate))
        {
            sql += $" WHERE {predicate}";
        }

        return await connection.QuerySingleAsync<long>(sql);
    }

    private static DbConnection CreateConnection(
        DatabaseProvider databaseProvider,
        string connectionString) => databaseProvider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(
                MySqlConnectionStringPolicy.Create(
                    connectionString,
                    MySqlGuidStorageMode.Binary16,
                    allowUserVariables: false)),
            _ => throw new ArgumentOutOfRangeException(nameof(databaseProvider)),
        };

    private static IOptions<UuidBinaryContractOptions> ContractOptions() =>
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = "test-tenancy-uuid-contract-009",
        });

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException("The non-HTTP integration fixture does not map API results.");

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException("The non-HTTP integration fixture does not map API exceptions.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";

        public string ApplicationName { get; set; } = "Full.NET.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}

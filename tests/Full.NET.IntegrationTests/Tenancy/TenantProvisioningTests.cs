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
using Full.NET.Abstractions.Results;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Serialization.MessagePack;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
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
    public async Task SqlServer_provisioning_is_atomic_and_writes_binary_outbox()
    {
        await VerifyProviderAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_provisioning_is_atomic_and_writes_binary_outbox()
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
        await using (var services = BuildServices(configuration, throwOnOutbox: false))
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

            var outbox = await ReadOutboxAsync(databaseProvider, connectionString);
            Assert.AreEqual("fullnet.tenancy.tenant.provisioned", outbox.MessageType);
            Assert.AreNotEqual(default(DateTimeOffset), outbox.OccurredAtUtc);
            Assert.AreEqual(1, outbox.SchemaVersion);
            Assert.AreEqual("application/x-msgpack", outbox.ContentType);
            Assert.IsTrue(outbox.Payload.Length > 0);

            var serializer = scope.ServiceProvider
                .GetRequiredService<IIntegrationEventSerializer>();
            var integrationEvent = serializer
                .Deserialize<TenantProvisionedIntegrationEvent>(outbox.Payload);
            Assert.AreEqual(result.Value.Id, integrationEvent.TenantId);
            Assert.AreEqual("acme", integrationEvent.Identifier);
            Assert.AreEqual("acme.localhost", integrationEvent.Domain);

            var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var leasedMessages = await outboxStore.AcquireAsync(
                20,
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            Assert.HasCount(1, leasedMessages);
            var leasedMessage = leasedMessages[0];
            Assert.AreNotEqual(Guid.Empty, leasedMessage.LockId);
            Assert.AreEqual(1, leasedMessage.Attempts);
            Assert.AreEqual(outbox.MessageType, leasedMessage.MessageType);
            CollectionAssert.AreEqual(outbox.Payload, leasedMessage.Payload);
            await outboxStore.MarkProcessedAsync(
                leasedMessage.Id,
                leasedMessage.LockId,
                CancellationToken.None);

            var concurrencyDetected = false;
            try
            {
                await outboxStore.MarkProcessedAsync(
                    leasedMessage.Id,
                    leasedMessage.LockId,
                    CancellationToken.None);
            }
            catch (OutboxConcurrencyException)
            {
                concurrencyDetected = true;
            }

            Assert.IsTrue(concurrencyDetected);
        }

        Assert.AreEqual(
            1L,
            await CountAsync(databaseProvider, connectionString, "fn_tenancy_tenant"));
        Assert.AreEqual(
            1L,
            await CountAsync(databaseProvider, connectionString, "fn_outbox_message"));

        await using (var services = BuildServices(configuration, throwOnOutbox: true))
        {
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();
            var provisioning = scope.ServiceProvider
                .GetRequiredService<ITenantProvisioningService>();

            var exceptionObserved = false;
            try
            {
                await provisioning.ProvisionAsync(
                    new ProvisionTenantRequest(
                        "rollback",
                        "Rollback Tenant",
                        "rollback.localhost"));
            }
            catch (InvalidOperationException exception)
                when (exception.Message == ThrowingOutboxWriter.ExceptionMessage)
            {
                exceptionObserved = true;
            }

            Assert.IsTrue(exceptionObserved);
        }

        Assert.AreEqual(
            0L,
            await CountAsync(
                databaseProvider,
                connectionString,
                "fn_tenancy_tenant",
                "Identifier = 'rollback'"));
        Assert.AreEqual(
            1L,
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

    private static ServiceProvider BuildServices(
        IConfiguration configuration,
        bool throwOnOutbox)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment, TestHostEnvironment>();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        // 该夹具只验证非 HTTP 事务切片；显式替身用于满足完整模块的授权结果映射依赖。
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddFullNetModularity();
        services.AddFullNetDapper(configuration, "Testing");
        services.AddFullNetMessagePack();
        services.AddFullNetCaching(configuration, "Test");
        services.AddFullNetModule<IdentityModule>(configuration);
        services.AddFullNetModule<TenancyModule>(configuration);
        services.AddFullNetModule<OrganizationModule>(configuration);

        if (throwOnOutbox)
        {
            services.AddScoped<IOutboxWriter, ThrowingOutboxWriter>();
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static async Task<OutboxRow> ReadOutboxAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        await using var connection = CreateConnection(databaseProvider, connectionString);
        return await connection.QuerySingleAsync<OutboxRow>(
            """
            SELECT MessageType,
                   OccurredAtUtc,
                   SchemaVersion,
                   ContentType,
                   Payload
            FROM fn_outbox_message
            WHERE ProcessedAtUtc IS NULL
            """);
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

    private sealed class OutboxRow
    {
        public string MessageType { get; set; } = string.Empty;

        public DateTimeOffset OccurredAtUtc { get; set; }

        public int SchemaVersion { get; set; }

        public string ContentType { get; set; } = string.Empty;

        public byte[] Payload { get; set; } = [];
    }

    private sealed class ThrowingOutboxWriter : IOutboxWriter
    {
        public const string ExceptionMessage = "Test Outbox failure.";

        public Task AddAsync<TEvent>(
            string eventType,
            int schemaVersion,
            TEvent payload,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(ExceptionMessage);
    }

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

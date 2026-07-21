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
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Tenancy;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Serialization.MessagePack;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MySqlConnector;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;

namespace Full.NET.IntegrationTests.Seeding;

[TestClass]
public sealed class DevelopmentSeedTests
{
    private const string BootstrapUsername = "seed-administrator";
    private const string BootstrapPassword = "Seed-Admin!Integration42";
    private const string BootstrapDisplayName = "种子管理员";
    private const string TenantProvisionedEventType = "fullnet.tenancy.tenant.provisioned";

    [TestMethod]
    public async Task SqlServer_development_seed_contract()
    {
        await VerifyProviderAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_development_seed_contract()
    {
        await VerifyProviderAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyProviderAsync(
        DatabaseProvider databaseProvider,
        string connectionString)
    {
        var options = CreateDatabaseOptions(databaseProvider, connectionString);
        await MigrateAsync(options);

        await VerifyDevelopmentLifecycleAsync(options);
        await VerifyDevelopmentConflictAsync(options);
        await VerifyProductionGatesAsync(options);
        await VerifyTestProfileIsolationAsync(options);
    }

    private static async Task VerifyDevelopmentLifecycleAsync(DatabaseOptions options)
    {
        await using var services = BuildServices(options, "Development");
        await using var scope = services.CreateAsyncScope();
        PrepareHostScope(scope);
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();

        var first = await orchestrator.RunAsync(SeedProfile.Development);
        Assert.IsTrue(first.IsSuccess, first.Error?.Code);

        var firstPasswordHash = await ReadAdminPasswordHashAsync(
            options,
            scope.ServiceProvider);
        Assert.IsNotNull(firstPasswordHash);
        await AssertDevelopmentDataStateAsync(scope.ServiceProvider, options, 1L);
        await AssertLatestRunContributorsAsync(
            options,
            ["identity.host_administrator", "tenancy.local_tenant", "identity.e2e_host_viewer"],
            ["identity.host_administrator", "tenancy.local_tenant"]);

        var second = await orchestrator.RunAsync(SeedProfile.Development);
        Assert.IsTrue(second.IsSuccess, second.Error?.Code);

        var secondPasswordHash = await ReadAdminPasswordHashAsync(
            options,
            scope.ServiceProvider);
        Assert.AreEqual(firstPasswordHash, secondPasswordHash);
        await AssertDevelopmentDataStateAsync(scope.ServiceProvider, options, 2L);
        await AssertLatestRunContributorsAsync(
            options,
            ["identity.host_administrator", "tenancy.local_tenant", "identity.e2e_host_viewer"],
            ["identity.host_administrator", "tenancy.local_tenant"]);

        var hasher = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<IdentityUser>>();
        var admin = await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = "host", NormalizedUsername = BootstrapUsername.ToUpperInvariant() });
        Assert.IsNotNull(admin);
        Assert.AreEqual(
            Microsoft.AspNetCore.Identity.PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(
                new IdentityUser(
                    admin.Id,
                    admin.TenantId,
                    admin.ScopeKey,
                    admin.Username,
                    admin.NormalizedUsername,
                    admin.DisplayName,
                    admin.PasswordHash,
                    admin.IsActive,
                    admin.FailedLoginCount,
                    admin.LockoutEndUtc,
                    admin.SecurityStamp,
                    admin.CreatedAtUtc,
                    admin.UpdatedAtUtc,
                    admin.Version,
                    admin.PreferredLocale,
                    admin.ProfileVersion),
                admin.PasswordHash,
                BootstrapPassword));
    }

    private static async Task VerifyDevelopmentConflictAsync(DatabaseOptions options)
    {
        var conflictConnectionString = options.Provider switch
        {
            DatabaseProvider.SqlServer =>
                await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            DatabaseProvider.MySql =>
                await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider)),
        };
        var conflictOptions = CreateDatabaseOptions(options.Provider, conflictConnectionString);
        await MigrateAsync(conflictOptions);

        await using var services = BuildServices(conflictOptions, "Development");
        await using var scope = services.CreateAsyncScope();
        PrepareHostScope(scope);
        var provisioning = scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>();
        var conflict = await provisioning.ProvisionAsync(
            new ProvisionTenantRequest("local", "冲突租户", "conflict.localhost"));
        Assert.IsTrue(conflict.IsSuccess, conflict.Error?.Code);

        var result = await scope.ServiceProvider
            .GetRequiredService<ISeedOrchestrator>()
            .RunAsync(SeedProfile.Development);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SeedContributionErrorCodes.DataConflict, result.Error!.Code);
        Assert.AreEqual(
            1L,
            await CountAsync(
                conflictOptions,
                "fn_tenancy_tenant",
                "Identifier = 'local' AND Domain = 'conflict.localhost'"));
        Assert.AreEqual(1L, await CountAsync(conflictOptions, "fn_seed_run"));
    }

    private static async Task VerifyProductionGatesAsync(DatabaseOptions options)
    {
        var productionConnectionString = options.Provider switch
        {
            DatabaseProvider.SqlServer =>
                await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            DatabaseProvider.MySql =>
                await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider)),
        };
        var productionOptions = CreateDatabaseOptions(
            options.Provider,
            productionConnectionString);
        await MigrateAsync(productionOptions);

        await using var services = BuildServices(productionOptions, "Production");
        await using var scope = services.CreateAsyncScope();
        PrepareHostScope(scope);
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();

        var baseline = await orchestrator.RunAsync(SeedProfile.Baseline);
        Assert.IsTrue(baseline.IsSuccess, baseline.Error?.Code);
        Assert.AreEqual(1L, await CountAsync(productionOptions, "fn_seed_run"));

        foreach (var profile in new[] { SeedProfile.Development, SeedProfile.Demo, SeedProfile.Test })
        {
            var rejected = await orchestrator.RunAsync(profile);
            Assert.IsFalse(rejected.IsSuccess);
            Assert.AreEqual(SeedErrorCodes.ProfileNotAllowed, rejected.Error!.Code);
        }

        Assert.AreEqual(1L, await CountAsync(productionOptions, "fn_seed_run"));
    }

    private static async Task VerifyTestProfileIsolationAsync(DatabaseOptions options)
    {
        var testConnectionString = options.Provider switch
        {
            DatabaseProvider.SqlServer =>
                await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(),
            DatabaseProvider.MySql =>
                await SharedDatabaseFixture.CreateMySqlDatabaseAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider)),
        };
        var testOptions = CreateDatabaseOptions(options.Provider, testConnectionString);
        await MigrateAsync(testOptions);

        await using var services = BuildServices(
            testOptions,
            "Test",
            collection =>
            {
                collection.TryAddEnumerable(ServiceDescriptor.Scoped<
                    IDataSeedContributor,
                    TestOnlySeedContributor>());
            });
        await using var scope = services.CreateAsyncScope();
        PrepareHostScope(scope);

        var result = await scope.ServiceProvider
            .GetRequiredService<ISeedOrchestrator>()
            .RunAsync(SeedProfile.Test);
        Assert.IsTrue(result.IsSuccess, result.Error?.Code);

        await AssertLatestRunContributorsAsync(
            testOptions,
            ["identity.host_administrator", "testing.profile_contract_marker"],
            ["identity.host_administrator", "testing.profile_contract_marker"]);
        Assert.AreEqual(0L, await CountAsync(testOptions, "fn_tenancy_tenant"));
        Assert.AreEqual(
            1L,
            await CountAsync(
                testOptions,
                "fn_identity_user",
                "Username = @Username",
                new { Username = TestOnlySeedContributor.MarkerUsername }));
    }

    private static async Task AssertDevelopmentDataStateAsync(
        IServiceProvider services,
        DatabaseOptions options,
        long expectedSeedRuns)
    {
        var query = services.GetRequiredService<IQueryExecutor>();
        var admin = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
            IdentitySql.FindUserByScopeAndUsername,
            new { ScopeKey = "host", NormalizedUsername = BootstrapUsername.ToUpperInvariant() });
        Assert.IsNotNull(admin);
        Assert.AreEqual(BootstrapDisplayName, admin.DisplayName);

        var superAdminRoleCount = await query.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.count-super-administrator-role",
                """
                SELECT COUNT(*)
                FROM fn_identity_role
                WHERE ScopeKey = 'host'
                  AND Code = 'host-administrator'
                  AND IsSuperAdministrator = 1
                  AND IsSystem = 1
                """,
                SqlDataScope.HostOnly));
        Assert.AreEqual(1L, superAdminRoleCount);

        var assignmentCount = await query.QuerySingleOrDefaultAsync<long>(
            new SqlStatement(
                "test.count-super-administrator-assignment",
                """
                SELECT COUNT(*)
                FROM fn_identity_user_role AS assignment
                INNER JOIN fn_identity_role AS roleObject
                    ON roleObject.Id = assignment.RoleId
                WHERE assignment.UserId = @UserId
                  AND roleObject.Code = 'host-administrator'
                """,
                SqlDataScope.HostOnly),
            new { UserId = admin.Id });
        Assert.AreEqual(1L, assignmentCount);

        Assert.AreEqual(
            1L,
            await CountAsync(
                options,
                "fn_tenancy_tenant",
                "Identifier = 'local'"));
        Assert.AreEqual(
            1L,
            await CountAsync(
                options,
                "fn_outbox_message",
                "MessageType = @MessageType",
                new { MessageType = TenantProvisionedEventType }));
        Assert.AreEqual(expectedSeedRuns, await CountAsync(options, "fn_seed_run"));
    }

    private static async Task<string?> ReadAdminPasswordHashAsync(
        DatabaseOptions options,
        IServiceProvider services)
    {
        var admin = await services.GetRequiredService<IQueryExecutor>()
            .QuerySingleOrDefaultAsync<IdentityUserRecord>(
                IdentitySql.FindUserByScopeAndUsername,
                new { ScopeKey = "host", NormalizedUsername = BootstrapUsername.ToUpperInvariant() });
        return admin?.PasswordHash;
    }

    private static async Task AssertLatestRunContributorsAsync(
        DatabaseOptions options,
        IReadOnlyCollection<string> expectedContributors,
        IReadOnlyCollection<string> requiredSucceededContributors)
    {
        await using var connection = CreateConnection(options);
        var latestRunId = options.Provider switch
        {
            DatabaseProvider.SqlServer => await connection.QuerySingleAsync<Guid>(
                """
                SELECT TOP (1) Id
                FROM fn_seed_run
                ORDER BY StartedAt DESC
                """),
            DatabaseProvider.MySql => await connection.QuerySingleAsync<Guid>(
                """
                SELECT Id
                FROM fn_seed_run
                ORDER BY StartedAt DESC
                LIMIT 1
                """),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider)),
        };
        var items = (await connection.QueryAsync<SeedRunItemRow>(
            """
            SELECT Contributor, Status
            FROM fn_seed_run_item
            WHERE RunId = @RunId
            ORDER BY StartedAt
            """,
            new { RunId = latestRunId })).ToArray();

        CollectionAssert.AreEquivalent(
            expectedContributors.ToArray(),
            items.Select(item => item.Contributor).ToArray());
        foreach (var contributor in requiredSucceededContributors)
        {
            var item = items.Single(row => row.Contributor == contributor);
            Assert.AreEqual(SeedExecutionStatuses.Succeeded, item.Status);
        }
    }

    private static async Task MigrateAsync(DatabaseOptions options)
    {
        var migration = new DbUpMigrationRunner(
            Options.Create(options),
            NullLoggerFactory.Instance,
            Options.Create(new UuidBinaryContractOptions
            {
                MaintenanceMode = true,
                BackupVerified = true,
                LegacyWritersStopped = true,
                DestructiveDdlApprovalId = "test-development-seed-uuid-contract-009",
            }),
            MigrationContractOptionFactory.NamingOptions());
        var result = await migration.MigrateAsync();
        Assert.IsTrue(result.Successful);
    }

    private static DatabaseOptions CreateDatabaseOptions(
        DatabaseProvider provider,
        string connectionString) =>
        new()
        {
            Provider = provider,
            ConnectionString = connectionString,
            MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
            CommandTimeoutSeconds = 300,
        };

    private static ServiceProvider BuildServices(
        DatabaseOptions options,
        string environmentName,
        Action<IServiceCollection>? configure = null)
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
                ["Identity:Bootstrap:Username"] = BootstrapUsername,
                ["Identity:Bootstrap:Password"] = BootstrapPassword,
                ["Identity:Bootstrap:DisplayName"] = BootstrapDisplayName,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddSingleton<IHostEnvironment>(new SeedTestHostEnvironment(environmentName));
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddFullNetModularity();
        services.AddFullNetDapper(configuration, environmentName);
        services.AddFullNetMessagePack();
        services.AddFullNetCaching(configuration, environmentName);
        services.AddFullNetSeeding(configuration);
        services.AddFullNetModule<IdentityModule>(configuration);
        services.AddFullNetModule<TenancyModule>(configuration);
        services.AddFullNetModule<OrganizationModule>(configuration);
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static void PrepareHostScope(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

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

    private sealed record SeedRunItemRow(string Contributor, string Status);

    private sealed class SeedTestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Full.NET.IntegrationTests.Seeding";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Full.NET.Abstractions.Results.Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException("Seed integration fixture does not map API results.");

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException("Seed integration fixture does not map API exceptions.");
    }
}

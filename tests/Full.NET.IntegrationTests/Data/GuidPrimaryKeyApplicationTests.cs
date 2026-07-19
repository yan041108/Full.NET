using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Testcontainers.MySql;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class GuidPrimaryKeyApplicationTests
{
    private static readonly SqlStatement InsertOutbox = new(
        "test.outbox.insert-guid-primary-key",
        """
        INSERT INTO fn_outbox_message
            (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt, Attempts, LockId)
        VALUES
            (@Id, @Type, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAt, 0, @LockId)
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement FindPersistedGraph = new(
        "test.guid-primary-key.find-graph",
        """
        SELECT usr.Id AS UserId,
               session.Id AS SessionId,
               session.UserId AS SessionUserId,
               session.FamilyId,
               audit.Id AS AuditId,
               audit.UserId AS AuditUserId,
               audit.SessionId AS AuditSessionId,
               outbox.Id AS OutboxId,
               outbox.LockId AS OutboxLockId
        FROM fn_identity_user AS usr
        INNER JOIN fn_identity_refresh_session AS session ON session.UserId = usr.Id
        INNER JOIN fn_identity_auth_audit AS audit
            ON audit.UserId = usr.Id AND audit.SessionId = session.Id
        INNER JOIN fn_outbox_message AS outbox ON outbox.Id = @OutboxId
        WHERE usr.Id = @UserId
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement CountUsersByUsername = new(
        "test.guid-primary-key.count-users",
        """
        SELECT COUNT(*)
        FROM fn_identity_user
        WHERE Username = @Username
        """,
        SqlDataScope.HostOnly);

    [TestMethod]
    public async Task SqlServer_same_transaction_preserves_uuid_references_and_rolls_back_on_failure()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();

        await VerifyProviderAsync(
            DatabaseProvider.SqlServer,
            container.GetConnectionString());
    }

    [TestMethod]
    public async Task MySql_same_transaction_preserves_uuid_references_and_rolls_back_on_failure()
    {
        await using var container = new MySqlBuilder("mysql:8.0")
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet")
            .WithUsername("fullnet")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();

        await VerifyProviderAsync(
            DatabaseProvider.MySql,
            container.GetConnectionString());
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
            ContractOptions());
        await migrationRunner.MigrateAsync();

        var configuration = CreateConfiguration(options);
        await using var services = BuildServices(configuration);
        await using var scope = services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();

        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var commandTransaction = scope.ServiceProvider.GetRequiredService<ICommandTransaction>();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var userId = idGenerator.NewId();
        var sessionId = idGenerator.NewId();
        var familyId = idGenerator.NewId();
        var auditId = idGenerator.NewId();
        var outboxId = idGenerator.NewId();
        var lockId = idGenerator.NewId();
        var username = $"uuid-app-{userId:N}";

        await commandTransaction.ExecuteAsync<bool>(
            async cancellationToken =>
            {
                await InsertGraphAsync(
                    commandExecutor,
                    now,
                    userId,
                    sessionId,
                    familyId,
                    auditId,
                    outboxId,
                    lockId,
                    username,
                    cancellationToken);
                return true;
            },
            CancellationToken.None);

        var graph = await queryExecutor.QuerySingleOrDefaultAsync<PersistedGraphRow>(
            FindPersistedGraph,
            new { UserId = userId, OutboxId = outboxId });
        Assert.IsNotNull(graph);
        Assert.AreEqual(userId, graph.UserId);
        Assert.AreEqual(sessionId, graph.SessionId);
        Assert.AreEqual(userId, graph.SessionUserId);
        Assert.AreEqual(familyId, graph.FamilyId);
        Assert.AreEqual(auditId, graph.AuditId);
        Assert.AreEqual(userId, graph.AuditUserId);
        Assert.AreEqual(sessionId, graph.AuditSessionId);
        Assert.AreEqual(outboxId, graph.OutboxId);
        Assert.AreEqual(lockId, graph.OutboxLockId);

        var rollbackUsername = $"uuid-rollback-{userId:N}";
        var rollbackObserved = false;
        try
        {
            await commandTransaction.ExecuteAsync<bool>(
                async cancellationToken =>
                {
                    await InsertGraphAsync(
                        commandExecutor,
                        now,
                        idGenerator.NewId(),
                        idGenerator.NewId(),
                        idGenerator.NewId(),
                        idGenerator.NewId(),
                        idGenerator.NewId(),
                        idGenerator.NewId(),
                        rollbackUsername,
                        cancellationToken);
                    throw new InvalidOperationException("Injected rollback for UUID primary-key application test.");
                },
                CancellationToken.None);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("Injected rollback", StringComparison.Ordinal))
        {
            rollbackObserved = true;
        }

        Assert.IsTrue(rollbackObserved);
        var rollbackCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
            CountUsersByUsername,
            new { Username = rollbackUsername });
        Assert.AreEqual(0L, rollbackCount);
    }

    private static async Task InsertGraphAsync(
        ICommandExecutor commandExecutor,
        DateTimeOffset now,
        Guid userId,
        Guid sessionId,
        Guid familyId,
        Guid auditId,
        Guid outboxId,
        Guid lockId,
        string username,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertUser,
            new IdentityUserRecord(
                userId,
                null,
                "host",
                username,
                username.ToUpperInvariant(),
                "UUID Application Test",
                "unused",
                true,
                0,
                null,
                Guid.NewGuid().ToString("N"),
                now,
                null,
                1),
            cancellationToken);
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertRefreshSession,
            new RefreshSession(
                sessionId,
                userId,
                familyId,
                "fullnet-admin",
                $"token-{sessionId:N}",
                now.AddHours(1),
                null,
                null,
                null,
                null,
                now,
                1),
            cancellationToken);
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertAuthAudit,
            new AuthAuditEvent(
                auditId,
                userId,
                sessionId,
                "uuid-app",
                "identity.login.succeeded",
                "identity.login.succeeded",
                true,
                "127.0.0.1",
                "integration-test",
                null,
                now),
            cancellationToken);
        await commandExecutor.ExecuteAsync(
            InsertOutbox,
            new
            {
                Id = outboxId,
                Type = "test.uuid-primary-key",
                SchemaVersion = 1,
                ContentType = "application/octet-stream",
                TenantId = (Guid?)null,
                TraceId = "trace-uuid-app",
                Payload = new byte[] { 0x01 },
                OccurredAt = now,
                LockId = lockId,
            },
            cancellationToken);
    }

    private static IConfiguration CreateConfiguration(DatabaseOptions options) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = options.Provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = options.ConnectionString,
                [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                    options.MySqlGuidStorageMode.ToString(),
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "300",
            })
            .Build();

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddFullNetDapper(configuration, "Testing");
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            // 本夹具只验证 ICommandTransaction 与 UUID 引用；Outbox 写入走显式 SQL。
            ValidateOnBuild = false,
            ValidateScopes = true,
        });
    }

    private static IOptions<UuidBinaryContractOptions> ContractOptions() =>
        Options.Create(new UuidBinaryContractOptions
        {
            MaintenanceMode = true,
            BackupVerified = true,
            LegacyWritersStopped = true,
            DestructiveDdlApprovalId = "test-guid-primary-key-009",
        });

    private sealed class PersistedGraphRow
    {
        public Guid UserId { get; init; }

        public Guid SessionId { get; init; }

        public Guid SessionUserId { get; init; }

        public Guid FamilyId { get; init; }

        public Guid AuditId { get; init; }

        public Guid AuditUserId { get; init; }

        public Guid AuditSessionId { get; init; }

        public Guid OutboxId { get; init; }

        public Guid OutboxLockId { get; init; }
    }
}

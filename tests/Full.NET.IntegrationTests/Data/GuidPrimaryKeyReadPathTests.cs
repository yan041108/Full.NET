using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Contracts;
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
public sealed class GuidPrimaryKeyReadPathTests
{
    private static readonly SqlStatement InsertTenant = new(
        "test.guid-read-path.insert-tenant",
        """
        INSERT INTO fn_tenant_tenant
            (Id, Identifier, Name, Domain, IsActive, CreatedAt, Version, DefaultLocale)
        VALUES
            (@Id, @Identifier, @Name, @Domain, @IsActive, @CreatedAt, @Version, @DefaultLocale)
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement InsertOutbox = new(
        "test.guid-read-path.insert-outbox",
        """
        INSERT INTO fn_outbox_message
            (Id, Type, SchemaVersion, ContentType, TenantId, TraceId, Payload, OccurredAt, Attempts)
        VALUES
            (@Id, @Type, @SchemaVersion, @ContentType, @TenantId, @TraceId, @Payload, @OccurredAt, 0)
        """,
        SqlDataScope.Global);

    private static readonly SqlStatement ListSessionsByFamily = new(
        "test.guid-read-path.list-sessions-by-family",
        """
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               session.ReplacedById,
               session.ActiveTenantId
        FROM fn_identity_refresh_session AS session
        WHERE session.FamilyId = @FamilyId
        ORDER BY session.CreatedAtUtc, session.Id
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement ListAuditsByActor = new(
        "test.guid-read-path.list-audits-by-actor",
        """
        SELECT audit.Id,
               audit.UserId AS TargetUserId,
               audit.ActorUserId,
               audit.ContextTenantId
        FROM fn_identity_auth_audit AS audit
        WHERE audit.ActorUserId = @ActorUserId
        ORDER BY audit.OccurredAtUtc, audit.Id
        """,
        SqlDataScope.HostOnly);

    private static readonly SqlStatement ReadSessionAndUserMultiple = new(
        "test.guid-read-path.read-session-and-user",
        """
        SELECT session.Id AS SessionId,
               session.UserId,
               session.FamilyId,
               session.ReplacedById,
               session.ActiveTenantId
        FROM fn_identity_refresh_session AS session
        WHERE session.Id = @SessionId;
        SELECT identityUser.Id AS UserId,
               identityUser.TenantId
        FROM fn_identity_user AS identityUser
        WHERE identityUser.Id = @UserId;
        """,
        SqlDataScope.HostOnly);

    [TestMethod]
    public async Task SqlServer_read_paths_project_guids_for_sessions_outbox_and_audits()
    {
        await using var container = new MsSqlBuilder(
                "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();

        await VerifyReadPathsAsync(
            DatabaseProvider.SqlServer,
            container.GetConnectionString());
    }

    [TestMethod]
    public async Task MySql_read_paths_project_guids_for_sessions_outbox_and_audits()
    {
        await using var container = new MySqlBuilder("mysql:8.0")
            .WithCommand("--log-bin-trust-function-creators=1")
            .WithDatabase("fullnet")
            .WithUsername("fullnet")
            .WithPassword("FullNet_Test!123")
            .Build();
        await container.StartAsync();

        await VerifyReadPathsAsync(
            DatabaseProvider.MySql,
            container.GetConnectionString());
    }

    private static async Task VerifyReadPathsAsync(
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
        await new DbUpMigrationRunner(
            Options.Create(options),
            NullLoggerFactory.Instance,
            ContractOptions()).MigrateAsync();

        var configuration = CreateConfiguration(options);
        await using var services = BuildServices(configuration);
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var commandExecutor = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
        var queryExecutor = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
        var multiQueryExecutor = scope.ServiceProvider.GetRequiredService<IMultiResultQueryExecutor>();
        var outboxStore = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var tenantId = idGenerator.NewId();
        var actorUserId = idGenerator.NewId();
        var targetUserId = idGenerator.NewId();
        var familyId = idGenerator.NewId();
        var oldSessionId = idGenerator.NewId();
        var newSessionId = idGenerator.NewId();
        var auditId = idGenerator.NewId();
        var hostOutboxId = idGenerator.NewId();
        var tenantOutboxId = idGenerator.NewId();
        var actorUsername = $"actor-{actorUserId:N}";
        var targetUsername = $"target-{targetUserId:N}";

        await SeedReadPathDataAsync(
            commandExecutor,
            now,
            tenantId,
            actorUserId,
            targetUserId,
            familyId,
            oldSessionId,
            newSessionId,
            auditId,
            hostOutboxId,
            tenantOutboxId,
            actorUsername,
            targetUsername);

        var currentSession = await queryExecutor.QuerySingleOrDefaultAsync<RefreshSessionRecord>(
            IdentitySql.FindRefreshSessionById,
            new { SessionId = newSessionId });
        Assert.IsNotNull(currentSession);
        Assert.AreEqual(newSessionId, currentSession.SessionId);
        Assert.AreEqual(targetUserId, currentSession.UserId);
        Assert.AreEqual(familyId, currentSession.FamilyId);
        Assert.AreEqual(tenantId, currentSession.ActiveTenantId);
        Assert.IsNull(currentSession.ReplacedById);
        AssertGuidProjection(currentSession);

        var familySessions = await queryExecutor.QueryAsync<SessionFamilyRow>(
            ListSessionsByFamily,
            new { FamilyId = familyId });
        Assert.HasCount(2, familySessions);
        Assert.IsTrue(familySessions.All(row => row.FamilyId == familyId));
        var oldSession = familySessions.Single(row => row.SessionId == oldSessionId);
        Assert.AreEqual(newSessionId, oldSession.ReplacedById);
        foreach (var row in familySessions)
        {
            AssertGuidProjection(row);
        }

        var multiple = await multiQueryExecutor.QueryMultipleAsync(
            ReadSessionAndUserMultiple,
            new { SessionId = newSessionId, UserId = targetUserId },
            async (reader, cancellationToken) =>
            {
                var session = await reader.ReadSingleOrDefaultAsync<SessionFamilyRow>();
                var user = await reader.ReadSingleOrDefaultAsync<UserTenantRow>();
                return (session, user);
            });
        Assert.IsNotNull(multiple.session);
        Assert.IsNotNull(multiple.user);
        Assert.AreEqual(newSessionId, multiple.session.SessionId);
        Assert.AreEqual(targetUserId, multiple.user.UserId);
        Assert.IsNull(multiple.user.TenantId);
        AssertGuidProjection(multiple.session);
        AssertGuidProjection(multiple.user);

        var actorAudits = await queryExecutor.QueryAsync<ActorAuditRow>(
            ListAuditsByActor,
            new { ActorUserId = actorUserId });
        Assert.HasCount(1, actorAudits);
        Assert.AreEqual(auditId, actorAudits[0].Id);
        Assert.AreEqual(targetUserId, actorAudits[0].TargetUserId);
        Assert.AreEqual(actorUserId, actorAudits[0].ActorUserId);
        Assert.IsNull(actorAudits[0].ContextTenantId);
        AssertGuidProjection(actorAudits[0]);

        var auditStatement = databaseProvider == DatabaseProvider.SqlServer
            ? IdentitySql.ListSuperAdministratorAuditsSqlServer
            : IdentitySql.ListSuperAdministratorAuditsMySql;
        var superAdminAudits = await queryExecutor.QueryAsync<SuperAdministratorAuditResponse>(
            auditStatement,
            new { Limit = 10 });
        Assert.IsTrue(superAdminAudits.Any(audit => audit.Id == auditId));
        Assert.IsTrue(superAdminAudits.All(audit =>
            audit.ActorUserId is null || audit.ActorUserId is Guid));
        foreach (var audit in superAdminAudits)
        {
            AssertGuidProjection(audit);
        }

        var leasedMessages = await outboxStore.AcquireAsync(
            5,
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        Assert.IsGreaterThanOrEqualTo(2, leasedMessages.Count);
        var leasedHost = leasedMessages.Single(message => message.Id == hostOutboxId);
        var leasedTenant = leasedMessages.Single(message => message.Id == tenantOutboxId);
        Assert.IsNull(leasedHost.TenantId);
        Assert.AreEqual(tenantId, leasedTenant.TenantId);
        Assert.AreNotEqual(Guid.Empty, leasedHost.LockId);
        Assert.AreNotEqual(Guid.Empty, leasedTenant.LockId);
        foreach (var message in leasedMessages)
        {
            AssertGuidProjection(message, nameof(OutboxEnvelope.Payload));
        }

        await outboxStore.MarkProcessedAsync(
            leasedHost.Id,
            leasedHost.LockId,
            CancellationToken.None);
        await outboxStore.MarkProcessedAsync(
            leasedTenant.Id,
            leasedTenant.LockId,
            CancellationToken.None);
    }

    private static async Task SeedReadPathDataAsync(
        ICommandExecutor commandExecutor,
        DateTimeOffset now,
        Guid tenantId,
        Guid actorUserId,
        Guid targetUserId,
        Guid familyId,
        Guid oldSessionId,
        Guid newSessionId,
        Guid auditId,
        Guid hostOutboxId,
        Guid tenantOutboxId,
        string actorUsername,
        string targetUsername)
    {
        await commandExecutor.ExecuteAsync(
            InsertTenant,
            new
            {
                Id = tenantId,
                Identifier = $"read-{tenantId:N}"[..20],
                Name = "UUID Read Path Tenant",
                Domain = $"read-{tenantId:N}.localhost",
                IsActive = true,
                CreatedAt = now,
                Version = 1,
                DefaultLocale = "zh-CN",
            });
        await InsertHostUserAsync(commandExecutor, now, actorUserId, actorUsername);
        await InsertHostUserAsync(commandExecutor, now, targetUserId, targetUsername);
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertRefreshSession,
            new RefreshSession(
                oldSessionId,
                targetUserId,
                familyId,
                "fullnet-admin",
                $"old-{oldSessionId:N}",
                now.AddHours(1),
                now,
                null,
                newSessionId,
                null,
                now.AddMinutes(-5),
                2));
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertRefreshSession,
            new RefreshSession(
                newSessionId,
                targetUserId,
                familyId,
                "fullnet-admin",
                $"new-{newSessionId:N}",
                now.AddHours(2),
                null,
                null,
                null,
                tenantId,
                now,
                1));
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertSuperAdministratorAudit,
            new
            {
                Id = auditId,
                UserId = targetUserId,
                SessionId = (Guid?)null,
                UsernameFingerprint = "uuid-read-path",
                EventType = "identity.super_administrator.granted",
                ResultCode = "identity.super_administrator.granted",
                Succeeded = true,
                IpAddress = (string?)null,
                UserAgent = (string?)null,
                ContextTenantId = (Guid?)null,
                OccurredAtUtc = now,
                ActorUserId = actorUserId,
            });
        await commandExecutor.ExecuteAsync(
            InsertOutbox,
            new
            {
                Id = hostOutboxId,
                Type = "test.uuid-read-path.host",
                SchemaVersion = 1,
                ContentType = "application/octet-stream",
                TenantId = (Guid?)null,
                TraceId = "trace-host",
                Payload = new byte[] { 0x01 },
                OccurredAt = now,
            });
        await commandExecutor.ExecuteAsync(
            InsertOutbox,
            new
            {
                Id = tenantOutboxId,
                Type = "test.uuid-read-path.tenant",
                SchemaVersion = 1,
                ContentType = "application/octet-stream",
                TenantId = tenantId,
                TraceId = "trace-tenant",
                Payload = new byte[] { 0x02 },
                OccurredAt = now,
            });
    }

    private static async Task InsertHostUserAsync(
        ICommandExecutor commandExecutor,
        DateTimeOffset now,
        Guid userId,
        string username) =>
        await commandExecutor.ExecuteAsync(
            IdentitySql.InsertUser,
            new IdentityUserRecord(
                userId,
                null,
                "host",
                username,
                username.ToUpperInvariant(),
                "UUID Read Path",
                "unused",
                true,
                0,
                null,
                Guid.NewGuid().ToString("N"),
                now,
                null,
                1));

    private static void AssertGuidProjection<T>(T value, params string[] allowedByteArrayProperties)
    {
        var allowed = new HashSet<string>(allowedByteArrayProperties, StringComparer.Ordinal);
        foreach (var property in typeof(T).GetProperties())
        {
            if (allowed.Contains(property.Name))
            {
                continue;
            }

            Assert.AreNotEqual(
                typeof(byte[]),
                property.PropertyType,
                $"属性 {property.Name} 不得映射为 byte[]。");
        }

        Assert.IsNotNull(value);
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
            DestructiveDdlApprovalId = "test-guid-read-path-009",
        });

    private sealed class SessionFamilyRow
    {
        public Guid SessionId { get; init; }

        public Guid UserId { get; init; }

        public Guid FamilyId { get; init; }

        public Guid? ReplacedById { get; init; }

        public Guid? ActiveTenantId { get; init; }
    }

    private sealed class UserTenantRow
    {
        public Guid UserId { get; init; }

        public Guid? TenantId { get; init; }
    }

    private sealed class ActorAuditRow
    {
        public Guid Id { get; init; }

        public Guid TargetUserId { get; init; }

        public Guid? ActorUserId { get; init; }

        public Guid? ContextTenantId { get; init; }
    }
}

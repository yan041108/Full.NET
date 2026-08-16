using System.Net;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.DangerZone;

namespace Full.NET.IntegrationTests.Api;

internal sealed class FullNetApiFactory(
    DatabaseProvider provider,
    string connectionString,
    IReadOnlyDictionary<string, string?>? settingsOverrides = null,
    IPAddress? connectionRemoteIpAddress = null,
    Action<IServiceCollection>? configureTestServices = null) : WebApplicationFactory<Program>
{
    public const string TestPassword = "FullNet!2026Integration";

    public DatabaseProvider Provider => provider;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly string _cacheInstanceId = $"integration-{Guid.NewGuid():N}";
    private readonly List<BackplaneEventObservation> _backplaneEvents = [];
    private readonly object _backplaneEventsLock = new();
    private bool _backplaneEventsSubscribed;
    private bool _initialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = BuildSettings();
        foreach (var pair in settings.Where(pair => pair.Value is not null))
        {
            // Minimal Hosting 会在 ConfigureAppConfiguration 回调前执行部分即时配置读取；
            // WebHost setting 确保 Redis 等启动期依赖在模块注册时已经可见。
            builder.UseSetting(pair.Key, pair.Value);
        }

        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hosts",
            "Full.NET.Host.Api"));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            if (connectionRemoteIpAddress is not null)
            {
                services.AddSingleton<IStartupFilter>(
                    new TestConnectionRemoteIpAddressStartupFilter(
                        connectionRemoteIpAddress));
            }

            services.PostConfigure<FusionCacheOptions>(options =>
            {
                // 集成测试会在同一进程里启动多个 API 工厂；若共享同一个 InstanceId，
                // FusionCache Backplane 会把它们视为同一缓存节点并忽略彼此的失效广播。
                FusionCacheDangerZoneUtils.SetInstanceId(options, _cacheInstanceId);
            });
            configureTestServices?.Invoke(services);
        });
    }

    private Dictionary<string, string?> BuildSettings()
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] = "Binary16",
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
            ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
            // 一般 API 契约场景会多次登录不同用户；登录限流语义由专用测试显式覆盖。
            ["Identity:LoginRateLimitPermitLimitPerMinute"] = "1000",
            ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["Tenancy:HostDomains:0"] = "localhost",
            ["Files:Local:RootPath"] = Path.Combine(
                Path.GetTempPath(),
                "fullnet-files-integration",
                _cacheInstanceId),
            // Testing 常与 Cache 共用同一 Testcontainer Redis；生产隔离由专用连接串门禁保证。
            ["Realtime:AllowSharedRedisInDevelopment"] = "true",
        };

        if (settingsOverrides is null)
        {
            return settings;
        }

        // 测试宿主只允许在内存配置层覆盖必要设置，避免为单个场景复制整套 API 装配逻辑。
        foreach (var pair in settingsOverrides)
        {
            settings[pair.Key] = pair.Value;
        }

        return settings;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default,
        bool useSchemaTemplate = true)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            if (useSchemaTemplate)
            {
                // 空库先克隆只含 DbUp schema 的模板，避免每个用例重跑 90+ 条迁移；
                // 租户供给、管理员引导和导航同步仍由本用例执行，保持与非克隆路径相同的业务数据。
                await ApiSchemaTemplate.TryHydrateEmptyDatabaseAsync(
                        provider,
                        connectionString,
                        (templateConnectionString, templateCancellation) =>
                            RunDbUpMigrationsAsync(
                                provider,
                                templateConnectionString,
                                templateCancellation),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await MigrateAndBootstrapAsync(cancellationToken).ConfigureAwait(false);

            SubscribeBackplaneEvents();
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static Task RunDbUpMigrationsAsync(
        DatabaseProvider databaseProvider,
        string migrateConnectionString,
        CancellationToken cancellationToken) =>
        new DbUpMigrationRunner(
                Options.Create(new DatabaseOptions
                {
                    Provider = databaseProvider,
                    ConnectionString = migrateConnectionString,
                    MySqlGuidStorageMode = MySqlGuidStorageMode.Binary16,
                    CommandTimeoutSeconds = 300,
                }),
                NullLoggerFactory.Instance,
                Options.Create(new UuidBinaryContractOptions
                {
                    MaintenanceMode = true,
                    BackupVerified = true,
                    LegacyWritersStopped = true,
                    DestructiveDdlApprovalId = "test-api-uuid-contract-009",
                }),
                MigrationContractOptionFactory.NamingOptions())
            .MigrateAsync(cancellationToken);

    private async Task MigrateAndBootstrapAsync(CancellationToken cancellationToken)
    {
        await RunDbUpMigrationsAsync(provider, connectionString, cancellationToken)
            .ConfigureAwait(false);
        using var bootstrapClient = CreateClient();
        await using var scope = Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(
                    new ProvisionTenantRequest(
                        "acme",
                        "Acme Corporation",
                        "acme.localhost"),
                    cancellationToken);
            if (!result.IsSuccess
                && result.Error?.Code is not TenancyErrorCodes.IdentifierExists
                && result.Error?.Code is not TenancyErrorCodes.DomainExists)
            {
                throw new InvalidOperationException(
                    $"Test tenant provisioning failed: {result.Error?.Code} - "
                    + result.Error?.Message);
            }

            var bootstrap = await scope.ServiceProvider
                .GetRequiredService<IIdentityBootstrapService>()
                .BootstrapHostAdminAsync(
                    new BootstrapHostAdminRequest(
                        "admin",
                        TestPassword,
                        "系统管理员"),
                    cancellationToken);
            if (!bootstrap.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Test identity bootstrap failed: {bootstrap.Error?.Code}");
            }

            await scope.ServiceProvider
                .GetRequiredService<HostNavigationCatalogSyncService>()
                .SyncMissingCatalogEntriesAsync(cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    public string CacheInstanceId => _cacheInstanceId;

    public void ClearBackplaneEvents()
    {
        lock (_backplaneEventsLock)
        {
            _backplaneEvents.Clear();
        }
    }

    public IReadOnlyList<BackplaneEventObservation> GetBackplaneEventsSnapshot()
    {
        lock (_backplaneEventsLock)
        {
            return _backplaneEvents.ToArray();
        }
    }

    public HttpClient CreateClientForHost(string host)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://localhost")
        });
        client.DefaultRequestHeaders.Host = host;
        return client;
    }

    public FullNetApiFactory CreateIsolatedFactory()
    {
        return new FullNetApiFactory(
            provider,
            connectionString,
            settingsOverrides,
            connectionRemoteIpAddress,
            configureTestServices);
    }

    public async Task<string> CreateHostAccessTokenAsync(
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default) =>
        (await CreateHostIdentityAsync(
                $"limited-{Guid.NewGuid():N}",
                permissions,
                cancellationToken)
            .ConfigureAwait(false)).AccessToken;

    public async Task<HostTestIdentity> CreateHostIdentityAsync(
        string username,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var suffix = userId.ToString("N");
        var user = new IdentityUser(
            userId,
            null,
            "host",
            username,
            username.ToUpperInvariant(),
            "受限管理员",
            "unused",
            true,
            0,
            null,
            Guid.NewGuid().ToString("N"),
            now,
            null,
            1);
        await using var scope = Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider
            .GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var command = scope.ServiceProvider.GetRequiredService<ICommandExecutor>();
            await command.ExecuteAsync(
                IdentitySql.InsertUser,
                new IdentityUserRecord(
                    user.Id,
                    user.TenantId,
                    user.ScopeKey,
                    user.Username,
                    user.NormalizedUsername,
                    user.DisplayName,
                    user.PasswordHash,
                    user.IsActive,
                    user.FailedLoginCount,
                    user.LockoutEndUtc,
                    user.SecurityStamp,
                    user.CreatedAtUtc,
                    user.UpdatedAtUtc,
                    user.Version,
                    user.PreferredLocale,
                    user.ProfileVersion),
                cancellationToken);
            await command.ExecuteAsync(
                IdentitySql.InsertRefreshSession,
                new RefreshSession(
                    sessionId,
                    userId,
                    Guid.NewGuid(),
                    "fullnet-admin",
                    $"test-{suffix}",
                    now.AddHours(1),
                    null,
                    null,
                    null,
                    null,
                    now,
                    1),
                cancellationToken);
            var accessToken = scope.ServiceProvider
                .GetRequiredService<IAccessTokenIssuer>()
                .Issue(user, sessionId, null, permissions, false)
                .AccessToken;
            return new HostTestIdentity(userId, username, accessToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    public async Task<long> GetAuthenticationAuditCountAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                .QuerySingleOrDefaultAsync<long>(
                    IdentitySql.CountAuthenticationAudits,
                    cancellationToken: cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    public async Task<long> GetAuthenticationAuditCountByIpAddressAsync(
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            return await scope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                .QuerySingleOrDefaultAsync<long>(
                    new SqlStatement(
                        "test.count-authentication-audits-by-ip-address",
                        """
                        SELECT COUNT(*)
                        FROM fn_identity_auth_audit
                        WHERE IpAddress = @IpAddress
                        """,
                        SqlDataScope.HostOnly),
                    new { IpAddress = ipAddress },
                    cancellationToken);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    public async Task<HostAuthorizationState> GetHostAuthorizationStateAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope = Services.CreateAsyncScope();
        var currentTenant = scope.ServiceProvider.GetRequiredService<CurrentTenantAccessor>();
        currentTenant.SetHost();
        try
        {
            var bootstrap = await scope.ServiceProvider
                .GetRequiredService<IIdentityBootstrapService>()
                .BootstrapHostAdminAsync(
                    new BootstrapHostAdminRequest(
                        "admin",
                        TestPassword,
                        "系统管理员"),
                    cancellationToken);
            Assert.IsTrue(bootstrap.IsSuccess);
            Assert.IsFalse(bootstrap.Value!.Created);
            Assert.IsTrue(bootstrap.Value.AuthorizationSynchronized);

            var query = scope.ServiceProvider.GetRequiredService<IQueryExecutor>();
            var roleCount = await query.QuerySingleOrDefaultAsync<long>(
                new SqlStatement(
                    "test.count-host-administrator-roles",
                    """
                    SELECT COUNT(*)
                    FROM fn_identity_role
                    WHERE ScopeKey = 'host' AND Code = 'host-administrator'
                      AND IsSystem = 1 AND IsActive = 1
                      AND IsSuperAdministrator = 1
                    """,
                    SqlDataScope.HostOnly),
                cancellationToken: cancellationToken);
            var permissionCount = await query.QuerySingleOrDefaultAsync<long>(
                new SqlStatement(
                    "test.count-host-administrator-permissions",
                    """
                    SELECT COUNT(*)
                    FROM fn_identity_role_permission AS rolePermission
                    INNER JOIN fn_identity_role AS roleObject
                        ON roleObject.Id = rolePermission.RoleId
                    WHERE roleObject.ScopeKey = 'host'
                      AND roleObject.Code = 'host-administrator'
                    """,
                    SqlDataScope.HostOnly),
                cancellationToken: cancellationToken);
            var assignmentCount = await query.QuerySingleOrDefaultAsync<long>(
                new SqlStatement(
                    "test.count-host-administrator-assignments",
                    """
                    SELECT COUNT(*)
                    FROM fn_identity_user_role AS userRole
                    INNER JOIN fn_identity_role AS roleObject
                        ON roleObject.Id = userRole.RoleId
                    INNER JOIN fn_identity_user AS identityUser
                        ON identityUser.Id = userRole.UserId
                    WHERE roleObject.ScopeKey = 'host'
                      AND roleObject.Code = 'host-administrator'
                      AND identityUser.ScopeKey = 'host'
                      AND identityUser.NormalizedUsername = 'ADMIN'
                    """,
                    SqlDataScope.HostOnly),
                cancellationToken: cancellationToken);

            return new HostAuthorizationState(
                roleCount,
                permissionCount,
                assignmentCount);
        }
        finally
        {
            currentTenant.Clear();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _initializationLock.Dispose();
        }

        base.Dispose(disposing);
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

    private void SubscribeBackplaneEvents()
    {
        if (_backplaneEventsSubscribed)
        {
            return;
        }

        var cache = Services.GetRequiredService<IFusionCache>();
        if (!cache.HasBackplane)
        {
            return;
        }

        cache.Events.Backplane.MessagePublished += OnBackplaneMessagePublished;
        cache.Events.Backplane.MessageReceived += OnBackplaneMessageReceived;
        _backplaneEventsSubscribed = true;
    }

    private void OnBackplaneMessagePublished(
        object? sender,
        FusionCacheBackplaneMessageEventArgs args) =>
        RecordBackplaneEvent("published", args);

    private void OnBackplaneMessageReceived(
        object? sender,
        FusionCacheBackplaneMessageEventArgs args) =>
        RecordBackplaneEvent("received", args);

    private void RecordBackplaneEvent(
        string direction,
        FusionCacheBackplaneMessageEventArgs args)
    {
        lock (_backplaneEventsLock)
        {
            _backplaneEvents.Add(new BackplaneEventObservation(
                direction,
                args.Message.SourceId,
                args.Message.Action.ToString(),
                args.Message.CacheKey));
        }
    }
}

internal sealed record HostAuthorizationState(
    long RoleCount,
    long PermissionCount,
    long AssignmentCount);

internal sealed record BackplaneEventObservation(
    string Direction,
    string? SourceId,
    string Action,
    string? CacheKey);

internal sealed record HostTestIdentity(
    Guid UserId,
    string Username,
    string AccessToken);

internal sealed class TestConnectionRemoteIpAddressStartupFilter(
    IPAddress remoteIpAddress) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        application =>
        {
            // 测试入口必须先模拟实际 TCP 对端，随后才让生产转发中间件执行信任判断。
            application.Use(nextMiddleware => async context =>
            {
                context.Connection.RemoteIpAddress = remoteIpAddress;
                await nextMiddleware(context);
            });
            next(application);
        };
}

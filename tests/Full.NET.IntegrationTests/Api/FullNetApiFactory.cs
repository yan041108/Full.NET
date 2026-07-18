using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.IntegrationTests.Api;

internal sealed class FullNetApiFactory(
    DatabaseProvider provider,
    string connectionString) : WebApplicationFactory<Program>
{
    public const string TestPassword = "FullNet!2026Integration";

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseContentRoot(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Hosts",
            "Full.NET.Host.Api"));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{DatabaseOptions.SectionName}:Provider"] = provider.ToString(),
                [$"{DatabaseOptions.SectionName}:ConnectionString"] = connectionString,
                [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
                ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
                ["Identity:AllowedOrigins:0"] = "http://localhost",
                ["Tenancy:HostDomains:0"] = "localhost",
            }));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            using var bootstrapClient = CreateClient();
            await using var scope = Services.CreateAsyncScope();
            var currentTenant = scope.ServiceProvider
                .GetRequiredService<CurrentTenantAccessor>();
            currentTenant.SetHost();
            try
            {
                await scope.ServiceProvider
                    .GetRequiredService<IDatabaseMigrationRunner>()
                    .MigrateAsync(cancellationToken);
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
            }
            finally
            {
                currentTenant.Clear();
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
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
        return new FullNetApiFactory(provider, connectionString);
    }

    public string CreateHostAccessToken(
        IReadOnlyCollection<string> permissions)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new IdentityUser(
            Guid.NewGuid(),
            null,
            "host",
            "limited-admin",
            "LIMITED-ADMIN",
            "受限管理员",
            "unused",
            true,
            0,
            null,
            Guid.NewGuid().ToString("N"),
            now,
            null,
            1);
        return Services.GetRequiredService<IAccessTokenIssuer>()
            .Issue(user, Guid.NewGuid(), null, permissions, false)
            .AccessToken;
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
}

internal sealed record HostAuthorizationState(
    long RoleCount,
    long PermissionCount,
    long AssignmentCount);

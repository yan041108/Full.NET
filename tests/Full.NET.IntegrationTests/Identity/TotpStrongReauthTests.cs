using System.Security.Claims;
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
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.Persistence;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Tenancy;
using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Serialization.MemoryPack;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Full.NET.IntegrationTests.Identity;

/// <summary>
/// 双库验证：TOTP 登记与 Production 合格强认证下的远程超管授予。
/// </summary>
[TestClass]
public sealed class TotpStrongReauthTests
{
    [TestMethod]
    public async Task SqlServer_totp_enrollment_enables_production_grant()
    {
        await VerifyAsync(
            DatabaseProvider.SqlServer,
            await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    }

    [TestMethod]
    public async Task MySql_totp_enrollment_enables_production_grant()
    {
        await VerifyAsync(
            DatabaseProvider.MySql,
            await SharedDatabaseFixture.CreateMySqlDatabaseAsync());
    }

    private static async Task VerifyAsync(
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
                DestructiveDdlApprovalId = "test-totp-strong-reauth-016",
            }),
            MigrationContractOptionFactory.NamingOptions());
        Assert.IsTrue((await migration.MigrateAsync()).Successful);

        await using var services = BuildProductionServices(options);
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<CurrentTenantAccessor>().SetHost();

        var bootstrap = sp.GetRequiredService<IIdentityBootstrapService>();
        var boot = await bootstrap.BootstrapHostAdminAsync(
            new BootstrapHostAdminRequest(
                "totp-admin",
                "FullNet!2026TotpAdmin",
                "TOTP 管理员"));
        Assert.IsTrue(boot.IsSuccess, boot.Error?.Message);

        var query = sp.GetRequiredService<IQueryExecutor>();
        var admin = await query.QuerySingleOrDefaultAsync<IdentityUserRecord>(
            IdentitySql.FindUserByScopeAndUsername,
            new { ScopeKey = "host", NormalizedUsername = "TOTP-ADMIN" });
        Assert.IsNotNull(admin);

        var principal = CreatePrincipal(admin.Id);
        var enrollment = sp.GetRequiredService<TotpEnrollmentService>();
        var begin = await enrollment.BeginAsync(principal);
        Assert.IsTrue(begin.IsSuccess, begin.Error?.Message);

        var key = TotpAlgorithm.DecodeSharedSecret(begin.Value!.SharedSecretBase32);
        var code = TotpAlgorithm.ComputeCode(key, sp.GetRequiredService<IClock>().UtcNow);
        var confirm = await enrollment.ConfirmAsync(principal, code);
        Assert.IsTrue(confirm.IsSuccess, confirm.Error?.Message);
        Assert.IsTrue(confirm.Value!.IsEnabled);

        var hostUsers = sp.GetRequiredService<HostUserManagementService>();
        var created = await hostUsers.CreateAsync(
            new CreateHostUserRequest(
                "totp-target",
                "TOTP 目标用户",
                "FullNet!2026TotpTarget"));
        Assert.IsTrue(created.IsSuccess, created.Error?.Message);

        var management = sp.GetRequiredService<SuperAdministratorManagementService>();
        var missingTotp = await management.GrantAsync(
            principal,
            "totp-target",
            "FullNet!2026TotpAdmin");
        Assert.IsFalse(missingTotp.IsSuccess);
        Assert.AreEqual(IdentityErrorCodes.MfaTotpRequired, missingTotp.Error?.Code);

        var freshCode = TotpAlgorithm.ComputeCode(
            key,
            sp.GetRequiredService<IClock>().UtcNow);
        var grant = await management.GrantAsync(
            principal,
            "totp-target",
            "FullNet!2026TotpAdmin",
            freshCode);
        Assert.IsTrue(grant.IsSuccess, grant.Error?.Message);
        Assert.IsTrue(grant.Value!.Changed);
    }

    private static ServiceProvider BuildProductionServices(DatabaseOptions options)
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
                ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
                ["Identity:EnableTotpStrongReauthentication"] = "true",
                ["Identity:Bootstrap:Username"] = "unused",
                ["Identity:Bootstrap:Password"] = "FullNet!2026Unused!",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddHttpContextAccessor();
        services.AddSingleton<IHostEnvironment>(new ProductionHostEnvironment());
        services.AddScoped<CurrentTenantAccessor>();
        services.AddScoped<ICurrentTenant>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddScoped<ICurrentTenantContextWriter>(provider =>
            provider.GetRequiredService<CurrentTenantAccessor>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.AddSingleton<IApiResultMapper, NonHttpApiResultMapper>();
        services.AddFullNetModularity();
        services.AddFullNetDapper(configuration, "Production");
        services.AddFullNetMemoryPack();
        services.AddFullNetCaching(configuration, "Production");
        services.AddFullNetSeeding(configuration);
        services.AddSingleton<
            ITenantOrganizationUnitDirectory,
            EmptyTenantOrganizationUnitDirectory>();
        services.AddSingleton<
            IIdentityOrganizationUnitDirectory,
            EmptyIdentityOrganizationUnitDirectory>();
        services.AddSingleton<
            IIdentityOrganizationUnitProjectionSource,
            EmptyIdentityOrganizationUnitProjectionSource>();
        services.AddSingleton<
            IIdentityOrganizationPositionDirectory,
            EmptyIdentityOrganizationPositionDirectory>();
        services.AddSingleton<IHostFileUploadWriter, NoOpHostFileUploadWriter>();
        services.AddSingleton<IHostFileReferenceClaimService, NoOpHostFileReferenceClaimService>();
        services.AddSingleton<IHostFileDescriptorReader, NoOpHostFileDescriptorReader>();
        services.AddSingleton<IHostFileContentReader, NoOpHostFileContentReader>();
        services.AddFullNetModule<IdentityModule>(configuration);
        services.AddFullNetModule<TenancyModule>(configuration);
        services.AddFullNetModule<OrganizationModule>(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static ClaimsPrincipal CreatePrincipal(Guid userId) => new(
        new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D"))],
            "integration-test"));

    private sealed class ProductionHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "Full.NET.IntegrationTests.Totp";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class NonHttpApiResultMapper : IApiResultMapper
    {
        public IResult Map<T>(Result<T> result, HttpContext httpContext) =>
            throw new NotSupportedException();

        public IResult MapException(Exception exception, HttpContext httpContext) =>
            throw new NotSupportedException();
    }

    private sealed class EmptyTenantOrganizationUnitDirectory : ITenantOrganizationUnitDirectory
    {
        public Task<TenantOrganizationUnitDirectoryEntry?> FindActiveUnitAsync(
            Guid tenantId,
            Guid unitId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TenantOrganizationUnitDirectoryEntry?>(null);
    }

    private sealed class EmptyIdentityOrganizationUnitDirectory : IIdentityOrganizationUnitDirectory
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

    private sealed class EmptyIdentityOrganizationPositionDirectory
        : IIdentityOrganizationPositionDirectory
    {
        public Task<IdentityOrganizationPositionDirectoryEntry?> FindActivePositionAsync(
            Guid tenantId,
            Guid positionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityOrganizationPositionDirectoryEntry?>(null);
    }

    private sealed class NoOpHostFileUploadWriter : IHostFileUploadWriter
    {
        public Task<Result<HostFileUploadResult>> UploadAsync(
            Guid createdByUserId,
            string originalFileName,
            string contentType,
            Stream content,
            long contentLength,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<HostFileUploadResult>.Failure(new Error(
                "files.upload_not_available",
                "The TOTP fixture does not upload files.",
                ErrorType.Unexpected)));
    }

    private sealed class NoOpHostFileReferenceClaimService : IHostFileReferenceClaimService
    {
        public Task<Result<HostFileReferenceClaimResult>> ClaimAsync(
            HostFileReferenceClaimRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<HostFileReferenceClaimResult>.Failure(new Error(
                "files.claim_not_available",
                "The TOTP fixture does not claim file references.",
                ErrorType.Unexpected)));

        public Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<HostFileReferenceClaimResult>.Failure(new Error(
                "files.claim_not_available",
                "The TOTP fixture does not claim file references.",
                ErrorType.Unexpected)));

        public Task<Result<bool>> ReleaseAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Failure(new Error(
                "files.claim_not_available",
                "The TOTP fixture does not claim file references.",
                ErrorType.Unexpected)));

        public Task<bool> HasOpenClaimsAsync(
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class NoOpHostFileDescriptorReader : IHostFileDescriptorReader
    {
        public Task<HostFileDescriptor?> GetReadyDescriptorAsync(
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<HostFileDescriptor?>(null);
    }

    private sealed class NoOpHostFileContentReader : IHostFileContentReader
    {
        public Task<Result<HostFileContent>> OpenReadyContentAsync(
            Guid fileId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<HostFileContent>.Failure(new Error(
                "files.content_not_available",
                "The TOTP fixture does not open file content.",
                ErrorType.Unexpected)));
    }
}

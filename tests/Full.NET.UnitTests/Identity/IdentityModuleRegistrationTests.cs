using FluentValidation;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Localization;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Configuration;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.DataScope;
using Full.NET.Modules.Identity.DependencyInjection;
using Full.NET.Modules.Identity.Domain;
using Full.NET.Modules.Identity.Features.Bootstrap;
using Full.NET.Modules.Identity.Features.ChangeSessionContext;
using Full.NET.Modules.Identity.Features.GetAuthorizationTree;
using Full.NET.Modules.Identity.Features.GetNavigation;
using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Features.ManageHostApiKeys;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Identity.Features.ManageHostOnlineSessions;
using Full.NET.Modules.Identity.Features.ManageHostRoles;
using Full.NET.Modules.Identity.Features.ManageHostRoleFieldGrants;
using Full.NET.Modules.Identity.Features.ManageHostUsers;
using Full.NET.Modules.Identity.Features.OrganizationUnitProjection;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Features.ManageSuperAdministrators;
using Full.NET.Modules.Identity.Features.ManageTotp;
using Full.NET.Modules.Identity.HostUsers;
using Full.NET.Modules.Identity.Http;
using Full.NET.Modules.Identity.RateLimiting;
using Full.NET.Modules.Identity.Resources;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Identity.Seeding;
using Full.NET.Modules.Identity.Serialization;
using Full.NET.Seeding.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using NSubstitute;
using AspNetPasswordHasher =
    Microsoft.AspNetCore.Identity.PasswordHasher<
        Full.NET.Modules.Identity.Domain.IdentityUser>;
using AspNetPasswordHasherContract =
    Microsoft.AspNetCore.Identity.IPasswordHasher<
        Full.NET.Modules.Identity.Domain.IdentityUser>;
using IdentityFeatures = Full.NET.Modules.Identity.Features;
using IdentityUser = Full.NET.Modules.Identity.Domain.IdentityUser;
using LoginCommand = Full.NET.Modules.Identity.Features.Login.Command;
using LoginHandler = Full.NET.Modules.Identity.Features.Login.Handler;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityModuleRegistrationTests
{
    private static readonly Type[] ModuleOwnedExternalServiceTypes =
    [
        typeof(IClock),
        typeof(IIdGenerator),
        typeof(AspNetPasswordHasherContract),
    ];

    [TestMethod]
    public void Identity_module_delegation_matches_responsibility_registration_pipeline()
    {
        var configuration = new ConfigurationBuilder().Build();
        var moduleServices = new ServiceCollection();
        var splitServices = new ServiceCollection();
        var module = new IdentityModule();

        module.AddServices(moduleServices, configuration);
        module.AddMigrationServices(splitServices, configuration);
        splitServices.AddIdentityAuthentication(configuration);
        splitServices.AddIdentityAuthorization(configuration);
        splitServices.AddIdentityDomainServices(configuration);
        splitServices.AddIdentityHttpPolicies(configuration);
        module.AddBackgroundServices(splitServices, configuration);

        CollectionAssert.AreEqual(
            SnapshotIdentityOwnedRegistrations(moduleServices),
            SnapshotIdentityOwnedRegistrations(splitServices));
        CollectionAssert.AreEqual(
            ExpectedIdentityOwnedRegistrations(),
            SnapshotIdentityOwnedRegistrations(moduleServices));
    }

    [TestMethod]
    public void Identity_background_services_register_organization_kafka_subscription_once()
    {
        // 目标：Worker profile 下恰好有一个 IIntegrationEventSubscription，
        // 三元路由为 (fullnet.identity.organization-unit-projection,
        // fullnet.organization.unit.changed, 1)，且生命周期为 Scoped。
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{IdentityOptions.SectionName}:AllowDevelopmentEphemeralSigningKey"] =
                    "true",
            })
            .Build();
        var services = new ServiceCollection();
        var module = new IdentityModule();
        module.AddServices(services, configuration);

        var subscriptionDescriptors = services
            .Where(d => d.ServiceType == typeof(IIntegrationEventSubscription))
            .ToArray();

        Assert.AreEqual(1, subscriptionDescriptors.Length);
        Assert.AreEqual(ServiceLifetime.Scoped, subscriptionDescriptors[0].Lifetime);
        var implementationType = subscriptionDescriptors[0].ImplementationType;
        Assert.IsNotNull(implementationType);
        StringAssert.EndsWith(
            implementationType.FullName,
            "OrganizationUnitChangedKafkaSubscription");

        // 不能是 LegacyIntegrationEventHandlerSubscriptionAdapter——那是旧轮询路径的适配器
        Assert.AreNotEqual(
            typeof(LegacyIntegrationEventHandlerSubscriptionAdapter),
            implementationType);
        Assert.IsTrue(
            typeof(IIntegrationEventSubscription).IsAssignableFrom(implementationType));

        // ConsumerName/EventType/SchemaVersion/IdempotencyStrategy 在实现类中都是
        // 纯常量 getter（不依赖构造注入的字段）。为避免拉通整条依赖链
        // （IIntegrationEventSerializer/OrganizationUnitProjectionWriter 等），
        // 通过 System.ComponentModel.TypeDescriptor.GetProperties 的弱类型 getter
        // 创建无参代理型未初始化实例并读取属性值——使用 RuntimeHelpers.GetUninitializedObject
        // 跳过构造函数（不跑字段初始化），因为常量属性 getter 不访问字段。
        var uninitialized = System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(implementationType) as IIntegrationEventSubscription;
        Assert.IsNotNull(uninitialized);

        Assert.AreEqual(
            "fullnet.identity.organization-unit-projection",
            uninitialized.ConsumerName);
        Assert.AreEqual(
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            uninitialized.EventType);
        Assert.AreEqual(1, uninitialized.SchemaVersion);
        Assert.AreEqual(
            IntegrationEventIdempotencyStrategy.NaturallyIdempotent,
            uninitialized.IdempotencyStrategy);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IIntegrationEventHandlerRegistry>();
        Assert.IsTrue(registry.TryResolve(
            IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
            1,
            "fullnet.identity.organization-unit-projection",
            out var descriptor));
        Assert.AreEqual(implementationType, descriptor.HandlerType);
    }

    [TestMethod]
    public async Task Identity_module_repeated_registration_preserves_critical_contracts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{IdentityOptions.SectionName}:AllowDevelopmentEphemeralSigningKey"] =
                    "true",
            })
            .Build();
        var services = new ServiceCollection();
        var module = new IdentityModule();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        services.AddSingleton(environment);
        services.AddLogging();

        module.AddServices(services, configuration);
        module.AddServices(services, configuration);

        CollectionAssert.AreEqual(
            ExpectedIdentityOwnedRegistrations(),
            SnapshotIdentityOwnedRegistrations(services));

        using var provider = services.BuildServiceProvider();
        var authentication = provider
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;
        Assert.AreEqual(
            SmartAuthenticationDefaults.AuthenticationScheme,
            authentication.DefaultAuthenticateScheme);
        Assert.AreEqual(
            JwtBearerDefaults.AuthenticationScheme,
            authentication.DefaultChallengeScheme);

        var schemes = (await provider
                .GetRequiredService<IAuthenticationSchemeProvider>()
                .GetAllSchemesAsync())
            .OrderBy(scheme => scheme.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.HasCount(4, schemes);
        AssertScheme<ApiKeyAuthenticationHandler>(
            schemes,
            ApiKeyAuthenticationDefaults.AuthenticationScheme);
        AssertScheme<JwtBearerHandler>(
            schemes,
            JwtBearerDefaults.AuthenticationScheme);
        AssertScheme<PolicySchemeHandler>(
            schemes,
            SmartAuthenticationDefaults.AuthenticationScheme);
        AssertScheme<SignatureAuthenticationHandler>(
            schemes,
            SignatureAuthenticationDefaults.AuthenticationScheme);

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.IsFalse(jwt.MapInboundClaims);
        Assert.AreEqual(typeof(FullNetJwtBearerEvents), jwt.EventsType);
        Assert.IsTrue(jwt.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.IsTrue(jwt.TokenValidationParameters.ValidateIssuer);
        Assert.AreEqual(
            "Full.NET",
            jwt.TokenValidationParameters.ValidIssuer);
        Assert.IsTrue(jwt.TokenValidationParameters.ValidateAudience);
        Assert.AreEqual(
            "Full.NET.Api",
            jwt.TokenValidationParameters.ValidAudience);
        Assert.IsTrue(jwt.TokenValidationParameters.ValidateLifetime);
        Assert.IsTrue(jwt.TokenValidationParameters.RequireExpirationTime);
        Assert.IsTrue(jwt.TokenValidationParameters.RequireSignedTokens);
        Assert.AreEqual(
            TimeSpan.FromSeconds(30),
            jwt.TokenValidationParameters.ClockSkew);
        Assert.AreEqual(
            JwtRegisteredClaimNames.Name,
            jwt.TokenValidationParameters.NameClaimType);
        Assert.IsTrue(jwt.TokenValidationParameters.IssuerSigningKeys.Any());

        var cors = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        Assert.IsNotNull(cors.GetPolicy(IdentityModule.BrowserCorsPolicy));

        var json = provider.GetRequiredService<IOptions<JsonOptions>>().Value;
        Assert.AreSame(
            IdentityJsonSerializerContext.Default,
            json.SerializerOptions.TypeInfoResolverChain[0]);
        Assert.AreEqual(
            1,
            json.SerializerOptions.TypeInfoResolverChain.Count(
                resolver => ReferenceEquals(
                    resolver,
                    IdentityJsonSerializerContext.Default)));

        var overrideServices = new ServiceCollection();
        overrideServices.AddSingleton(Substitute.For<IApiResultMapper>());
        module.AddServices(overrideServices, configuration);
        overrideServices.AddSingleton(
            Substitute.For<IAuthorizationPolicyProvider>());
        overrideServices.AddSingleton(
            Substitute.For<IAuthorizationMiddlewareResultHandler>());
        module.AddServices(overrideServices, configuration);

        using var overrideProvider = overrideServices.BuildServiceProvider();
        Assert.IsInstanceOfType<FullNetPermissionPolicyProvider>(
            overrideProvider.GetRequiredService<IAuthorizationPolicyProvider>());
        Assert.IsInstanceOfType<FullNetAuthorizationResultHandler>(
            overrideProvider.GetRequiredService<
                IAuthorizationMiddlewareResultHandler>());
    }

    private static RegistrationExpectation[] ExpectedIdentityOwnedRegistrations() =>
    [
        RegistrationExpectation.Type<
            IValidateOptions<IdentityOptions>,
            IdentityOptionsValidator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IValidateOptions<SignatureAuthenticationOptions>,
            SignatureAuthenticationOptionsValidator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IClock,
            Full.NET.Abstractions.Time.SystemClock>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IIdGenerator,
            GuidV7IdGenerator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            AspNetPasswordHasherContract,
            AspNetPasswordHasher>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IIdentityBootstrapService,
            IdentityBootstrapService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IAuthorizationCatalogContributor,
            IdentityAuthorizationContributor>(ServiceLifetime.Singleton),
        RegistrationExpectation.Factory<AuthorizationCatalog>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Self<HostNavigationCatalogSyncService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IDataSeedContributor,
            HostAdministratorSeedContributor>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IDataSeedContributor,
            HostNavigationCatalogSeedContributor>(ServiceLifetime.Scoped),

        RegistrationExpectation.Self<
            IdentityAuthenticationRegistrationMarker>(ServiceLifetime.Singleton),
        RegistrationExpectation.Self<AccessSessionValidator>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<FullNetJwtBearerEvents>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<TotpSecretProtector>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IStrongReauthenticationProvider,
            PasswordReauthenticationProvider>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<TotpEnrollmentService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<ApiKeyAuthenticationService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<SignatureAuthenticationService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<RsaSigningKeyRing>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IRandomTokenGenerator,
            CryptographicTokenGenerator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IAccessTokenIssuer,
            JwtAccessTokenIssuer>(ServiceLifetime.Singleton),
        RegistrationExpectation.Self<ApiKeyAuthenticationHandler>(
            ServiceLifetime.Transient),
        RegistrationExpectation.Self<SignatureAuthenticationHandler>(
            ServiceLifetime.Transient),

        RegistrationExpectation.Self<PermissionClaimEvaluator>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IPermissionSnapshotReader,
            PermissionSnapshotReader>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IIdentitySessionContextService,
            IdentitySessionContextService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<NavigationProjector>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Self<AuthorizationTreeProjector>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IAuthorizationHandler,
            FullNetPermissionHandler>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IHostedService,
            AuthorizationCatalogValidator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IUserDataScopeResolver,
            UserDataScopeResolver>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<RoleDataScopeProjection>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IDataScopeSqlFilterBuilder,
            DataScopeSqlFilterBuilder>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IAuthorizationPolicyProvider,
            FullNetPermissionPolicyProvider>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IAuthorizationMiddlewareResultHandler,
            FullNetAuthorizationResultHandler>(ServiceLifetime.Singleton),

        RegistrationExpectation.Type<
            IErrorResourceSource,
            IdentityErrorResourceSource>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            ISuperAdministratorService,
            SuperAdministratorService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<SuperAdministratorManagementService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<SuperAdministratorQueryService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostUserQueryService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostUserManagementService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostUserRolesService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostRoleQueryService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostRoleManagementService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostRoleDataScopeService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Factory<FieldProjectionCatalog>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IUserFieldProjectionResolver,
            UserFieldProjectionResolver>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostRoleFieldGrantService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostMenuQueryService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostMenuPermissionOptionsQueryService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostMenuManagementService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostOnlineSessionQueryService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostOnlineSessionManagementService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostApiKeyQueryService>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostApiKeyManagementService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<
            IdentityFeatures.QueryHostModuleCatalog.HostModuleCatalogQueryService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<
            IdentityFeatures.GetHostDashboardSummary.HostDashboardQueryService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostUserDirectory>(ServiceLifetime.Scoped),
        RegistrationExpectation.Factory<IHostUserDirectory>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Factory<IHostUserDisplayDirectory>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostUserSelectionDirectory>(ServiceLifetime.Scoped),
        RegistrationExpectation.Factory<IHostUserSelectionDirectory>(ServiceLifetime.Scoped),
        RegistrationExpectation.Factory<IHostUserBatchSelectionDirectory>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            ITenantUserSelectionDirectory,
            TenantUserSelectionDirectory>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<HostNavigationDefinitionLoader>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IValidator<LoginCommand>,
            LoginCommandValidator>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IValidator<IdentityFeatures.UpdateLocale.Command>,
            IdentityFeatures.UpdateLocale.Validator>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            ICommandHandler<LoginCommand, LoginSessionResult>,
            LoginHandler>(ServiceLifetime.Scoped),
        RegistrationExpectation.Self<IdentityCookieWriter>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            ICommandHandler<
                IdentityFeatures.RefreshSession.Command,
                IdentityFeatures.RefreshSession.RefreshSessionResult>,
            IdentityFeatures.RefreshSession.Handler>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            ICommandHandler<
                IdentityFeatures.Logout.Command,
                IdentityFeatures.Logout.LogoutResult>,
            IdentityFeatures.Logout.Handler>(ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            ICommandHandler<
                IdentityFeatures.UpdateLocale.Command,
                LocalePreferenceResponse>,
            IdentityFeatures.UpdateLocale.Handler>(ServiceLifetime.Scoped),

        RegistrationExpectation.Self<AllowedOriginValidator>(
            ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IConfigureOptions<CorsOptions>,
            IdentityCorsOptionsConfigurator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IConfigureOptions<RateLimiterOptions>,
            IdentityRateLimiterPolicyConfigurator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IConfigureOptions<RateLimitPolicyErrorCodes>,
            IdentityRateLimiterPolicyConfigurator>(ServiceLifetime.Singleton),
        RegistrationExpectation.Type<
            IConfigureOptions<JsonOptions>,
            IdentityHttpJsonOptionsConfigurator>(ServiceLifetime.Singleton),

        RegistrationExpectation.Self<OrganizationUnitProjectionWriter>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<OrganizationUnitProjectionDirectory>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Factory<IOrganizationUnitProjectionDirectory>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<OrganizationUnitProjectionBackfillService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Self<OrganizationUnitProjectionReconciliationService>(
            ServiceLifetime.Scoped),
        RegistrationExpectation.Type<
            IIntegrationEventHandler,
            OrganizationUnitChangedIntegrationEventHandler>(
            ServiceLifetime.Scoped),
        // Kafka 订阅实现类在独立实现步骤中创建，此处用运行时反射断言保证类名与命名空间正确。
        new RegistrationExpectation(
            typeof(IIntegrationEventSubscription),
            ServiceLifetime.Scoped,
            RegistrationKind.Type,
            typeof(IdentityModule).Assembly.GetType(
                "Full.NET.Modules.Identity.Features.OrganizationUnitProjection.OrganizationUnitChangedKafkaSubscription")),
        new RegistrationExpectation(
            typeof(IIntegrationEventHandlerRegistry),
            ServiceLifetime.Singleton,
            RegistrationKind.Type,
            typeof(IdentityModule).Assembly.GetType(
                "Full.NET.Generated.IntegrationEventHandlerRegistry")),
    ];

    private static RegistrationExpectation[] SnapshotIdentityOwnedRegistrations(
        IEnumerable<ServiceDescriptor> services) =>
        services
            .Where(IsIdentityOwnedRegistration)
            .Select(RegistrationExpectation.FromDescriptor)
            .ToArray();

    private static bool IsIdentityOwnedRegistration(ServiceDescriptor descriptor)
    {
        var identityAssembly = typeof(IdentityModule).Assembly;
        return descriptor.ImplementationType?.Assembly == identityAssembly
            || descriptor.ImplementationInstance?.GetType().Assembly
            == identityAssembly
            || descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly
            == identityAssembly
            || ModuleOwnedExternalServiceTypes.Contains(descriptor.ServiceType);
    }

    private static void AssertScheme<THandler>(
        IEnumerable<AuthenticationScheme> schemes,
        string name)
    {
        var scheme = schemes.Single(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal));
        Assert.AreEqual(typeof(THandler), scheme.HandlerType);
    }

    private enum RegistrationKind
    {
        Type,
        Factory,
        Instance,
    }

    private sealed record RegistrationExpectation(
        Type ServiceType,
        ServiceLifetime Lifetime,
        RegistrationKind Kind,
        Type? ImplementationType)
    {
        internal static RegistrationExpectation Type<TService, TImplementation>(
            ServiceLifetime lifetime) =>
            new(
                typeof(TService),
                lifetime,
                RegistrationKind.Type,
                typeof(TImplementation));

        internal static RegistrationExpectation Self<TService>(
            ServiceLifetime lifetime) =>
            Type<TService, TService>(lifetime);

        internal static RegistrationExpectation Factory<TService>(
            ServiceLifetime lifetime) =>
            new(
                typeof(TService),
                lifetime,
                RegistrationKind.Factory,
                null);

        internal static RegistrationExpectation FromDescriptor(
            ServiceDescriptor descriptor)
        {
            if (descriptor.ImplementationType is not null)
            {
                return new(
                    descriptor.ServiceType,
                    descriptor.Lifetime,
                    RegistrationKind.Type,
                    descriptor.ImplementationType);
            }

            if (descriptor.ImplementationFactory is not null)
            {
                return new(
                    descriptor.ServiceType,
                    descriptor.Lifetime,
                    RegistrationKind.Factory,
                    null);
            }

            return new(
                descriptor.ServiceType,
                descriptor.Lifetime,
                RegistrationKind.Instance,
                descriptor.ImplementationInstance?.GetType());
        }
    }
}

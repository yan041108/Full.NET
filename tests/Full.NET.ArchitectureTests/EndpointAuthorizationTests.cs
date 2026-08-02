using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MessagePack;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class EndpointAuthorizationTests
{
    [TestMethod]
    public void Api_v1_endpoints_explicitly_declare_authorization_or_anonymous_intent()
    {
        using var app = BuildApiApplication();

        var missingIntentEndpoints = CollectApiV1Endpoints(app)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
                && !endpoint.Metadata.OfType<IAuthorizeData>().Any())
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (missingIntentEndpoints.Length > 0)
        {
            Assert.Fail(
                "下列 API Endpoint 没有显式声明 RequireAuthorization(...) 或 AllowAnonymous(): "
                + string.Join(", ", missingIntentEndpoints));
        }
    }

    [TestMethod]
    public void Api_v1_endpoints_do_not_reference_unknown_fullnet_permissions()
    {
        using var app = BuildApiApplication();
        var knownPermissions = app.Services
            .GetServices<IAuthorizationCatalogContributor>()
            .SelectMany(contributor => contributor.Permissions)
            .Select(permission => permission.Code)
            .ToHashSet(StringComparer.Ordinal);

        var unknownPermissionEndpoints = CollectApiV1Endpoints(app)
            .SelectMany(endpoint => endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .SelectMany(authorizeData => ExtractPermissionCodes(authorizeData.Policy))
                .Select(permissionCode => (Route: endpoint.RoutePattern.RawText, permissionCode)))
            .Where(pair => !knownPermissions.Contains(pair.permissionCode))
            .Select(pair => $"{pair.Route} -> {pair.permissionCode}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (unknownPermissionEndpoints.Length > 0)
        {
            Assert.Fail(
                "下列 API Endpoint 引用了未登记权限: "
                + string.Join(", ", unknownPermissionEndpoints));
        }
    }

    [TestMethod]
    public void CollectUnknownPermissionCodes_reports_test_only_unknown_permission()
    {
        var knownPermissions = new HashSet<string>(StringComparer.Ordinal)
        {
            "identity.users.read",
        };

        var unknownCodes = ExtractPermissionCodes(
                FullNetPermissionPolicies.For("unknown.permission"))
            .Where(code => !knownPermissions.Contains(code))
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "unknown.permission" },
            unknownCodes);
    }

    [TestMethod]
    public void Api_v1_endpoints_do_not_bind_retired_identity_users_write()
    {
        using var app = BuildApiApplication();

        var violations = CollectPermissionBindings(app)
            .Where(binding => string.Equals(
                binding.PermissionCode,
                IdentityUserManagementPermissions.Write,
                StringComparison.Ordinal))
            .Select(binding => $"{binding.HttpMethod} {binding.Route} -> {binding.PermissionCode}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (violations.Length > 0)
        {
            Assert.Fail(
                "identity.users.write 已退役，下列 Endpoint 仍绑定该权限: "
                + string.Join(", ", violations));
        }
    }

    [TestMethod]
    public void Api_v1_coarse_write_bindings_match_frozen_inventory_allowlist()
    {
        using var app = BuildApiApplication();

        var currentBindings = CollectPermissionBindings(app)
            .Where(binding => LegacyCoarseActionPermissionRegistry.IsCoarseWritePermission(
                binding.PermissionCode))
            .Select(binding => binding.ToAllowlistKey())
            .ToHashSet(StringComparer.Ordinal);

        var unexpected = currentBindings
            .Where(key => !LegacyCoarseActionPermissionRegistry.AllowedBindings.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
        var stale = LegacyCoarseActionPermissionRegistry.AllowedBindings
            .Where(key => !currentBindings.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        if (unexpected.Length > 0 || stale.Length > 0)
        {
            Assert.Fail(
                "粗粒度 .write 绑定与冻结清单不一致。"
                + (unexpected.Length > 0
                    ? " 新增或未登记: " + string.Join(", ", unexpected)
                    : string.Empty)
                + (stale.Length > 0
                    ? " 清单陈旧: " + string.Join(", ", stale)
                    : string.Empty));
        }
    }

    private static IEnumerable<EndpointPermissionBinding> CollectPermissionBindings(
        WebApplication app) =>
        CollectApiV1Endpoints(app)
            .SelectMany(endpoint =>
            {
                var route = endpoint.RoutePattern.RawText ?? string.Empty;
                var methods = endpoint.Metadata
                    .GetMetadata<HttpMethodMetadata>()
                    ?.HttpMethods;
                if (methods is null || methods.Count == 0)
                {
                    methods = ["GET"];
                }

                var permissionCodes = endpoint.Metadata
                    .GetOrderedMetadata<IAuthorizeData>()
                    .SelectMany(authorizeData => ExtractPermissionCodes(authorizeData.Policy))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (permissionCodes.Length == 0)
                {
                    return [];
                }

                return methods.SelectMany(method =>
                    permissionCodes.Select(permissionCode =>
                        new EndpointPermissionBinding(method, route, permissionCode)));
            });

    private sealed record EndpointPermissionBinding(
        string HttpMethod,
        string Route,
        string PermissionCode)
    {
        public string ToAllowlistKey() =>
            $"{HttpMethod.ToUpperInvariant()} {Route}|{PermissionCode}";
    }

    private static WebApplication BuildApiApplication()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Testing";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{DatabaseOptions.SectionName}:Provider"] = DatabaseProvider.SqlServer.ToString(),
            [$"{DatabaseOptions.SectionName}:ConnectionString"] =
                "Server=127.0.0.1,1;Database=fullnet_architecture;User Id=sa;Password=FullNet_Test!123;TrustServerCertificate=True;Connect Timeout=1",
            [$"{DatabaseOptions.SectionName}:CommandTimeoutSeconds"] = "30",
            [$"{DatabaseOptions.SectionName}:MySqlGuidStorageMode"] =
                MySqlGuidStorageMode.Binary16.ToString(),
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
            ["Identity:EnableRemoteSuperAdministratorManagement"] = "true",
            ["Identity:AllowedOrigins:0"] = "http://localhost",
            ["Tenancy:HostDomains:0"] = "localhost",
        });
        builder.AddFullNetServiceDefaults();
        builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetDatabaseSchemaModeGuard();
        builder.Services.AddFullNetMessagePack();
        builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetRealtimeSignalR(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        builder.Services.AddFullNetApplicationModules(
            builder.Configuration,
            FullNetHostProfile.Api);

        var app = builder.Build();
        app.MapFullNetHealthEndpoints();
        app.MapFullNetRealtime();
        app.MapFullNetModules();
        return app;
    }

    private static IEnumerable<RouteEndpoint> CollectApiV1Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/v1/",
                StringComparison.Ordinal) == true);

    private static IEnumerable<string> ExtractPermissionCodes(string? policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
        {
            yield break;
        }

        foreach (var segment in policyName.Split(
                     ',',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (FullNetPermissionPolicies.TryRead(segment, out var permissionCode))
            {
                yield return permissionCode;
                continue;
            }

            const string openAccessPrefix = "FullNet.OpenAccess:";
            if (segment.StartsWith(openAccessPrefix, StringComparison.Ordinal))
            {
                yield return segment[openAccessPrefix.Length..];
            }
        }
    }
}

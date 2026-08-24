using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Data.MySql;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MemoryPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class ModuleSelectionEndpointTests
{
    [TestMethod]
    public void Disabled_module_does_not_register_production_endpoints()
    {
        using var app = BuildApiApplication(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Enabled:0"] = "Identity",
            ["FullNet:Modules:Enabled:1"] = "Tenancy",
            ["FullNet:Modules:Enabled:2"] = "Settings",
            ["FullNet:Modules:Enabled:3"] = "Organization",
        });

        var routes = CollectApiV1Routes(app);
        Assert.IsTrue(routes.Any(route => route.Contains("/organization/", StringComparison.Ordinal)));
        Assert.IsFalse(
            routes.Any(route => route.Contains("/document/", StringComparison.Ordinal)),
            "Document 未启用时不应暴露 /api/v1/document/* Endpoint。");
        Assert.IsFalse(
            routes.Any(route => route.Contains("/messaging/", StringComparison.Ordinal)),
            "Messaging 未启用时不应暴露 /api/v1/messaging/* Endpoint。");
    }

    [TestMethod]
    public void Platform_preset_excludes_content_and_codegen_endpoints()
    {
        using var app = BuildApiApplication(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Platform,
        });

        var routes = CollectApiV1Routes(app);
        Assert.IsTrue(routes.Any(route => route.Contains("/messaging/", StringComparison.Ordinal)));
        Assert.IsTrue(routes.Any(route => route.Contains("/jobs/", StringComparison.Ordinal)));
        Assert.IsFalse(
            routes.Any(route => route.Contains("/document/", StringComparison.Ordinal)),
            "Platform 预设不应暴露 Document Endpoint。");
        Assert.IsFalse(
            routes.Any(route => route.Contains("/code-generation/", StringComparison.Ordinal)),
            "Platform 预设不应暴露 CodeGeneration Endpoint。");
    }

    [TestMethod]
    public void Content_preset_excludes_codegen_endpoints()
    {
        using var app = BuildApiApplication(new Dictionary<string, string?>
        {
            ["FullNet:Modules:Preset"] = FullNetModuleSelectionOptions.Presets.Content,
        });

        var routes = CollectApiV1Routes(app);
        Assert.IsTrue(routes.Any(route => route.Contains("/document/", StringComparison.Ordinal)));
        Assert.IsTrue(routes.Any(route => route.Contains("/files/", StringComparison.Ordinal)));
        Assert.IsFalse(
            routes.Any(route => route.Contains("/code-generation/", StringComparison.Ordinal)),
            "Content 预设不应暴露 CodeGeneration Endpoint。");
    }

    private static WebApplication BuildApiApplication(
        IReadOnlyDictionary<string, string?> extraConfiguration)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = "Testing";
        var configuration = new Dictionary<string, string?>
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
        };
        foreach (var entry in extraConfiguration)
        {
            configuration[entry.Key] = entry.Value;
        }

        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddFullNetServiceDefaults();
        builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
        builder.Services.AddFullNetDatabaseSchemaModeGuard();
        builder.Services.AddFullNetMemoryPack();
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

    private static string[] CollectApiV1Routes(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .Where(route => route.StartsWith("/api/v1/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();
}

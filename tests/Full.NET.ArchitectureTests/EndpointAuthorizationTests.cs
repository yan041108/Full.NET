using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Modularity.Modules;
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
        builder.Services.AddFullNetApplicationModules(
            builder.Configuration,
            FullNetHostProfile.Api);

        var app = builder.Build();
        app.MapFullNetHealthEndpoints();
        app.MapFullNetModules();

        var missingIntentEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/v1/",
                StringComparison.Ordinal) == true)
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
}

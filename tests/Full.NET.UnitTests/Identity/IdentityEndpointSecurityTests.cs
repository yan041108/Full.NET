using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class IdentityEndpointSecurityTests
{
    [TestMethod]
    public void Refresh_and_logout_commands_require_the_shared_transaction_boundary()
    {
        var client = new ClientRequestContext("127.0.0.1", "unit-test");

        Assert.IsInstanceOfType<ITransactionalCommand>(
            new Modules.Identity.Features.RefreshSession.Command("refresh", client));
        Assert.IsInstanceOfType<ITransactionalCommand>(
            new Modules.Identity.Features.Logout.Command("refresh", client));
    }

    [TestMethod]
    public async Task Refresh_and_logout_endpoints_require_session_mutation_rate_limiting()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Environment.EnvironmentName = Environments.Development;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:AllowDevelopmentEphemeralSigningKey"] = "true",
        });
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        builder.Services.AddSingleton(Substitute.For<IQueryExecutor>());
        builder.Services.AddFullNetModularity();
        builder.Services.AddFullNetRateLimiter(builder.Configuration);
        builder.Services.AddFullNetModule<IdentityModule>(builder.Configuration);
        await using var app = builder.Build();
        app.MapFullNetModules();

        var rateLimiterOptions = app.Services
            .GetRequiredService<IOptions<RateLimiterOptions>>()
            .Value;
        Assert.AreEqual(
            StatusCodes.Status429TooManyRequests,
            rateLimiterOptions.RejectionStatusCode);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is
                "/api/v1/auth/refresh" or "/api/v1/auth/logout")
            .ToArray();

        Assert.HasCount(2, endpoints);
        foreach (var endpoint in endpoints)
        {
            var rateLimit = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            Assert.IsNotNull(rateLimit, endpoint.RoutePattern.RawText);
            Assert.AreEqual("identity-session-mutation", rateLimit.PolicyName);
        }
    }
}

using Full.NET.Hosting.Api;
using Full.NET.Modules.CodeGeneration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationEndpointSecurityTests
{
    [TestMethod]
    public async Task Apply_endpoint_requires_independent_apply_permission()
    {
        var builder = WebApplication.CreateBuilder();
        var module = new CodeGenerationModule();
        module.AddServices(builder.Services, builder.Configuration);
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build();
        module.MapEndpoints(app);

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText
                == "/api/v1/code-generation/runs/apply");
        var authorization = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>();

        Assert.HasCount(1, authorization);
        Assert.AreEqual(
            FullNetPermissionPolicies.For(
                CodeGenerationRunPermissions.Apply),
            authorization[0].Policy);
    }
}

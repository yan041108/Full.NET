using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.OpenApi;
using Full.NET.Modules.CodeGeneration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>以真实 Endpoint 与平台 OpenAPI 管道导出本模块契约，避免手工推测嵌套 Schema。</summary>
[TestClass]
public sealed class CodeGenerationOpenApiContractTests
{
    [TestMethod]
    public async Task Runtime_contract_exposes_frozen_relationships_and_independent_publication()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddFullNetOpenApi();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(Substitute.For<IQueryExecutor>());
        builder.Services.AddSingleton(Substitute.For<ICommandExecutor>());
        builder.Services.AddSingleton(Substitute.For<ICommandTransaction>());
        builder.Services.AddSingleton(Substitute.For<IMultiResultQueryExecutor>());
        builder.Services.AddSingleton(Substitute.For<IDatabaseSessionLock>());
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IIdGenerator, GuidV7IdGenerator>();
        var module = new CodeGenerationModule();
        module.AddServices(builder.Services, builder.Configuration);
        await using var app = builder.Build();
        module.MapEndpoints(app);
        app.MapFullNetOpenApi();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var json = await client.GetStringAsync(FullNetOpenApiExtensions.OpenApiJsonPath);
        using var document = JsonDocument.Parse(json);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var relationship = schemas.GetProperty("CodeGenerationRelationshipRequest").GetProperty("properties");
        Assert.IsTrue(relationship.TryGetProperty("principalSchema", out _));
        Assert.IsTrue(relationship.TryGetProperty("dependentSchema", out _));
        foreach (var name in new[] { "CodeGenerationRunApplyResponse", "CodeGenerationRunRollbackResponse", "CodeGenerationRunResponse" })
            Assert.IsTrue(schemas.GetProperty(name).GetProperty("properties").TryGetProperty("publicationStatus", out _), name);
        Assert.IsTrue(document.RootElement.GetProperty("paths").TryGetProperty("/api/v1/code-generation/runs/{runId}/publication/retry", out _));
        var export = Environment.GetEnvironmentVariable("FULLNET_CODEGEN_OPENAPI_EXPORT_PATH");
        if (!string.IsNullOrWhiteSpace(export)) await File.WriteAllTextAsync(export, json);
    }
}

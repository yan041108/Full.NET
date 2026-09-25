using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Forwarding;
using Full.NET.Hosting.Observability;
using Full.NET.Hosting.OpenApi;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Hosting.Security;
using Full.NET.Localization;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Modules;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MemoryPack;
using Scalar.AspNetCore;

// 应用拥有自己的启动入口；框架模块由随应用分发的 Composition 装配。
var builder = WebApplication.CreateBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddFullNetTrustedProxyForwarding(builder.Configuration);
builder.Services.AddFullNetOpenApi();
builder.Services.AddFullNetRateLimiter(builder.Configuration);
builder.Services.AddFullNetDapper(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMemoryPack();
builder.Services.AddFullNetKafkaReplayOperations(builder.Configuration);
builder.Services.AddFullNetCaching(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetRealtimeSignalR(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddFullNetApplicationModules(builder.Configuration, FullNetHostProfile.Api);

var app = builder.Build();
app.UseFullNetTrustedProxyForwarding();
app.UseFullNetLocalization();
app.UseFullNetRequestLogging();
app.UseExceptionHandler();
app.UseCors(FullNetModuleCatalog.BrowserCorsPolicy);
app.UseRateLimiter();
app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthentication);
app.UseAuthentication();
app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthorization);
app.UseAuthorization();
app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeEndpoints);
app.MapFullNetOpenApi();
app.MapScalarApiReference(options =>
{
    options
        .WithTitle(FullNetOpenApiExtensions.ApiTitle)
        .WithOpenApiRoutePattern(FullNetOpenApiExtensions.OpenApiRoutePattern);
});
app.MapFullNetHealthEndpoints();
app.MapFullNetRealtime();
app.MapFullNetModules();
app.Run();

/// <summary>
/// 应用拥有的 API 启动入口，可在保持模块边界与中间件顺序的前提下扩展。
/// </summary>
public partial class Program;

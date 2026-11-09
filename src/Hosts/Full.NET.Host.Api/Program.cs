// HTTP API 宿主入口；组合模块、OpenAPI、Dapper、缓存与 Native AOT 闭包。
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Api;
using Full.NET.Hosting.Forwarding;
using Full.NET.Hosting.Observability;
using Full.NET.Hosting.OpenApi;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Hosting.Security;
using Full.NET.Host.Api;
using Full.NET.Localization;
using Full.NET.Logging.Kafka;
using Full.NET.Modularity.Modules;
using Full.NET.Realtime.SignalR;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddFullNetServiceDefaults(KafkaLogSnapshotExporter.Create);
builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddFullNetTrustedProxyForwarding(builder.Configuration);
builder.Services.AddFullNetOpenApi();
builder.Services.AddFullNetRateLimiter(builder.Configuration);
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
HostApiServiceRegistration.AddIntegrationEventSerialization(builder.Services);
HostApiServiceRegistration.AddKafkaReplayOperations(
    builder.Services,
    builder.Configuration);
builder.Services.AddFullNetCaching(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetRealtimeSignalR(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetApplicationModules(
    builder.Configuration,
    FullNetHostProfile.Api);

var app = builder.Build();
app.UseFullNetApiPipeline();
var exposeApiDocumentation = app.Configuration.GetValue<bool?>("OpenApi:Enabled")
    ?? (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"));
if (exposeApiDocumentation)
{
    // 生产默认不映射运行时文档；保留开发和测试生成所需的服务注册。
    app.MapFullNetOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
            .WithTitle(FullNetOpenApiExtensions.ApiTitle)
            .WithOpenApiRoutePattern(FullNetOpenApiExtensions.OpenApiRoutePattern);
    });
}
app.MapFullNetHealthEndpoints();
app.MapFullNetRealtime();
app.MapFullNetModules();
app.Run();

/// <summary>
/// Full.NET API 宿主入口；按固定顺序完成装配与启动，模块装配只能经组合根完成。
/// </summary>
/// <remarks>
/// 启动顺序：
/// <list type="number">
/// <item><c>AddFullNetServiceDefaults</c>：基础观测与 ServiceDefaults；</item>
/// <item>BuildingBlocks：DataProtection、Forwarding、OpenApi、RateLimiter、Dapper、MemoryPack、Kafka、Caching、SignalR；</item>
/// <item><c>AddFullNetApplicationModules</c> 以 <see cref="FullNetHostProfile.Api"/> 装配模块并物化只读目录；</item>
/// <item>审计外层收尾 → 异常映射 → BeforeAuthentication → Authentication → BeforeAuthorization → Authorization → BeforeEndpoints；</item>
/// <item>Endpoints：OpenApi/Scalar、Health、Realtime Hub 与模块 Endpoint。</item>
/// </list>
/// </remarks>
public partial class Program;

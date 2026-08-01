using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Forwarding;
using Full.NET.Hosting.Observability;
using Full.NET.Hosting.OpenApi;
using Full.NET.Hosting.RateLimiting;
using Full.NET.Hosting.Security;
using Full.NET.Localization;
using Full.NET.Modularity.Modules;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MessagePack;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddFullNetTrustedProxyForwarding(builder.Configuration);
builder.Services.AddFullNetOpenApi();
builder.Services.AddFullNetRateLimiter(builder.Configuration);
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMessagePack();
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

public partial class Program;

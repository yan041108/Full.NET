using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Localization;
using Full.NET.Modularity.Modules;
using Full.NET.Serialization.MessagePack;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMessagePack();
builder.Services.AddFullNetCaching(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetApplicationModules(
    builder.Configuration,
    FullNetHostProfile.Api);

var app = builder.Build();
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
app.MapOpenApi();
app.MapScalarApiReference();
app.MapFullNetHealthEndpoints();
app.MapFullNetModules();
app.Run();

public partial class Program;

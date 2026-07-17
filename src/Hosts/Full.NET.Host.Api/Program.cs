using Full.NET.Caching.Fusion;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Migrations.DbUp;
using Full.NET.Modularity.Messaging;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
using Full.NET.Serialization.MessagePack;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddFullNetModularity();
builder.Services.AddFullNetDapper(builder.Configuration);
builder.Services.AddFullNetMessagePack();
builder.Services.AddFullNetCaching(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetMigrations();
builder.Services.AddFullNetModule<IdentityModule>(builder.Configuration);
builder.Services.AddFullNetModule<TenancyModule>(builder.Configuration);

var app = builder.Build();
app.UseFullNetRequestLogging();
app.UseExceptionHandler();
app.UseFullNetTenancy();
app.UseCors(IdentityModule.BrowserCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();
app.MapScalarApiReference();
app.MapFullNetHealthEndpoints();
app.MapFullNetModules();
app.Run();

public partial class Program;

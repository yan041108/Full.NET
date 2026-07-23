using Full.NET.Abstractions.Messaging;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Host.Worker;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMessagePack();
builder.Services.AddFullNetCaching(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddOptions<OutboxWorkerOptions>()
    .Bind(builder.Configuration.GetSection(OutboxWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<OutboxWorkerOptions>,
    OutboxWorkerOptionsValidator>();
builder.Services.AddFullNetApplicationModules(
    builder.Configuration,
    FullNetHostProfile.Worker);
builder.Services.AddHostedService<OutboxProcessor>();

var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
        scope.ServiceProvider.GetServices<IIntegrationEventHandler>());
}

await host.RunAsync();

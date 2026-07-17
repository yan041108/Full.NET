using Full.NET.Caching.Fusion;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Host.Worker;
using Full.NET.Modules.Tenancy;
using Full.NET.Serialization.MessagePack;

var builder = Host.CreateApplicationBuilder(args);
builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDapper(builder.Configuration);
builder.Services.AddFullNetMessagePack();
builder.Services.AddFullNetCaching(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetTenancyWorkerServices();
builder.Services.AddHostedService<OutboxProcessor>();

var host = builder.Build();
await host.RunAsync();

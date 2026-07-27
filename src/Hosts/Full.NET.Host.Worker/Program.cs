using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Host.Worker;
using Full.NET.Serialization.MessagePack;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;

OutboxWorkerCommandLineOptions commandLine;
try
{
    commandLine = OutboxVersionRetirementCommandLine.Parse(args);
}
catch (OutboxVersionRetirementException exception)
{
    await WriteErrorAsync(exception.Code);
    return 1;
}

var builder = Host.CreateApplicationBuilder(
    commandLine.HostArguments.ToArray());
if (commandLine.VersionRetirement is not null)
{
    // 一次性扫描的标准输出只保留机器结果，正常启动日志仍沿用默认级别。
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMessagePack();
builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(OutboxBacklogTelemetry.MeterName));
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
if (commandLine.VersionRetirement is null)
{
    builder.Services.AddHostedService<OutboxProcessor>();
}

using var host = builder.Build();
using (var scope = host.Services.CreateScope())
{
    IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
        scope.ServiceProvider.GetServices<IIntegrationEventHandler>());
}

if (commandLine.VersionRetirement is null)
{
    await host.RunAsync();
    return 0;
}

await host.StartAsync();
try
{
    using var scope = host.Services.CreateScope();
    scope.ServiceProvider
        .GetRequiredService<CurrentTenantAccessor>()
        .SetHost();
    var scanner = new OutboxVersionRetirementScanner(
        scope.ServiceProvider.GetRequiredService<IOutboxBacklogReader>(),
        scope.ServiceProvider
            .GetServices<IIntegrationEventHandler>()
            .ToArray());
    var report = await scanner.ScanAsync(
        commandLine.VersionRetirement,
        CancellationToken.None);
    await Console.Out.WriteLineAsync(
        JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return report.CanRetire ? 0 : 2;
}
catch (OutboxVersionRetirementException exception)
{
    await WriteErrorAsync(exception.Code);
    return 1;
}
finally
{
    await host.StopAsync();
}

static Task WriteErrorAsync(string code) =>
    Console.Error.WriteLineAsync(
        JsonSerializer.Serialize(
            new { code },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

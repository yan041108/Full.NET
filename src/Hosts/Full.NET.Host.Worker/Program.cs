using System.Text.Json;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Caching.Fusion;
using Full.NET.Composition;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Hosting.Observability;
using Full.NET.Hosting.Security;
using Full.NET.Host.Worker;
using Full.NET.Messaging.Abstractions;
using Full.NET.Messaging.Kafka;
using Full.NET.Modularity.Messaging;
using Full.NET.Realtime.SignalR;
using Full.NET.Serialization.MessagePack;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

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

var builder = WebApplication.CreateBuilder(commandLine.HostArguments.ToArray());
if (commandLine.VersionRetirement is not null)
{
    // 一次性扫描的标准输出只保留机器结果，正常启动日志仍沿用默认级别。
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

builder.AddFullNetServiceDefaults();
builder.Services.AddFullNetDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddFullNetDapper(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetDatabaseSchemaModeGuard();
builder.Services.AddFullNetMessagePack();
builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(OutboxBacklogTelemetry.MeterName)
        .AddMeter(OutboxRetentionTelemetry.MeterName)
        .AddMeter(ShadowEventComparisonProcessor.MeterName)
        .AddMeter(KafkaMessagingTelemetry.MeterName));
builder.Services.AddFullNetCaching(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddFullNetRealtimePublisher(
    builder.Configuration,
    builder.Environment.EnvironmentName);
builder.Services.AddOptions<OutboxWorkerOptions>()
    .Bind(builder.Configuration.GetSection(OutboxWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<OutboxWorkerOptions>,
    OutboxWorkerOptionsValidator>();
builder.Services.AddOptions<OutboxRetentionOptions>()
    .Bind(builder.Configuration.GetSection(OutboxRetentionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<OutboxRetentionOptions>,
    OutboxRetentionOptionsValidator>();
builder.Services.AddOptions<ShadowComparisonOptions>()
    .Bind(builder.Configuration.GetSection(ShadowComparisonOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<ShadowComparisonOptions>,
    ShadowComparisonOptionsValidator>();
builder.Services.AddOptions<MessagingWorkerOptions>()
    .Bind(builder.Configuration.GetSection(MessagingWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    IValidateOptions<MessagingWorkerOptions>,
    MessagingWorkerOptionsValidator>();
builder.Services.AddFullNetApplicationModules(
    builder.Configuration,
    FullNetHostProfile.Worker);

var messagingWorkerMode = builder.Configuration
    .GetSection(MessagingWorkerOptions.SectionName)
    .Get<MessagingWorkerOptions>()?.Mode
    ?? MessagingWorkerMode.LegacyPolling;

if (commandLine.VersionRetirement is null)
{
    switch (messagingWorkerMode)
    {
        case MessagingWorkerMode.LegacyPolling:
        case MessagingWorkerMode.ShadowCdc:
            builder.Services.AddHostedService<OutboxProcessor>();
            builder.Services.AddHostedService<OutboxRetentionProcessor>();
            break;
        case MessagingWorkerMode.CdcKafka:
            break;
    }

    if (messagingWorkerMode == MessagingWorkerMode.ShadowCdc)
    {
        builder.Services.AddHostedService<ShadowEventComparisonProcessor>();
    }
    else
    {
        var shadowComparison = builder.Configuration
            .GetSection(ShadowComparisonOptions.SectionName)
            .Get<ShadowComparisonOptions>();
        if (shadowComparison?.Enabled == true)
        {
            builder.Services.AddHostedService<ShadowEventComparisonProcessor>();
        }
    }

    if (messagingWorkerMode == MessagingWorkerMode.CdcKafka)
    {
        builder.Services.AddFullNetModularity();
        builder.Services.AddFullNetKafkaMessaging(
            builder.Configuration,
            builder.Environment.EnvironmentName);
    }
}

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    IntegrationEventHandlerMatcher.ValidateUniqueRoutes(
        scope.ServiceProvider.GetServices<IIntegrationEventHandler>());

    if (commandLine.VersionRetirement is null
        && messagingWorkerMode == MessagingWorkerMode.ShadowCdc)
    {
        var provider = scope.ServiceProvider;
        MessagingWorkerCatalogGuard.ValidateShadowMode(
            provider.GetServices<IIntegrationEventSubscription>().ToArray(),
            provider.GetServices<IntegrationEventTopicDefinition>().ToArray());
    }
    else if (commandLine.VersionRetirement is null
        && messagingWorkerMode == MessagingWorkerMode.CdcKafka)
    {
        MessagingWorkerCatalogGuard.ValidateCdcKafkaMode(
            scope.ServiceProvider
                .GetServices<IIntegrationEventSubscription>()
                .ToArray());
    }
}

if (commandLine.VersionRetirement is null)
{
    app.MapFullNetHealthEndpoints();
    await app.RunAsync();
    return 0;
}

await app.StartAsync();
try
{
    await using var scope = app.Services.CreateAsyncScope();
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
    await app.StopAsync();
}

static Task WriteErrorAsync(string code) =>
    Console.Error.WriteLineAsync(
        JsonSerializer.Serialize(
            new { code },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

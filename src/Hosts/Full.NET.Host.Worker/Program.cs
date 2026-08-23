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
        .AddMeter(KafkaMessagingTelemetry.MeterName))
    .WithTracing(tracing => tracing
        .AddSource(KafkaMessagingTelemetry.ActivitySourceName)
        .AddSource(IntegrationEventConsumerTelemetry.ActivitySourceName));
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

var rawMode = builder.Configuration
    .GetSection(MessagingWorkerOptions.SectionName)
    .Get<MessagingWorkerOptions>()?.Mode
    ?? MessagingWorkerMode.LegacyPolling;

// CdcKafka 枚举值作为 HybridKafka 的过时别名保留一个发布周期，
// 在服务注册前统一规范化为 HybridKafka 语义，避免双重分支判断。
#pragma warning disable CS0618 // CdcKafka 作为过时别名保留一版，旧配置字符串反序列化会得到该值。
var messagingWorkerMode = rawMode == MessagingWorkerMode.CdcKafka
    ? MessagingWorkerMode.HybridKafka
    : rawMode;
#pragma warning restore CS0618

if (commandLine.VersionRetirement is null)
{
    // LegacyPolling、ShadowCdc、HybridKafka 在非退役命令下始终注册 Legacy Outbox 处理器。
    // HybridKafka 模式下，OutboxProcessor 内部通过 IEffectiveEventDeliveryOwnerResolver
    // 按流跳过所有权为 CdcKafka 的消息（抛出 LegacyOwnerRevoked 死信），保证只处理 Legacy 流。
    switch (messagingWorkerMode)
    {
        case MessagingWorkerMode.LegacyPolling:
        case MessagingWorkerMode.ShadowCdc:
        case MessagingWorkerMode.HybridKafka:
            builder.Services.AddHostedService<OutboxProcessor>();
            builder.Services.AddHostedService<OutboxRetentionProcessor>();
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

    // HybridKafka（含过时别名 CdcKafka）模式注册 Kafka 模块化与消费能力。
    if (messagingWorkerMode == MessagingWorkerMode.HybridKafka)
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
        && messagingWorkerMode == MessagingWorkerMode.HybridKafka)
    {
        // 通过 Scoped 目录按流验证订阅，保证与 KafkaConsumerWorker
        // BuildConsumerGroups 所见一致；同时识别 CdcKafka 旧别名情况。
        var provider = scope.ServiceProvider;
        var catalog = provider
            .GetRequiredService<IIntegrationEventSubscriptionCatalog>();
        var subscriptions = catalog.GetAllSubscriptions();
        var topics = provider
            .GetServices<IntegrationEventTopicDefinition>()
            .ToArray();
        MessagingWorkerCatalogGuard.ValidateHybridKafkaMode(subscriptions, topics);
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

/// <summary>
/// Full.NET Worker 宿主入口；只承载后台任务，无 HTTP 管道。
/// </summary>
/// <remarks>
/// 装配顺序：ServiceDefaults → Dapper/Caching/MessagePack/Realtime Publisher →
/// <see cref="FullNetHostProfile.Worker"/> 模块后台能力（仅 <c>AddBackgroundServices</c>）→ 后台服务与 Kafka 消费。
/// <para>后台服务按 <c>MessagingWorkerMode</c> 选择注册：<c>OutboxProcessor</c>、<c>OutboxRetentionProcessor</c>、
/// <c>ShadowEventComparisonProcessor</c> 与 Kafka 消费者；<c>CdcKafka</c> 作为过时别名规范化为 <c>HybridKafka</c>。</para>
/// <para>支持一次性 Outbox 版本退役扫描命令；非退役模式下才注册后台服务并 <c>RunAsync</c>。</para>
/// </remarks>
public partial class Program;

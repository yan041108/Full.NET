using Full.NET.Hosting.Api;
using Full.NET.Hosting.Serialization;
using Full.NET.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace Full.NET.Hosting.Observability;

/// <summary>
/// 宿主级默认能力注入入口；统一装配 Serilog 结构化日志、OpenTelemetry Tracing/Metrics、
/// 全局异常处理、本地化、健康检查、标准结果映射与 HTTP 弹性韧性。
/// 调用应位于 Program.cs 的最早期，确保后续所有服务注册均可复用本扩展已就绪的基础设施。
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>
    /// 向宿主注册 Full.NET 默认服务：Serilog + 双缓冲异步管道、OTel Metrics/Tracing、
    /// ProblemDetails/异常处理、本地化资源、健康检查、HTTP 操作日志、
    /// 标准 <see cref="IApiResultMapper"/> 以及 HttpClient 标准韧性策略（ServiceDiscovery + Polly）。
    /// </summary>
    /// <param name="builder">宿主应用构建器；用于读取配置与写入 <see cref="IServiceCollection"/>。</param>
    /// <param name="createLogExporter">仅在显式选择 ApplicationKafka 后调用的静态出口工厂。</param>
    /// <exception cref="OptionsValidationException">
    /// LoggingOptions 存在阻塞、条数/字节容量、单事件上限或刷新超时配置非法时启动期抛出。
    /// </exception>
    public static IHostApplicationBuilder AddFullNetServiceDefaults(
        this IHostApplicationBuilder builder,
        Func<IConfiguration, IHostLogSnapshotExporter>? createLogExporter = null)
    {
        // 失效的直写配置必须显式报错，不能悄悄切换为默认控制台入口。
        if (builder.Configuration.GetSection($"{LoggingOptions.SectionName}:Elasticsearch").Exists())
            throw new OptionsValidationException(LoggingOptions.SectionName, typeof(LoggingOptions),
                ["FullNet:Logging:Elasticsearch is unsupported. Select Local, Collector or ApplicationKafka and configure ES in the independent consumer."]);
        // 显式 null 不能由配置绑定器转换为枚举零值 Local；只有完全未设置才采用默认入口。
        foreach (var setting in builder.Configuration.GetSection(LoggingOptions.SectionName).GetChildren())
        {
            if ((setting.Key.Equals(nameof(LoggingOptions.DeliveryMode), StringComparison.OrdinalIgnoreCase)
                 || setting.Key.Equals(nameof(LoggingOptions.ExpectedDeliveryMode), StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(setting.Value))
                throw new OptionsValidationException(LoggingOptions.SectionName, typeof(LoggingOptions),
                    [$"Explicit {setting.Key} must not be null or empty."]);
        }
        var loggingOptions = builder.Configuration
                .GetSection(LoggingOptions.SectionName)
                .Get<LoggingOptions>()
            ?? new LoggingOptions();
        if (!Enum.IsDefined(loggingOptions.DeliveryMode))
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["DeliveryMode must be Local, Collector or ApplicationKafka."]);
        }

        if (!Enum.IsDefined(loggingOptions.ExpectedDeliveryMode))
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["ExpectedDeliveryMode must be Local, Collector or ApplicationKafka."]);
        }

        if (loggingOptions.DeliveryMode != loggingOptions.ExpectedDeliveryMode
            || (builder.Configuration[$"{LoggingOptions.SectionName}:DeliveryMode"] is null)
                != (builder.Configuration[$"{LoggingOptions.SectionName}:ExpectedDeliveryMode"] is null))
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["Explicit DeliveryMode requires the same ExpectedDeliveryMode injected by the deployment; neither value may be set alone."]);
        }

        loggingOptions.LocalFile ??= new LocalFileLoggingOptions();
        loggingOptions.LocalFile.Validate(loggingOptions);
        // Production 的 Local 投递必须落盘；仅控制台快照无法满足保留与审计边界。
        if (builder.Environment.IsProduction()
            && loggingOptions.DeliveryMode is LoggingDeliveryMode.Local
            && !loggingOptions.LocalFile.Enabled)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["DeliveryMode=Local requires enabled LocalFile storage in Production."]);
        }

        if (loggingOptions.DeliveryMode == LoggingDeliveryMode.ApplicationKafka
            && createLogExporter is null)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["ApplicationKafka requires a qualified logging adapter; no adapter is registered in this build."]);
        }

        if (loggingOptions.AsyncBufferSize <= 0)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["AsyncBufferSize must be greater than zero."]);
        }

        if (loggingOptions.HighPriorityAsyncBufferSize <= 0)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["HighPriorityAsyncBufferSize must be greater than zero."]);
        }

        if (loggingOptions.MaxEventBytes is < 256 or > 65_536)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["MaxEventBytes must be between 256 and 65536."]);
        }

        var minimumQueueBytes = (long)loggingOptions.MaxEventBytes + 128;
        if (loggingOptions.GeneralQueueMaxBytes < minimumQueueBytes)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["GeneralQueueMaxBytes must hold at least one maximum-sized event and its envelope."]);
        }

        if (loggingOptions.HighPriorityQueueMaxBytes < minimumQueueBytes)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["HighPriorityQueueMaxBytes must hold at least one maximum-sized event and its envelope."]);
        }

        if (loggingOptions.BlockWhenFull)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["BlockWhenFull must remain false to protect request threads."]);
        }

        if (loggingOptions.ShutdownFlushTimeout <= TimeSpan.Zero
            || loggingOptions.ShutdownFlushTimeout > TimeSpan.FromSeconds(30))
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["ShutdownFlushTimeout must be greater than zero and no greater than 30 seconds."]);
        }

        // 由容器创建并拥有监控器，保证 Host 释放时注销 Meter；预创建实例不由 DI 自动释放。
        builder.Services.AddSingleton(static _ => new FullNetLoggingMonitors());
        var httpOperationIngress = new HttpOperationLogIngress();
        builder.Services.AddSingleton(httpOperationIngress);
        builder.Services.AddSingleton<ILoggingDeliverySelection>(
            new LoggingDeliverySelection(loggingOptions.DeliveryMode));
        var loggingResource = LoggingResourceMetadata.Create(
            builder.Environment.ApplicationName,
            builder.Environment.EnvironmentName);
        builder.Services.AddSingleton(loggingResource);
        builder.Services.AddHostedService<LoggingResourceAnnouncementService>();
        if ((loggingOptions.IndexRouteVersion == 0) != (loggingOptions.IndexRetentionDays == 0)
            || loggingOptions.IndexRouteVersion is < 0 or > 9999
            || loggingOptions.IndexRetentionDays is < 0 or > 3650)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["IndexRouteVersion (1..9999) and IndexRetentionDays (1..3650) must be configured together or both left at zero."]);
        }
        if (loggingOptions.DeliveryMode == LoggingDeliveryMode.ApplicationKafka
            && loggingOptions.IndexRouteVersion == 0)
        {
            throw new OptionsValidationException(
                LoggingOptions.SectionName,
                typeof(LoggingOptions),
                ["ApplicationKafka requires a frozen IndexRouteVersion and IndexRetentionDays policy."]);
        }
        builder.Services.AddSerilog((services, loggerConfiguration) =>
        {
            var effectiveLoggingOptions = builder.Configuration
                .GetSection(LoggingOptions.SectionName)
                .Get<LoggingOptions>() ?? new LoggingOptions();
            if (effectiveLoggingOptions?.DeliveryMode != loggingOptions.DeliveryMode
                || effectiveLoggingOptions?.ExpectedDeliveryMode != loggingOptions.ExpectedDeliveryMode
                || !loggingOptions.LocalFile.Matches(effectiveLoggingOptions?.LocalFile ?? new LocalFileLoggingOptions()))
            {
                throw new OptionsValidationException(
                    LoggingOptions.SectionName,
                    typeof(LoggingOptions),
                    ["DeliveryMode, ExpectedDeliveryMode, LocalFile changed after logging registration; restart with a stable delivery configuration."]);
            }

            var exporter = loggingOptions.DeliveryMode == LoggingDeliveryMode.ApplicationKafka
                ? createLogExporter!(builder.Configuration)
                    ?? throw new OptionsValidationException(
                        LoggingOptions.SectionName,
                        typeof(LoggingOptions),
                        ["ApplicationKafka exporter factory returned null."])
                : null;
            LocalFileLogStore? localFiles = null;
            try
            {
                // 目录租约与 Writer 在注册期创建；Serilog 回调失败时必须同步释放，避免孤儿锁占用日志目录。
                if (loggingOptions.LocalFile.Enabled)
                    localFiles = new LocalFileLogStore(loggingOptions.LocalFile,
                        builder.Environment.ContentRootPath, builder.Environment.ApplicationName);
                FullNetLoggingPipeline.Configure(
                    loggerConfiguration,
                    builder.Environment.ApplicationName,
                    loggingOptions,
                    services.GetRequiredService<FullNetLoggingMonitors>(),
                    null,
                    null,
                    loggingResource,
                    // 文件或 Kafka 出口已承担持久化/外发时关闭控制台 JSON，防止重复落盘与敏感字段扩散。
                    loggingOptions.DeliveryMode == LoggingDeliveryMode.ApplicationKafka || localFiles is not null
                        ? null
                        : LogEnvelopeConsoleWriter.Emit,
                    emitExternalSnapshot: exporter is null ? null : exporter.TryEmit,
                    httpOperationIngress: httpOperationIngress,
                    externalExporter: exporter,
                    localFiles: localFiles);
            }
            catch
            {
                localFiles?.General.Dispose();
                localFiles?.Priority.Dispose();
                exporter?.Dispose();
                throw;
            }
        });

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<FullNetExceptionHandler>();
        builder.Services.AddFullNetJson();
        builder.Services.AddFullNetLocalization();
        builder.Services.TryAddSingleton<NamedMessageFormatter>();
        builder.Services.TryAddSingleton<
            IErrorMessageLocalizer,
            ResourceErrorMessageLocalizer>();
        builder.Services.TryAddSingleton<
            IPreV1LegacyErrorCodeProfile,
            DefaultPreV1LegacyErrorCodeProfile>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            CommonErrorResourceSource>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            AuthorizationErrorResourceSource>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            ValidationErrorResourceSource>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            LocalizationErrorResourceSource>());
        builder.Services.AddHealthChecks()
            .AddCheck<HighPriorityLoggingHealthCheck>(
                "high_priority_logging",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready"]);

        var telemetryServiceName = builder.Configuration["OTEL_SERVICE_NAME"];
        if (string.IsNullOrWhiteSpace(telemetryServiceName))
            telemetryServiceName = loggingResource.Application;
        var openTelemetry = builder.Services.AddOpenTelemetry()
            // 与日志共用实例标识，避免多宿主 Counter/Gauge 合并到同一 Remote Write 时间序列。
            .ConfigureResource(resource => resource.AddService(
                telemetryServiceName, serviceInstanceId: loggingResource.Instance))
            .WithMetrics(metrics => metrics
                .AddMeter(FullNetAsyncLogMonitor.MeterName)
                .AddMeter("Full.NET.Logging.Kafka")
                .AddMeter(HttpOperationLogTelemetry.MeterName)
                .AddMeter(ResourceErrorMessageLocalizer.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        if (!string.IsNullOrWhiteSpace(
                builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            openTelemetry.UseOtlpExporter();
        }

        builder.Services.AddOptions<HttpOperationLogOptions>()
            .BindConfiguration(HttpOperationLogOptions.SectionName)
            .ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<HttpOperationLogOptions>,
            HttpOperationLogOptionsValidator>());
        builder.Services.TryAddSingleton<IDiagnosticPolicyStore, DefaultDiagnosticPolicyStore>();
        builder.Services.TryAddSingleton<HttpOperationLogEmitter>();
        builder.Services.TryAddSingleton(_ => new HttpLogCaptureBudget(TimeProvider.System));
        builder.Services.TryAddSingleton<HttpOperationPayloadProjection>(services =>
            new HttpOperationPayloadProjection(
                services.GetRequiredService<IOptionsMonitor<HttpOperationLogOptions>>(),
                services.GetRequiredService<HttpLogCaptureBudget>(),
                services.GetRequiredService<HttpOperationLogEmitter>(),
                services.GetRequiredService<ILogger<HttpOperationLogMiddleware>>()));

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(httpClient =>
        {
            httpClient.AddStandardResilienceHandler();
            httpClient.AddServiceDiscovery();
        });
        builder.Services.AddSingleton<IApiResultMapper, StandardApiResultMapper>();
        return builder;
    }

    /// <summary>
    /// 注册普通 HTTP Operation Log（B2）中间件，替代 Serilog RequestLogging 的重复 Access 摘要。
    /// </summary>
    public static IApplicationBuilder UseFullNetRequestLogging(
        this IApplicationBuilder app) =>
        app.UseMiddleware<HttpOperationLogMiddleware>();
}

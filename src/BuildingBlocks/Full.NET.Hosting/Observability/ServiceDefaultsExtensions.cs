using Full.NET.Hosting.Api;
using Full.NET.Hosting.Serialization;
using Full.NET.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

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
    /// <exception cref="OptionsValidationException">
    /// LoggingOptions 存在缓冲配置非法（BlockWhenFull=true、缓冲区大小非正、刷新超时超限）时启动期抛出。
    /// </exception>
    public static IHostApplicationBuilder AddFullNetServiceDefaults(
        this IHostApplicationBuilder builder)
    {
        var loggingOptions = builder.Configuration
                .GetSection(LoggingOptions.SectionName)
                .Get<LoggingOptions>()
            ?? new LoggingOptions();
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

        var loggingMonitors = new FullNetLoggingMonitors();
        builder.Services.AddSingleton(loggingMonitors);
        builder.Services.AddSerilog((services, configuration) =>
        {
            configuration.ReadFrom.Services(services);
            FullNetLoggingPipeline.Configure(
                configuration,
                builder.Environment.ApplicationName,
                loggingOptions,
                loggingMonitors,
                sink => sink.Console(new CompactJsonFormatter()),
                sink => sink.Console(new CompactJsonFormatter()));
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

        var openTelemetry = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(FullNetAsyncLogMonitor.MeterName)
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

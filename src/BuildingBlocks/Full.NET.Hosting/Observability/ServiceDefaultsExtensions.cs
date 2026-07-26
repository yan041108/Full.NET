using System.Diagnostics;
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
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Full.NET.Hosting.Observability;

public static class ServiceDefaultsExtensions
{
    public static IHostApplicationBuilder AddFullNetServiceDefaults(
        this IHostApplicationBuilder builder)
    {
        var loggingOptions = new LoggingOptions();
        builder.Configuration.GetSection(LoggingOptions.SectionName).Bind(loggingOptions);
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

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(httpClient =>
        {
            httpClient.AddStandardResilienceHandler();
            httpClient.AddServiceDiscovery();
        });
        builder.Services.AddSingleton<IApiResultMapper, StandardApiResultMapper>();
        return builder;
    }

    public static IApplicationBuilder UseFullNetRequestLogging(
        this IApplicationBuilder app) =>
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Host);
                diagnosticContext.Set(
                    "TraceId",
                    Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier);

                if (httpContext.Items.TryGetValue("FullNet.TenantId", out var tenantId))
                {
                    diagnosticContext.Set("TenantId", tenantId);
                }
            };
        });
}

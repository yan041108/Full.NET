using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Time;
using Full.NET.Hosting.Api;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Middleware;
using Full.NET.Modules.Auditing.Resources;
using Full.NET.Modules.Auditing.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Modules.Auditing;

public sealed class AuditingModule : IFullNetModule
{
    public string Name => "Auditing";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            AuditingAuthorizationContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IErrorResourceSource,
            AuditingErrorResourceSource>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IIdGenerator, GuidV7IdGenerator>();
        services.TryAddScoped<AccessLogWriter>();
        services.TryAddScoped<OperationLogWriter>();
        services.TryAddScoped<ExceptionLogWriter>();
        services.TryAddScoped<Features.QueryHostAccessLogs.HostAccessLogQueryService>();
        services.TryAddScoped<Features.QueryHostOperationLogs.HostOperationLogQueryService>();
        services.TryAddScoped<Features.QueryHostExceptionLogs.HostExceptionLogQueryService>();
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                AuditingJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.QueryHostAccessLogs.Endpoint.Map(endpoints);
        Features.QueryHostOperationLogs.Endpoint.Map(endpoints);
        Features.QueryHostExceptionLogs.Endpoint.Map(endpoints);
        var environment = endpoints.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        Features.TriggerExceptionProbe.Endpoint.Map(endpoints, environment);
    }

    /// <summary>
    /// 异常日志中间件必须最靠近 Endpoint，以便捕获业务异常后重抛给外层 ExceptionHandler。
    /// </summary>
    public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
    {
        if (stage == ModulePipelineStage.BeforeEndpoints)
        {
            app.UseMiddleware<AccessLogMiddleware>();
            app.UseMiddleware<OperationLogMiddleware>();
            app.UseMiddleware<ExceptionLogMiddleware>();
        }
    }
}

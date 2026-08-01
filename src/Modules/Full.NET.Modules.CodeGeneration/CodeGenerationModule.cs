using Full.NET.Abstractions.Time;
using Full.NET.Modularity.Modules;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Features.ManageHostTemplates;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Features.PreviewCrudGeneration;
using Full.NET.Modules.CodeGeneration.Retention;
using Full.NET.Modules.CodeGeneration.Serialization;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration;

/// <summary>
/// 提供 Host 管理端代码生成预览能力，并保持生成引擎与 HTTP 边界分离。
/// </summary>
public sealed class CodeGenerationModule : IFullNetModule
{
    public string Name => "CodeGeneration";

    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IAuthorizationCatalogContributor,
            CodeGenerationAuthorizationContributor>());
        services.TryAddSingleton<CodeGenerationSchemaNormalizer>();
        services.TryAddScoped<CodeGenerationPreviewService>();
        services.TryAddScoped<CodeGenerationRunService>();
        services.TryAddScoped<CodeGenerationRunQueryService>();
        services.TryAddScoped<CodeGenerationApplyService>();
        services.TryAddScoped<CodeGenerationRollbackService>();
        services.TryAddSingleton<ICodeGenerationWorkspaceLockBackend,
            SessionAppLockWorkspaceLockBackend>();
        services.TryAddSingleton<CodeGenerationApplyGate>();
        services.TryAddScoped<CodeGenerationTemplateQueryService>();
        services.TryAddScoped<CodeGenerationTemplateManagementService>();
        services.AddOptions<CodeGenerationApplyOptions>()
            .Bind(configuration.GetSection(
                CodeGenerationApplyOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<CodeGenerationApplyOptions>,
            CodeGenerationApplyOptionsValidator>());
        services.AddOptions<CodeGenerationCheckpointRetentionOptions>()
            .Bind(configuration.GetSection(
                CodeGenerationCheckpointRetentionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<CodeGenerationCheckpointRetentionOptions>,
            CodeGenerationCheckpointRetentionOptionsValidator>());
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Insert(
                0,
                CodeGenerationJsonSerializerContext.Default));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Features.PreviewCrudGeneration.Endpoint.Map(endpoints);
        Features.ManageHostTemplates.Endpoint.Map(endpoints);
        Features.ManageHostRuns.Endpoint.Map(endpoints);
    }

    /// <summary>
    /// 仅为 Worker 装配默认关闭的检查点保留清理，避免 API 进程重复执行后台任务。
    /// </summary>
    public void AddBackgroundServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CodeGenerationCheckpointRetentionOptions>()
            .Bind(configuration.GetSection(
                CodeGenerationCheckpointRetentionOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<CodeGenerationCheckpointRetentionOptions>,
            CodeGenerationCheckpointRetentionOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<CodeGenerationCheckpointRetentionRunner>();
        services.AddHostedService<CodeGenerationCheckpointRetentionHostedProcessor>();
    }
}

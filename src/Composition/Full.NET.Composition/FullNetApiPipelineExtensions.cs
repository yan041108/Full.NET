using Full.NET.Hosting.Api;
using Full.NET.Hosting.Forwarding;
using Full.NET.Hosting.Observability;
using Full.NET.Localization;
using Full.NET.Modularity.Modules;
using Microsoft.AspNetCore.Builder;

namespace Full.NET.Composition;

/// <summary>正式宿主与项目模板共用的 API 管线，保证审计收尾和身份限流顺序一致。</summary>
public static class FullNetApiPipelineExtensions
{
    /// <summary>在端点映射前安装必要管线；模块通过声明的阶段扩展，不复制宿主顺序。</summary>
    /// <param name="app">已完成模块注册的应用。</param>
    /// <returns>当前应用。</returns>
    public static WebApplication UseFullNetApiPipeline(this WebApplication app)
    {
        app.UseFullNetTrustedProxyForwarding();
        app.UseFullNetLocalization();
        app.UseFullNetRequestLogging();
        app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeExceptionHandler);
        app.UseExceptionHandler();
        app.UseFullNetRequestBodyProblemDetails();
        app.UseCors(FullNetModuleCatalog.BrowserCorsPolicy);
        app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthentication);
        app.UseAuthentication();
        // 身份已经验证，认证用户按其身份分区，匿名请求仍使用来源地址。
        app.UseRateLimiter();
        app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthorization);
        app.UseAuthorization();
        app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeEndpoints);
        return app;
    }
}

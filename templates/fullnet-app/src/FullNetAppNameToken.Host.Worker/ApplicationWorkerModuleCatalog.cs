using Full.NET.Composition;
using FullNetAppNameToken.Composition;

namespace Full.NET.Host.Worker;

/// <summary>把共享 Worker 管线接到应用自有的模块目录。</summary>
internal static class ApplicationWorkerModuleCatalog
{
    /// <summary>仅装配应用与框架模块声明的后台能力。</summary>
    internal static void Register(IServiceCollection services, IConfiguration configuration) =>
        services.AddApplicationModules(configuration, FullNetHostProfile.Worker);
}

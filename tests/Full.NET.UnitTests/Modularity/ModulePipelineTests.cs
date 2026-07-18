using Full.NET.Modularity.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Modularity;

[TestClass]
public sealed class ModulePipelineTests
{
    [TestMethod]
    public void UseFullNetModuleMiddleware_invokes_modules_in_dependency_order()
    {
        var log = new List<string>();
        var registry = new FullNetModuleRegistry();
        // 故意乱序注册，验证按依赖顺序（Alpha 先于依赖它的 Beta）应用中间件。
        registry.Add(new BetaModule(log));
        registry.Add(new AlphaModule(log));
        using var provider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeAuthorization);

        CollectionAssert.AreEqual(
            new[]
            {
                "Alpha:BeforeAuthorization",
                "Beta:BeforeAuthorization",
            },
            log);
    }

    [TestMethod]
    public void UseFullNetModuleMiddleware_skips_modules_without_pipeline_contribution()
    {
        var log = new List<string>();
        var registry = new FullNetModuleRegistry();
        registry.Add(new AlphaModule(log));
        registry.Add(new SilentModule());
        using var provider = new ServiceCollection()
            .AddSingleton(registry)
            .BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        app.UseFullNetModuleMiddleware(ModulePipelineStage.BeforeEndpoints);

        CollectionAssert.AreEqual(new[] { "Alpha:BeforeEndpoints" }, log);
    }

    private sealed class AlphaModule(List<string> log) : IFullNetModule
    {
        public string Name => "Alpha";

        public IReadOnlyCollection<Type> Dependencies => [];

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }

        public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
            => log.Add($"Alpha:{stage}");
    }

    private sealed class BetaModule(List<string> log) : IFullNetModule
    {
        public string Name => "Beta";

        public IReadOnlyCollection<Type> Dependencies => [typeof(AlphaModule)];

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }

        public void UseModuleMiddleware(IApplicationBuilder app, ModulePipelineStage stage)
            => log.Add($"Beta:{stage}");
    }

    // 不覆盖 UseModuleMiddleware，验证默认接口实现为空操作，宿主无需为此类模块做特殊处理。
    private sealed class SilentModule : IFullNetModule
    {
        public string Name => "Silent";

        public IReadOnlyCollection<Type> Dependencies => [];

        public void AddServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }
}

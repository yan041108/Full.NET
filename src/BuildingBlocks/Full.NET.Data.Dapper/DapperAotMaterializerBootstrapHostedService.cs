#if FULLNET_AOT_COMPILE
using Microsoft.Extensions.Hosting;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 在宿主接受请求前汇总各模块注册的 AOT 行物化器。
/// </summary>
internal sealed class DapperAotMaterializerBootstrapHostedService(
    IEnumerable<IDapperAotMaterializerContributor> contributors) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var registrar = new DapperAotMaterializerRegistrar();
        foreach (var contributor in contributors)
        {
            contributor.RegisterMaterializers(registrar);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
#endif

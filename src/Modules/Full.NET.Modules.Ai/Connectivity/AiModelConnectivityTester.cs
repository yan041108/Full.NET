using Full.NET.Modules.Ai.Security;
using Full.NET.AI.Abstractions.Connectivity;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Connectivity;

/// <summary>将已授权配置交给显式注册的探测适配器；业务编排不解密或处理供应商协议。</summary>
internal sealed class AiModelConnectivityTester(IEnumerable<IAiModelConnectivityProbe> probes, AiModelBindingScope bindings)
{
    /// <summary>允许探测尚未启用的配置；调用方必须先完成配置访问权限和租户范围查询。</summary>
    public async Task<(bool Succeeded, string Message)> TestAsync(AiModelConfigRecord record,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var probe = probes.SingleOrDefault(item => item.ProviderKey == record.ProviderKey);
        if (probe is null) return (false, "Unsupported provider key.");
        var binding = bindings.Create(record);
        var result = await probe.TestConnectivityAsync(binding, cancellationToken).ConfigureAwait(false);
        return (result.Succeeded, result.Message);
    }
}

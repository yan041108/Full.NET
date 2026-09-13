using Full.NET.AI.Abstractions.Models;

namespace Full.NET.AI.Abstractions.Connectivity;

/// <summary>供应商连通性扩展点；只接受调用方已授权的模型绑定，不承担业务数据查询。</summary>
public interface IAiModelConnectivityProbe
{
    /// <summary>与配置兼容的静态提供程序键。</summary>
    string ProviderKey { get; }

    /// <summary>在有限时间与响应预算内探测供应商；取消保持异常语义。</summary>
    /// <param name="binding">已经过模型配置访问校验的绑定；凭据仅为受保护内容。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可持久化的安全摘要，不得包含远端正文、密钥或异常原文。</returns>
    Task<ModelConnectivityResult> TestConnectivityAsync(ModelBinding binding, CancellationToken cancellationToken = default);
}

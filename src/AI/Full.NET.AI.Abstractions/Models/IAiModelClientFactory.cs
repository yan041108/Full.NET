using Microsoft.Extensions.AI;
namespace Full.NET.AI.Abstractions.Models;
/// <summary>显式注册的供应商工厂；不承担业务授权，不允许由外部请求直接构造绑定。</summary>
public interface IAiModelClientFactory
{
    /// <summary>唯一静态提供程序键。</summary>
    string ProviderKey { get; }
    /// <summary>创建独立客户端；调用方负责释放，不与其他租户共享认证状态。</summary>
    /// <param name="binding">已授权模型绑定。</param><param name="cancellationToken">取消令牌。</param>
    /// <returns>新建的独立 IChatClient 实例；调用方拥有所有权并负责释放。</returns>
    ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken);
    /// <summary>创建独立 Embedding 生成器；不支持时由实现显式拒绝，不返回假向量。</summary>
    /// <param name="binding">已授权模型绑定。</param><param name="cancellationToken">取消令牌。</param>
    /// <returns>新建的独立 IEmbeddingGenerator 实例；调用方拥有所有权并负责释放；不支持 Embedding 的提供程序应抛出 NotSupportedException。</returns>
    ValueTask<IEmbeddingGenerator<string, Embedding<float>>> CreateEmbeddingGeneratorAsync(
        ModelBinding binding, CancellationToken cancellationToken);
}

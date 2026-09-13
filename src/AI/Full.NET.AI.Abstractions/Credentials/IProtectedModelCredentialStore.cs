using Full.NET.AI.Abstractions.Models;

namespace Full.NET.AI.Abstractions.Credentials;

/// <summary>读取已授权模型绑定的受保护凭据；实现必须验证引用作用域与完整绑定。</summary>
public interface IProtectedModelCredentialStore
{
    /// <summary>读取密文；无凭据返回 null，未知或被篡改的引用必须失败关闭。</summary>
    /// <param name="binding">已由业务授权并签发的模型绑定。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>只供 Provider 解保护的密文，禁止记录或交给模型。</returns>
    ValueTask<string?> ReadAsync(ModelBinding binding, CancellationToken cancellationToken = default);
}

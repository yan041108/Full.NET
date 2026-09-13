namespace Full.NET.AI.Abstractions.Credentials;

/// <summary>配置输入边界使用的凭据写入保护接口；不向业务模块提供解密能力。</summary>
public interface IAiModelCredentialProtector
{
    /// <summary>立即保护输入密钥，保持既有密文格式兼容。</summary>
    /// <param name="credential">刚从授权配置请求取得的明文；禁止写日志。</param>
    /// <returns>可存入 Ai 配置所有者存储的密文。</returns>
    string Protect(string credential);
}

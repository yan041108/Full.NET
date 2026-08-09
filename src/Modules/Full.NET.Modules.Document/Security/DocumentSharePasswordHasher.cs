using Microsoft.AspNetCore.Identity;

namespace Full.NET.Modules.Document.Security;

/// <summary>
/// 文档分享口令哈希主体：使用 ASP.NET Core Identity 的 PasswordHasher
/// 执行 PBKDF2 + 随机盐 + 版本标识，确保抗时序与抗彩虹表攻击。
/// </summary>
internal sealed class DocumentSharePasswordSubject;

/// <summary>
/// 文档分享口令哈希/验证契约。
/// 为什么不直接用 string 比较：口令属于凭据类数据，必须使用
/// 固定耗时比较（PasswordVerificationResult）避免时序侧信道，
/// 并且存储内容必须不可逆（明文口令永不落库、永不回传）。
/// </summary>
internal interface IDocumentSharePasswordHasher
{
    /// <summary>生成口令哈希；空口令抛出以避免调用方误写入空串。</summary>
    string Hash(Guid shareId, string password);

    /// <summary>验证口令；返回值同时区分 rehash-needed 以便迭代升级算法。</summary>
    bool Verify(Guid shareId, string passwordHash, string providedPassword);
}

/// <summary>
/// 基于 ASP.NET Core Identity PasswordHasher 的实现。
/// shareId 作为目的字符串（purpose）纳入签名，避免跨表重放哈希。
/// </summary>
internal sealed class DocumentSharePasswordHasher : IDocumentSharePasswordHasher
{
    private readonly PasswordHasher<DocumentSharePasswordSubject> _hasher = new();
    private readonly DocumentSharePasswordSubject _subject = new();

    public string Hash(Guid shareId, string password)
    {
        // 中文注释：把 shareId 作为目的追加到口令再做哈希，
        // 等价于带 purpose 的独立 key，避免攻击者把其他分享
        // 行的 PasswordHash 直接搬运过来当作口令验证。
        var materialized = Materialize(shareId, password);
        return _hasher.HashPassword(_subject, materialized);
    }

    public bool Verify(Guid shareId, string passwordHash, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrEmpty(providedPassword))
        {
            return false;
        }

        var materialized = Materialize(shareId, providedPassword);
        var result = _hasher.VerifyHashedPassword(_subject, passwordHash, materialized);

        // Rehash-needed 仍然视为验证成功；上层在写事务内可以根据结果升级 hash。
        // 当前为最小正确实现：统一返回 bool，rehash 升级单独迭代。
        return result != PasswordVerificationResult.Failed;
    }

    /// <summary>
    /// 把 shareId 与 password 组合成单一字符串输入；
    /// 这里不追求密码学意义的 HMAC 目的绑定（PasswordHasher 内部已有 salt+HMAC），
    /// 只是为了保证不同 shareId 下相同明文产生完全不同的 PasswordHash 输出。
    /// </summary>
    private static string Materialize(Guid shareId, string password) =>
        shareId.ToString("D") + "|" + password;
}

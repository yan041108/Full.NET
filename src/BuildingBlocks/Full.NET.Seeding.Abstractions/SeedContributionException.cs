namespace Full.NET.Seeding.Abstractions;

/// <summary>
/// 表示 Contributor 已识别且可安全公开的稳定失败；异常消息仅包含机器错误码。
/// </summary>
public sealed class SeedContributionException : Exception
{
    /// <summary>使用稳定错误码创建 Contributor 失败。</summary>
    /// <param name="code">不包含 Secret、个人数据或动态输入的稳定机器错误码。</param>
    public SeedContributionException(string code)
        : base(code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    /// <summary>取得供编排结果与执行审计使用的稳定错误码。</summary>
    public string Code { get; }
}

/// <summary>
/// 集中声明由业务 Contributor 产生且跨模块共享的稳定错误码。
/// </summary>
public static class SeedContributionErrorCodes
{
    /// <summary>自然键已存在，但真实数据与目标 Seed 状态冲突。</summary>
    public const string DataConflict = "seeding.data.conflict";
}

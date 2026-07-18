namespace Full.NET.Seeding.Dapper;

/// <summary>
/// 集中声明 Seed 配置和执行边界使用的稳定错误码。
/// </summary>
public static class SeedErrorCodes
{
    /// <summary>CLI 中的 Seed 参数组合无效。</summary>
    public const string CommandInvalid = "seeding.command.invalid";

    /// <summary>Contributor 的稳定名称不符合约定。</summary>
    public const string ContributorNameInvalid = "seeding.contributor.name_invalid";

    /// <summary>Contributor 版本必须从 1 开始单调递增。</summary>
    public const string ContributorVersionInvalid = "seeding.contributor.version_invalid";

    /// <summary>Contributor 的 Profile 集合为空或包含未知值。</summary>
    public const string ContributorProfileInvalid = "seeding.contributor.profile_invalid";

    /// <summary>Contributor 稳定名称重复。</summary>
    public const string ContributorDuplicate = "seeding.contributor.duplicate";

    /// <summary>当前 Profile 的执行图缺少直接依赖。</summary>
    public const string DependencyMissing = "seeding.dependency.missing";

    /// <summary>当前 Profile 的执行图包含循环依赖。</summary>
    public const string DependencyCycle = "seeding.dependency.cycle";

    /// <summary>Seeding 配置节不满足安全边界。</summary>
    public const string OptionsInvalid = "seeding.options.invalid";

    /// <summary>当前宿主环境不允许执行请求的 Profile。</summary>
    public const string ProfileNotAllowed = "seeding.profile.not_allowed";

    /// <summary>在限定时间内未取得数据库级 Seed 独占锁。</summary>
    public const string LockTimeout = "seeding.lock.timeout";

    /// <summary>Contributor 执行失败，详细异常不得写入执行审计。</summary>
    public const string ContributorFailed = "seeding.contributor.failed";

    /// <summary>Seed 执行被调用方取消。</summary>
    public const string ExecutionCancelled = "seeding.execution.cancelled";

    /// <summary>获取全部稳定错误码，用于命名契约门禁。</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        CommandInvalid,
        ContributorNameInvalid,
        ContributorVersionInvalid,
        ContributorProfileInvalid,
        ContributorDuplicate,
        DependencyMissing,
        DependencyCycle,
        OptionsInvalid,
        ProfileNotAllowed,
        LockTimeout,
        ContributorFailed,
        ExecutionCancelled,
    ];
}

/// <summary>
/// 表示 Seed 在接触数据库前即可确定的配置错误；消息只暴露稳定错误码。
/// </summary>
public sealed class SeedConfigurationException : Exception
{
    /// <summary>使用稳定错误码创建安全异常。</summary>
    /// <param name="code">不包含参数值、连接信息或 Secret 的稳定错误码。</param>
    public SeedConfigurationException(string code)
        : base(code)
    {
        Code = code;
    }

    /// <summary>获取可供 CLI 和日志分类的稳定错误码。</summary>
    public string Code { get; }
}

using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Calendar.Contracts;

/// <summary>本人日程知识助手的固定来源协议；不开放任意业务对象或字段投影。</summary>
public static class PersonalScheduleKnowledgeSourceKeys
{
    /// <summary>持久来源种类，不能以普通文档或文件替代。</summary>
    public const string Kind = "calendar-personal-schedule-v1";

    /// <summary>固定字段投影与用途，不接受调用方另选字段或用途。</summary>
    public const string Purpose = "knowledge-assistant-v1";

    /// <summary>单次完整来源批次上限；超限拒绝，禁止静默截断。</summary>
    public const int MaxSources = 8;
}

/// <summary>个人日程向知识助手交付字段的独立用途权限。</summary>
public static class PersonalScheduleKnowledgePermissions
{
    /// <summary>在本人日程读取权限之外，允许显式读取助手来源预览。</summary>
    public const string Read = "calendar.personal_schedules.assistant_read";
}

/// <summary>本人日程来源边界的稳定错误码，由 Calendar 资源包提供译文。</summary>
public static class PersonalScheduleKnowledgeErrorCodes
{
    /// <summary>来源选择不是非空、唯一且最多八项的本人日程集合。</summary>
    public const string Invalid = "calendar.personal_schedule_source_invalid";

    /// <summary>任一来源不可读、变更或未完成当前权威核证。</summary>
    public const string Unavailable = "calendar.personal_schedule_source_unavailable";
}

/// <summary>不含正文的权威来源身份；保存历史事实，不构成下一次授权凭证。</summary>
/// <param name="Kind">固定来源协议。</param>
/// <param name="Purpose">固定投影用途。</param>
/// <param name="Id">日程逻辑主键。</param>
/// <param name="TenantId">来源实际租户；Host 为 null。</param>
/// <param name="OwnerUserId">来源实际所有者。</param>
/// <param name="Version">参与输入时的准确业务版本。</param>
/// <param name="ProjectionHash">包含协议、身份、版本与全部允许字段的规范 SHA-256。</param>
public sealed record PersonalScheduleKnowledgeProof(string Kind, string Purpose, Guid Id,
    Guid? TenantId, Guid OwnerUserId, int Version, string ProjectionHash);

/// <summary>当次已授权的完整字段投影，不允许缓存为长期可读正文。</summary>
/// <param name="Proof">对应的服务器来源证明。</param>
/// <param name="Content">本人日程内容。</param>
/// <param name="StartAtUtc">计划开始时间。</param>
/// <param name="EndAtUtc">计划结束时间。</param>
/// <param name="Status">实际完成状态。</param>
public sealed record PersonalScheduleKnowledgeSnapshot(PersonalScheduleKnowledgeProof Proof,
    string Content, DateTimeOffset StartAtUtc, DateTimeOffset EndAtUtc, string Status);

/// <summary>Calendar 拥有的本人日程来源边界；只能在调用方本地事务外使用。</summary>
/// <remarks>权限、主体与租户来自当前交互会话。无权限、缺失或任一证明失效时整批返回 null/false；取消继续传播。</remarks>
public interface IPersonalScheduleKnowledgeSource
{
    /// <summary>读取最多八个准确对象的全部允许字段与来源证明，不返回部分批次。</summary>
    /// <param name="scheduleIds">非空、唯一的对象标识集合；不能指定其他主体或租户。</param>
    /// <param name="cancellationToken">取消当前读取与复核。</param>
    /// <returns>按请求顺序排列的完整只读快照；任一对象不可用则为 null。</returns>
    Task<IReadOnlyList<PersonalScheduleKnowledgeSnapshot>?> CaptureAsync(
        IReadOnlyCollection<Guid> scheduleIds, CancellationToken cancellationToken = default);

    /// <summary>按当前会话再次核证完整批次，包含未被答案引用的来源。</summary>
    /// <param name="proofs">由服务器先前捕获的准确证明；未知协议、伪造或重复均拒绝。</param>
    /// <param name="cancellationToken">取消当前复核。</param>
    /// <returns>每个对象当前仍可读且准确版本/摘要完全一致时为 true。</returns>
    Task<bool> VerifyAsync(IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs,
        CancellationToken cancellationToken = default);

    /// <summary>后台按原持久会话重新核证完整来源，只返回资格，不交付日程正文或替换当前交互会话。</summary>
    /// <param name="original">任务创建时由服务器保存的原会话；失效、权限变化或未知会话种类均拒绝。</param>
    /// <param name="proofs">原任务完整来源，所属人与可信租户必须与原会话完全一致。</param>
    /// <param name="cancellationToken">取消当前核证。</param>
    /// <returns>当前原会话与全部对象仍符合准确证明时为 true。</returns>
    Task<bool> VerifyBackgroundAsync(SessionBindingSnapshot original,
        IReadOnlyCollection<PersonalScheduleKnowledgeProof> proofs, CancellationToken cancellationToken = default);
}

namespace Full.NET.Modules.Files.Contracts;

using Full.NET.Abstractions.Results;

/// <summary>引用 claim 的稳定消费者模块键。</summary>
public static class HostFileReferenceClaimConsumerModules
{
    /// <summary>Document 模块消费者；用于文档版本关联文件的 claim 生命周期。</summary>
    public const string Document = "document";

    /// <summary>Workflow 模块消费者；用于表单提交附件的 claim 生命周期。</summary>
    public const string Workflow = "workflow";

    /// <summary>Identity 模块消费者；用于用户头像与签名的 claim 生命周期。</summary>
    public const string Identity = "identity";

    /// <summary>Tenancy 模块消费者；用于租户 Logo 的 claim 生命周期。</summary>
    public const string Tenancy = "tenancy";

    /// <summary>Notifications 模块消费者；用于邮件 Intent 附件的 claim 生命周期。</summary>
    public const string Notifications = "notifications";
}

/// <summary>引用 claim 状态机。</summary>
public static class HostFileReferenceClaimStates
{
    /// <summary>已创建但消费者尚未确认；超龄未确认的 Pending claim 可由对账扫描回收。</summary>
    public const string Pending = "pending";

    /// <summary>消费者已确认持有；只要存在 Active claim，Files 不得物理删除对应文件。</summary>
    public const string Active = "active";

    /// <summary>已释放；文件引用计数减一，claim 进入只读历史。</summary>
    public const string Released = "released";
}

/// <summary>
/// 引用 claim 幂等键工厂；生成的幂等键格式为稳定机器码的一部分。
/// </summary>
public static class HostFileReferenceClaimIdempotencyKeys
{
    /// <summary>
    /// 为 Document 模块的某个版本生成稳定幂等键；同一 versionId 多次调用返回值相同。
    /// </summary>
    /// <param name="versionId">文档版本标识。</param>
    /// <returns>形如 "document-version:xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" 的幂等键。</returns>
    public static string DocumentVersion(Guid versionId) => $"document-version:{versionId:D}";

    /// <summary>
    /// 为 Workflow 表单提交附件生成稳定幂等键；同一提交与文件组合多次调用返回值相同。
    /// </summary>
    /// <param name="submissionId">表单提交标识。</param>
    /// <param name="fileId">附件文件标识。</param>
    /// <returns>形如 "workflow-form-submission-attachment:{submissionId}:{fileId}" 的幂等键。</returns>
    public static string WorkflowFormSubmissionAttachment(Guid submissionId, Guid fileId) =>
        $"workflow-form-submission-attachment:{submissionId:D}:{fileId:D}";

    /// <summary>为 Identity 用户头像生成稳定幂等键。</summary>
    /// <param name="userId">Host 用户标识。</param>
    /// <param name="fileId">头像文件标识。</param>
    public static string IdentityUserAvatar(Guid userId, Guid fileId) =>
        $"identity-user-avatar:{userId:D}:{fileId:D}";

    /// <summary>为 Identity 用户签名生成稳定幂等键。</summary>
    /// <param name="userId">Host 用户标识。</param>
    /// <param name="fileId">签名文件标识。</param>
    public static string IdentityUserSignature(Guid userId, Guid fileId) =>
        $"identity-user-signature:{userId:D}:{fileId:D}";

    /// <summary>为 Tenancy 租户 Logo 生成稳定幂等键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="fileId">Logo 文件标识。</param>
    public static string TenancyTenantLogo(Guid tenantId, Guid fileId) =>
        $"tenancy-tenant-logo:{tenantId:D}:{fileId:D}";

    /// <summary>为一次头像绑定生成独立幂等键，延迟解绑不得影响重新绑定。</summary>
    public static string IdentityUserAvatar(Guid userId, Guid fileId, Guid bindingId) =>
        CreateBindingKey(IdentityUserAvatar(userId, fileId), bindingId);

    /// <summary>为一次签名绑定生成独立幂等键，保留旧双参数方法用于历史数据。</summary>
    public static string IdentityUserSignature(Guid userId, Guid fileId, Guid bindingId) =>
        CreateBindingKey(IdentityUserSignature(userId, fileId), bindingId);

    /// <summary>为一次 Logo 绑定生成独立幂等键，文件复用也不复用绑定身份。</summary>
    public static string TenancyTenantLogo(Guid tenantId, Guid fileId, Guid bindingId) =>
        CreateBindingKey(TenancyTenantLogo(tenantId, fileId), bindingId);

    /// <summary>解析本消费方自己的绑定键；拒绝跨资源键及非规范代际。</summary>
    public static bool IsBindingKey(string legacyKey, string candidate) =>
        !string.IsNullOrWhiteSpace(legacyKey) && !string.IsNullOrWhiteSpace(candidate)
        && (string.Equals(legacyKey, candidate, StringComparison.Ordinal)
            || IsGenerationKey(legacyKey, candidate));

    private static string CreateBindingKey(string legacyKey, Guid bindingId)
    {
        if (bindingId == Guid.Empty) throw new ArgumentException("Binding identifier is required.", nameof(bindingId));
        // 三段 UUID 使用规范 N 格式，键最长 123 字符，适配正式双库的 128 字符列。
        return $"{CompactBindingPrefix(legacyKey)}{bindingId:N}";
    }

    private static string CompactBindingPrefix(string legacyKey) =>
        legacyKey[..^73] + legacyKey[^73..].Replace("-", string.Empty, StringComparison.Ordinal) + ":b:";

    private static bool IsGenerationKey(string legacyKey, string candidate)
    {
        // 旧双参数键使用两个 D 格式 UUID；只压缩标识部分，保留消费方前缀。
        if (legacyKey.Length <= 73 || legacyKey[^37] != ':'
            || !Guid.TryParseExact(legacyKey.AsSpan(legacyKey.Length - 73, 36), "D", out _)
            || !Guid.TryParseExact(legacyKey.AsSpan(legacyKey.Length - 36, 36), "D", out _)) return false;
        var prefix = CompactBindingPrefix(legacyKey);
        if (candidate is null || candidate.Length != prefix.Length + 32
            || !candidate.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var identifier = candidate[prefix.Length..];
        // 规范小写十六进制避免正式数据库不区分大小写的索引把两种代际别名视为同一键。
        return Guid.TryParseExact(identifier, "N", out var bindingId) && bindingId != Guid.Empty
            && string.Equals(identifier, bindingId.ToString("N"), StringComparison.Ordinal);
    }

    /// <summary>为 Notifications 邮件 Intent 附件生成稳定幂等键。</summary>
    /// <param name="intentId">通知意图标识。</param>
    /// <param name="fileId">附件文件标识。</param>
    public static string NotificationIntentAttachment(Guid intentId, Guid fileId) =>
        $"notification-intent-attachment:{intentId:D}:{fileId:D}";
}

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>
/// 创建文件引用 claim 的请求契约；IdempotencyKey 保证重复提交的幂等性。
/// </summary>
/// <param name="IdempotencyKey">消费者模块内唯一的幂等键，最多 128 字符；建议通过 HostFileReferenceClaimIdempotencyKeys 生成。</param>
/// <param name="ConsumerModule">消费者模块稳定键，最多 64 字符，取值自 HostFileReferenceClaimConsumerModules。</param>
/// <param name="ConsumerReferenceId">消费者侧业务引用标识，如 DocumentVersionId。</param>
/// <param name="FileId">Files 模块中目标文件标识。</param>
public sealed record HostFileReferenceClaimRequest(
    string IdempotencyKey,
    string ConsumerModule,
    Guid ConsumerReferenceId,
    Guid FileId);

/// <remarks>
/// 机器码稳定性：字段顺序与权限码/错误码字符串发布后不可改名或删除，新增只能追加。
/// </remarks>
/// <summary>
/// claim 操作结果，承载 claim 标识、当前状态与就绪文件元数据。
/// </summary>
/// <param name="ClaimId">claim 行标识。</param>
/// <param name="State">claim 状态稳定机器码，取值自 HostFileReferenceClaimStates。</param>
/// <param name="FileReference">就绪文件的最小元数据；只有 Active/Pending claim 会返回非 null 的 FileReference。</param>
public sealed record HostFileReferenceClaimResult(
    Guid ClaimId,
    string State,
    HostFileReference FileReference);

/// <summary>
/// 引用探测结果枚举；作为对账扫描中消费者是否仍持有的权威回答。
/// </summary>
public enum HostFileReferenceClaimProbeOutcome
{
    /// <summary>存在：消费者侧引用仍然有效，Files 应将 claim 保持 Active。</summary>
    Exists = 0,

    /// <summary>不存在：消费者侧引用已清理或从未存在，Files 可安全释放对应 Pending claim。</summary>
    NotFound = 1,

    /// <summary>探测失败：消费者实现抛出未处理异常或超时，Files 保留 claim 并在下轮对账重试。</summary>
    Failed = 2,
}

/// <summary>
/// 引用探测结果封装。
/// </summary>
/// <param name="Outcome">探测结论枚举值。</param>
public sealed record HostFileReferenceClaimProbeResult(HostFileReferenceClaimProbeOutcome Outcome);

/// <summary>
/// 文件引用 claim 服务；提供跨模块安全 claim-confirm-release 三段式生命周期。
/// </summary>
/// <remarks>
/// 典型时序：Claim（创建 Pending，文件被临时保留）→ 消费者写入自身引用 → Confirm（转为 Active）；
/// Pending 可由超龄对账补偿；Active 仅对不可复用引用的声明探测补偿，其他消费者须可靠发布释放事实。
/// </remarks>
public interface IHostFileReferenceClaimService
{
    /// <summary>
    /// 申请一个引用 claim；同 IdempotencyKey 重复调用幂等返回首次成功结果。
    /// </summary>
    /// <param name="request">申请请求，含幂等键、消费者标识与目标文件标识。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>成功时返回 claim 详情；失败时返回 Result 错误，如文件不存在。</returns>
    Task<Result<HostFileReferenceClaimResult>> ClaimAsync(
        HostFileReferenceClaimRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 确认已持有的 claim，将状态从 Pending 提升为 Active；重复 Confirm 幂等。
    /// </summary>
    /// <param name="idempotencyKey">与 ClaimAsync 相同的幂等键。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>成功时返回更新后的 claim 详情。</returns>
    Task<Result<HostFileReferenceClaimResult>> ConfirmAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 主动释放 claim；消费者业务引用删除后应立即调用，释放对应文件的保留计数。
    /// </summary>
    /// <param name="idempotencyKey">与 ClaimAsync 相同的幂等键。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>true 表示本次或此前已完成释放；缺失 claim 返回失败 Result。</returns>
    Task<Result<bool>> ReleaseAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断指定文件是否仍存在任一未释放的 claim（Pending 或 Active）；供物理删除前的保护判断。
    /// </summary>
    /// <param name="fileId">目标文件标识。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    /// <returns>true 表示仍有开放 claim，禁止物理删除；false 表示可安全回收。</returns>
    Task<bool> HasOpenClaimsAsync(
        Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>消费方注册的精确引用探测，供 Files 对账超龄 Pending claim。</summary>
public interface IHostFileReferenceClaimProbe
{
    /// <summary>
    /// 该探测实现归属的消费者模块稳定键；Files 按此键将探测请求路由到正确实现。
    /// </summary>
    string ConsumerModule { get; }

    /// <summary>
    /// 按消费者引用标识探测引用是否仍存在；实现必须无副作用且具备最终一致性视角。
    /// </summary>
    /// <param name="consumerReferenceId">消费者侧业务引用标识，如 DocumentVersionId。</param>
    /// <param name="fileId">关联文件标识，用于跨校验。</param>
    /// <param name="cancellationToken">用于取消探测的令牌。</param>
    /// <returns>探测结果结论。</returns>
    Task<HostFileReferenceClaimProbeResult> ProbeReferenceAsync(
        Guid consumerReferenceId,
        Guid fileId,
        CancellationToken cancellationToken = default);
}

/// <summary>可复用绑定的 Pending 探测；只有当前有效绑定键匹配扫描键才允许确认。</summary>
/// <remarks>可选扩展不改变其他消费者；历史双参数键由归属模块按自身绑定记录兼容。</remarks>
public interface IHostFileReferenceBindingClaimProbe : IHostFileReferenceClaimProbe
{
    /// <summary>精确核对一个消费方、文件和绑定代际，不将另一代际的存在视为当前 Claim 有效。</summary>
    /// <param name="consumerReferenceId">消费方业务标识。</param>
    /// <param name="fileId">扫描时的文件标识。</param>
    /// <param name="idempotencyKey">扫描记录的实际绑定键。</param>
    /// <param name="cancellationToken">取消权威读取。</param>
    Task<HostFileReferenceClaimProbeResult> ProbeBindingAsync(Guid consumerReferenceId, Guid fileId,
        string idempotencyKey, CancellationToken cancellationToken = default);
}

/// <summary>主动声明消费者引用身份不可复用，允许 Files 对账历史 Active 孤儿的只读探测。</summary>
/// <remarks>
/// 同一 ConsumerReferenceId/FileId 一旦不存在，官方写入流程不得重新创建该引用。
/// 软删除或可恢复的事实必须回答 Exists；暂时不可见、未同步或探测错误必须回答 Failed。
/// 可反复更换头像、Logo 等复用身份的消费者不得实现本标记，普通探测的行为保持不变。
/// </remarks>
public interface IHostImmutableFileReferenceClaimProbe : IHostFileReferenceClaimProbe
{
    /// <summary>对一个所有者的一批引用一次权威读取；缺失回答视为 Failed，禁止逐条回退。</summary>
    /// <param name="requests">最多 1000 项且 ClaimId 唯一的扫描快照。</param>
    /// <param name="cancellationToken">取消权威读取。</param>
    /// <returns>按 ClaimId 关联的明确结果。</returns>
    Task<IReadOnlyDictionary<Guid, HostFileReferenceClaimProbeResult>> ProbeReferencesAsync(
        IReadOnlyList<HostFileReferenceClaimProbeRequest> requests, CancellationToken cancellationToken = default);
}

/// <summary>引用探测快照；ClaimId 关联回答，业务版本和文件标识共同确认归属。</summary>
public readonly record struct HostFileReferenceClaimProbeRequest(Guid ClaimId, Guid ConsumerReferenceId, Guid FileId);

namespace Full.NET.Modules.Files.Contracts;

using Full.NET.Abstractions.Results;

/// <summary>引用 claim 的稳定消费者模块键。</summary>
public static class HostFileReferenceClaimConsumerModules
{
    /// <summary>Document 模块消费者；用于文档版本关联文件的 claim 生命周期。</summary>
    public const string Document = "document";
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
}

/// <summary>
/// 创建文件引用 claim 的请求契约；IdempotencyKey 保证重复提交的幂等性。
/// </summary>
/// <param name="IdempotencyKey">消费者模块内唯一的幂等键；建议通过 HostFileReferenceClaimIdempotencyKeys 生成。</param>
/// <param name="ConsumerModule">消费者模块稳定键，取值自 HostFileReferenceClaimConsumerModules。</param>
/// <param name="ConsumerReferenceId">消费者侧业务引用标识，如 DocumentVersionId。</param>
/// <param name="FileId">Files 模块中目标文件标识。</param>
public sealed record HostFileReferenceClaimRequest(
    string IdempotencyKey,
    string ConsumerModule,
    Guid ConsumerReferenceId,
    Guid FileId);

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
/// 任何阶段失败都会在超时后进入对账扫描，不会永久泄漏引用计数。
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
    /// <returns>true 表示本次调用真正完成状态迁移，false 表示已被提前释放。</returns>
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

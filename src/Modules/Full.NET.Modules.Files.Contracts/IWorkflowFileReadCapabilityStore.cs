using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Files.Contracts;

/// <summary>Workflow先授权业务资源，Files再核对持有、接收作用域、原文件状态及期限；所有调用均在Workflow事务外。</summary>
public interface IWorkflowFileReadCapabilityStore
{
    /// <summary>按调用、方向、字段和本地文件幂等签发；Host要求活动Workflow引用，Tenant要求精确本地所有权。</summary>
    /// <param name="request">由可信Workflow内核从实际历史引用派生的请求，不接收HTTP透传声明。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<WorkflowFileReadCapability>> IssueAsync(WorkflowFileReadCapabilityIssue request, CancellationToken cancellationToken = default);

    /// <summary>只向当前可信接收作用域返回有效能力的安全元数据。</summary>
    /// <param name="capabilityId">不透明能力身份。</param>
    /// <param name="callId">已授权Workflow资源的固定调用身份。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WorkflowFileReadCapability?> DescribeAsync(Guid capabilityId, Guid callId, CancellationToken cancellationToken = default);

    /// <summary>复核与描述相同的约束后打开内容；调用方负责释放流，不返回原文件Id或物理路径。</summary>
    /// <param name="capabilityId">不透明能力身份。</param>
    /// <param name="callId">已授权Workflow资源的固定调用身份。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<WorkflowFileReadContent>> OpenAsync(Guid capabilityId, Guid callId, CancellationToken cancellationToken = default);

    /// <summary>仅签发方可解析自己的原引用，用于源文件原样回传；接收方不能借此获得外租户FileId。</summary>
    /// <param name="capabilityId">原签发能力。</param>
    /// <param name="callId">原调用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<WorkflowIssuedFileReference?> ResolveIssuedAsync(Guid capabilityId, Guid callId, CancellationToken cancellationToken = default);

    /// <summary>只撤销当前签发作用域的能力，每次最多25个；返回false时调用方继续恢复，不删除原文件。</summary>
    /// <param name="callId">不再共享内容的调用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> ReleaseIssuedAsync(Guid callId, CancellationToken cancellationToken = default);
}

/// <summary>本地文件和所有权引用仅在签发方内部使用；方向只允许input/result，接收租户为null表示Host。</summary>
/// <param name="CallId">耐久调用身份。</param>
/// <param name="DirectionKey">协议方向。</param>
/// <param name="FieldKey">接收方固定字段键。</param>
/// <param name="FileId">签发方本地文件。</param>
/// <param name="OwnerReferenceId">Host活动claim消费者引用，或Tenant资源所有者。</param>
/// <param name="RecipientTenantId">可信协议派生的接收作用域。</param>
/// <param name="ExpiresAtUtc">不得超过协议授权期限。</param>
public sealed record WorkflowFileReadCapabilityIssue(Guid CallId, string DirectionKey, string FieldKey, Guid FileId,
    Guid OwnerReferenceId, Guid? RecipientTenantId, DateTimeOffset ExpiresAtUtc);

/// <summary>接收方可见的最小元数据，不包含外租户原文件身份和存储定位。</summary>
/// <param name="Id">能力身份。</param>
/// <param name="CallId">调用身份。</param>
/// <param name="DirectionKey">协议方向。</param>
/// <param name="FieldKey">最初接收字段。</param>
/// <param name="OriginalFileName">安全下载名。</param>
/// <param name="ContentType">内容类型。</param>
/// <param name="SizeBytes">实际文件长度。</param>
/// <param name="ContentHash">固定内容SHA256摘要。</param>
/// <param name="ExpiresAtUtc">读取期限。</param>
public sealed record WorkflowFileReadCapability(Guid Id, Guid CallId, string DirectionKey, string FieldKey,
    string OriginalFileName, string ContentType, long SizeBytes, string ContentHash, DateTimeOffset ExpiresAtUtc);

/// <summary>只供原签发作用域解析本地所有权，不能作为接收方描述返回。</summary>
/// <param name="FileId">签发方自己的文件。</param>
/// <param name="OwnerReferenceId">原所有权引用。</param>
/// <param name="Capability">固定安全元数据。</param>
public sealed record WorkflowIssuedFileReference(Guid FileId, Guid OwnerReferenceId, WorkflowFileReadCapability Capability);

/// <summary>仅包含调用方须释放的读取流及安全元数据。</summary>
/// <param name="Content">只读流。</param>
/// <param name="Capability">固定安全元数据。</param>
public sealed record WorkflowFileReadContent(Stream Content, WorkflowFileReadCapability Capability);

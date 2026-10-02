namespace Full.NET.Modules.Ai.Contracts;

/// <summary>创建写工具审批；参数由服务端冻结，客户端不能指定审批人。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="RunId">关联的 Agent 运行标识；服务端冻结，不可篡改。</param>
/// <param name="OperationId">操作标识；用于幂等去重，同一 OperationId 重复请求视为同一审批。</param>
/// <param name="ToolName">工具稳定名称；发布后不可改名。</param>
/// <param name="ToolVersion">工具版本号；用于校验参数 schema 兼容性。</param>
/// <param name="ArgumentsJson">工具调用参数 JSON；服务端按 ToolVersion schema 校验。</param>
public sealed record CreateAiAgentApprovalRequest(
    Guid RunId,
    Guid OperationId,
    string ToolName,
    int ToolVersion,
    string ArgumentsJson);

/// <summary>审批创建响应。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="ApprovalId">新建审批的唯一标识。</param>
public sealed record CreateAiAgentApprovalResponse(Guid ApprovalId);

/// <summary>审批决定请求。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Approve"><see langword="true"/> 表示批准，<see langword="false"/> 表示拒绝。</param>
/// <param name="ExpectedVersion">CAS 乐观并发期望值；必须等于当前审批版本，否则决定失败。</param>
public sealed record DecideAiAgentApprovalRequest(bool Approve, long ExpectedVersion);

/// <summary>可读审批摘要，包含人能理解的动作描述。</summary>
/// <remarks>字段顺序发布后不可调整；新增字段只能追加到末尾，以保持线格式兼容。</remarks>
/// <param name="Id">审批唯一标识。</param>
/// <param name="RunId">关联的 Agent 运行标识。</param>
/// <param name="OperationId">操作标识；用于幂等去重。</param>
/// <param name="ToolName">工具稳定名称。</param>
/// <param name="ToolVersion">工具版本号。</param>
/// <param name="ArgumentsHash">工具参数哈希；用于检测参数篡改。</param>
/// <param name="DecisionKey">决策键；标识审批结论的稳定机器码。</param>
/// <param name="ActionSummary">人能理解的动作摘要。</param>
/// <param name="TargetSummary">操作目标摘要；说明影响的对象。</param>
/// <param name="ChangeSummary">变更摘要；说明产生或修改的内容。</param>
/// <param name="ScopeSummary">影响范围摘要；说明波及的数据或系统。</param>
/// <param name="CostCeiling">成本上限；可选，超出时拒绝执行。</param>
/// <param name="Currency">成本币种；CostCeiling 存在时必填。</param>
/// <param name="ExpiresAtUtc">审批过期时间（UTC）；过期后不可消费。</param>
/// <param name="ConsumedAtUtc">审批消费时间（UTC）；未消费时为 <see langword="null"/>。</param>
/// <param name="Version">CAS 乐观并发版本号。</param>
/// <param name="CreatedAtUtc">审批创建时间（UTC）。</param>
public sealed record AiAgentApprovalResponse(
    Guid Id,
    Guid RunId,
    Guid OperationId,
    string ToolName,
    int ToolVersion,
    string ArgumentsHash,
    string DecisionKey,
    string ActionSummary,
    string TargetSummary,
    string ChangeSummary,
    string ScopeSummary,
    decimal? CostCeiling,
    string? Currency,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    long Version,
    DateTimeOffset CreatedAtUtc);

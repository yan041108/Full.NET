namespace Full.NET.Modules.Workflow.Contracts;

/// <summary>当前可信作用域可发布的实际业务动作；不包含目标记录或业务快照。</summary>
public sealed record WorkflowBusinessActionCatalogResponse(IReadOnlyList<WorkflowBusinessActionCatalogItemResponse> Items);

/// <summary>固定动作版本和摘要用于发布绑定；放置约束由服务端再次验证。</summary>
public sealed record WorkflowBusinessActionCatalogItemResponse(string OwnerKey, string ActionKey, int ActionVersion,
    string BusinessType, string PlacementKey, string ActionHash);

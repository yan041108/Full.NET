using System.Text.Json;
using Full.NET.Modules.Workflow.Persistence;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>单次推进显式携带已验证表单值及同实例已完成活动的真实处理事实，不存储于可变环境状态。</summary>
internal sealed record WorkflowAssigneeRuntimeContext(IReadOnlyDictionary<string, JsonElement> Values,
    IReadOnlyDictionary<string, IReadOnlyList<WorkflowPreviousActorRecord>> PreviousActors,
    WorkflowStarterSelectionResolution? StarterSelections = null,
    IReadOnlyList<Guid>? FlowAdministratorUserIds = null);

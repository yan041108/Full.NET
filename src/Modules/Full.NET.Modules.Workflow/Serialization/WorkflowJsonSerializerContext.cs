using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.Modules.Workflow.Serialization;

/// <summary>为工作流静态闭包类型提供 Native AOT JSON 元数据。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WorkflowDefinitionDraft))]
[JsonSerializable(typeof(WorkflowFormSchema))]
[JsonSerializable(typeof(StartWorkflowCommand))]
[JsonSerializable(typeof(ActOnWorkflowTodoCommand))]
internal partial class WorkflowJsonSerializerContext : JsonSerializerContext;

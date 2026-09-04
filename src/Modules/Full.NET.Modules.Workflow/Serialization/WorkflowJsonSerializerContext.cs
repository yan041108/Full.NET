using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Features.ManageForms;
using Full.NET.Modules.Workflow.Features.ManageInstances;
using Full.NET.Modules.Workflow.Features.ManageMyTodos;
using Full.NET.Modules.Workflow.Features.ManageMyCc;

namespace Full.NET.Modules.Workflow.Serialization;

/// <summary>为工作流静态闭包类型提供 Native AOT JSON 元数据。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(WorkflowDefinitionDraft))]
[JsonSerializable(typeof(WorkflowFormSchema))]
[JsonSerializable(typeof(StartWorkflowCommand))]
[JsonSerializable(typeof(ActOnWorkflowTodoCommand))]
[JsonSerializable(typeof(CreateWorkflowFormRequest))]
[JsonSerializable(typeof(UpdateWorkflowFormDraftRequest))]
[JsonSerializable(typeof(PublishWorkflowFormRequest))]
[JsonSerializable(typeof(WorkflowFormComponentCatalogResponse))]
[JsonSerializable(typeof(WorkflowFormComponentResponse))]
[JsonSerializable(typeof(WorkflowFormResponse))]
[JsonSerializable(typeof(WorkflowFormResponse[]))]
[JsonSerializable(typeof(WorkflowFormVersionResponse))]
[JsonSerializable(typeof(CreateWorkflowDefinitionRequest))]
[JsonSerializable(typeof(UpdateWorkflowDefinitionDraftRequest))]
[JsonSerializable(typeof(PublishWorkflowDefinitionRequest))]
[JsonSerializable(typeof(WorkflowNodeTypeCatalogResponse))]
[JsonSerializable(typeof(WorkflowNodeTypeResponse))]
[JsonSerializable(typeof(WorkflowRecipientCandidateResponse))]
[JsonSerializable(typeof(WorkflowRecipientCandidateResponse[]))]
[JsonSerializable(typeof(WorkflowRecipientCandidatePageResponse))]
[JsonSerializable(typeof(WorkflowDefinitionResponse))]
[JsonSerializable(typeof(WorkflowDefinitionResponse[]))]
[JsonSerializable(typeof(WorkflowDefinitionVersionResponse))]
[JsonSerializable(typeof(WorkflowDefinitionVersionResponse[]))]
[JsonSerializable(typeof(StartWorkflowInstanceRequest))]
[JsonSerializable(typeof(CancelWorkflowInstanceRequest))]
[JsonSerializable(typeof(WorkflowInstanceResponse))]
[JsonSerializable(typeof(WorkflowTodoResponse))]
[JsonSerializable(typeof(WorkflowTodoResponse[]))]
[JsonSerializable(typeof(WorkflowTodoDetailResponse))]
[JsonSerializable(typeof(WorkflowTodoRuntimeResponse))]
[JsonSerializable(typeof(ActWorkflowTodoRequest))]
[JsonSerializable(typeof(WorkflowExecutionLogResponse))]
[JsonSerializable(typeof(WorkflowExecutionLogResponse[]))]
[JsonSerializable(typeof(WorkflowCcResponse))]
[JsonSerializable(typeof(WorkflowCcResponse[]))]
[JsonSerializable(typeof(WorkflowCcReadResponse))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class WorkflowJsonSerializerContext : JsonSerializerContext;

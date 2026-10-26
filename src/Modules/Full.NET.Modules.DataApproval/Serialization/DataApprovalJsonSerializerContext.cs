using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;

// DataApproval 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.DataApproval.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Full.NET.Modules.Workflow.Contracts.WorkflowBusinessCommandResultIntegrationEvent))]
[JsonSerializable(typeof(RetryDataApprovalRequestBody))]
[JsonSerializable(typeof(CancelDataApprovalRequestBody))]
[JsonSerializable(typeof(CreateDataApprovalRequestBody))]
[JsonSerializable(typeof(DataApprovalRequestResponse))]
[JsonSerializable(typeof(DataApprovalScenarioResponse))]
[JsonSerializable(typeof(UpdateDataApprovalScenarioBindingBody))]
[JsonSerializable(typeof(PagedResult<DataApprovalRequestResponse>))]
[JsonSerializable(typeof(DataApprovalScenarioResponse[]))]
/// <summary>DataApproval 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class DataApprovalJsonSerializerContext : JsonSerializerContext;

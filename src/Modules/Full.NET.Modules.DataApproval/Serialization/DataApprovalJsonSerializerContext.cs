using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.DataApproval.Contracts;

namespace Full.NET.Modules.DataApproval.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RetryDataApprovalRequestBody))]
[JsonSerializable(typeof(CancelDataApprovalRequestBody))]
[JsonSerializable(typeof(CreateDataApprovalRequestBody))]
[JsonSerializable(typeof(DataApprovalRequestResponse))]
[JsonSerializable(typeof(DataApprovalScenarioResponse))]
[JsonSerializable(typeof(UpdateDataApprovalScenarioBindingBody))]
[JsonSerializable(typeof(PagedResult<DataApprovalRequestResponse>))]
[JsonSerializable(typeof(DataApprovalScenarioResponse[]))]
// OpenAPI 使用端点声明的集合接口类型，数组元数据不能替代集合根类型。
[JsonSerializable(typeof(IReadOnlyList<DataApprovalScenarioResponse>))]
internal partial class DataApprovalJsonSerializerContext : JsonSerializerContext;

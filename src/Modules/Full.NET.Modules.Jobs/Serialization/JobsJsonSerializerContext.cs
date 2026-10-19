using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Jobs.Contracts;

// Jobs 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.Jobs.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(HttpJobArgs))]
[JsonSerializable(typeof(HttpJobSecretHeaderRef))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, HttpJobSecretHeaderRef>))]
[JsonSerializable(typeof(HostJobDefinitionResponse))]
[JsonSerializable(typeof(PagedResult<HostJobDefinitionResponse>))]
[JsonSerializable(typeof(CreateHostJobDefinitionRequest))]
[JsonSerializable(typeof(UpdateHostJobDefinitionRequest))]
[JsonSerializable(typeof(DisableHostJobDefinitionRequest))]
[JsonSerializable(typeof(DeleteHostJobDefinitionRequest))]
[JsonSerializable(typeof(HostJobGroupResponse))]
[JsonSerializable(typeof(IReadOnlyList<HostJobGroupResponse>))]
[JsonSerializable(typeof(HostJobScheduleResponse))]
[JsonSerializable(typeof(PagedResult<HostJobScheduleResponse>))]
[JsonSerializable(typeof(CreateHostJobScheduleRequest))]
[JsonSerializable(typeof(UpdateHostJobScheduleRequest))]
[JsonSerializable(typeof(ChangeHostJobScheduleStateRequest))]
[JsonSerializable(typeof(BatchChangeHostJobScheduleStateRequest))]
[JsonSerializable(typeof(BatchChangeHostJobScheduleStateItem))]
[JsonSerializable(typeof(BatchChangeHostJobScheduleStateResponse))]
[JsonSerializable(typeof(BatchChangeHostJobScheduleStateResultItem))]
[JsonSerializable(typeof(IReadOnlyList<BatchChangeHostJobScheduleStateItem>))]
[JsonSerializable(typeof(IReadOnlyList<BatchChangeHostJobScheduleStateResultItem>))]
[JsonSerializable(typeof(HostJobScheduleDefinitionOptionResponse))]
[JsonSerializable(typeof(IReadOnlyList<HostJobScheduleDefinitionOptionResponse>))]
[JsonSerializable(typeof(IReadOnlyList<DateTimeOffset>))]
[JsonSerializable(typeof(HostJobScheduleCronPreviewResponse))]
[JsonSerializable(typeof(HostJobExecutionResponse))]
[JsonSerializable(typeof(PagedResult<HostJobExecutionResponse>))]
[JsonSerializable(typeof(HostJobHealthResponse))]
[JsonSerializable(typeof(IReadOnlyList<HostJobWorkerInstanceResponse>))]
/// <summary>Jobs 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class JobsJsonSerializerContext : JsonSerializerContext;

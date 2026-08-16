using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
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
[JsonSerializable(typeof(HostJobScheduleDefinitionOptionResponse))]
[JsonSerializable(typeof(IReadOnlyList<HostJobScheduleDefinitionOptionResponse>))]
[JsonSerializable(typeof(IReadOnlyList<DateTimeOffset>))]
[JsonSerializable(typeof(HostJobScheduleCronPreviewResponse))]
[JsonSerializable(typeof(HostJobExecutionResponse))]
[JsonSerializable(typeof(PagedResult<HostJobExecutionResponse>))]
[JsonSerializable(typeof(HostJobHealthResponse))]
[JsonSerializable(typeof(IReadOnlyList<HostJobWorkerInstanceResponse>))]
internal partial class JobsJsonSerializerContext : JsonSerializerContext;

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
[JsonSerializable(typeof(HostJobExecutionResponse))]
[JsonSerializable(typeof(PagedResult<HostJobExecutionResponse>))]
internal partial class JobsJsonSerializerContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;

namespace Full.NET.Modules.SerialNumbers.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChangeSerialNumberRuleStatusRequest))]
[JsonSerializable(typeof(CreateSerialNumberRuleRequest))]
[JsonSerializable(typeof(PagedResult<SerialNumberRuleResponse>))]
[JsonSerializable(typeof(PreviewSerialNumberRequest))]
[JsonSerializable(typeof(SerialNumberPreviewResponse))]
[JsonSerializable(typeof(SerialNumberRuleResponse))]
[JsonSerializable(typeof(UpdateSerialNumberRuleRequest))]
internal partial class SerialNumbersJsonSerializerContext
    : JsonSerializerContext;

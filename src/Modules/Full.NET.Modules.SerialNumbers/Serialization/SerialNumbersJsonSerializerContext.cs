using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.SerialNumbers.Contracts;

// SerialNumbers 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.SerialNumbers.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChangeSerialNumberRuleStatusRequest))]
[JsonSerializable(typeof(CreateSerialNumberRuleRequest))]
[JsonSerializable(typeof(PagedResult<SerialNumberRuleResponse>))]
[JsonSerializable(typeof(PreviewSerialNumberRequest))]
[JsonSerializable(typeof(SerialNumberPreviewResponse))]
[JsonSerializable(typeof(SerialNumberRuleResponse))]
[JsonSerializable(typeof(SerialRuleFieldChange))]
[JsonSerializable(typeof(SerialRuleFieldChange[]))]
[JsonSerializable(typeof(SerialRuleUpdateApprovalPreviewResponse))]
[JsonSerializable(typeof(SerialRuleUpdateApprovalSubmissionResponse))]
[JsonSerializable(typeof(SubmitSerialRuleUpdateApprovalRequest))]
[JsonSerializable(typeof(SerialRuleDisableApprovalPreviewResponse))]
[JsonSerializable(typeof(SerialRuleDisableApprovalSubmissionResponse))]
[JsonSerializable(typeof(SubmitSerialRuleDisableApprovalRequest))]
[JsonSerializable(typeof(UpdateSerialNumberRuleRequest))]
/// <summary>SerialNumbers 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class SerialNumbersJsonSerializerContext
    : JsonSerializerContext;

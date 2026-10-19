using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Calendar.Contracts;

// Calendar 模块 Native AOT 友好的 System.Text.Json 源生成上下文；仅注册已审计的契约类型。
namespace Full.NET.Modules.Calendar.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChangePersonalScheduleRequest))]
[JsonSerializable(typeof(CreatePersonalScheduleRequest))]
[JsonSerializable(typeof(PagedResult<PersonalScheduleResponse>))]
[JsonSerializable(typeof(PersonalScheduleResponse))]
[JsonSerializable(typeof(SetPersonalScheduleStatusRequest))]
[JsonSerializable(typeof(UpdatePersonalScheduleRequest))]
/// <summary>Calendar 模块 JSON 源生成上下文；Native AOT 绑定已审计契约，禁止反射序列化。</summary>
internal partial class CalendarJsonSerializerContext
    : JsonSerializerContext;

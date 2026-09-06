using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.Modules.Calendar.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ChangePersonalScheduleRequest))]
[JsonSerializable(typeof(CreatePersonalScheduleRequest))]
[JsonSerializable(typeof(PagedResult<PersonalScheduleResponse>))]
[JsonSerializable(typeof(PersonalScheduleResponse))]
[JsonSerializable(typeof(SetPersonalScheduleStatusRequest))]
[JsonSerializable(typeof(UpdatePersonalScheduleRequest))]
internal partial class CalendarJsonSerializerContext
    : JsonSerializerContext;

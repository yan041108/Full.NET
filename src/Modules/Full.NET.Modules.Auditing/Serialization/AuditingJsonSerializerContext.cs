using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Auditing.Contracts;

namespace Full.NET.Modules.Auditing.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AccessLogResponse))]
[JsonSerializable(typeof(PagedResult<AccessLogResponse>))]
[JsonSerializable(typeof(AccessLogCursorPageResponse))]
[JsonSerializable(typeof(OperationLogResponse))]
[JsonSerializable(typeof(PagedResult<OperationLogResponse>))]
[JsonSerializable(typeof(ExceptionLogResponse))]
[JsonSerializable(typeof(PagedResult<ExceptionLogResponse>))]
internal partial class AuditingJsonSerializerContext : JsonSerializerContext;

using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.ObservabilityAdmin.Features.ManageLogFiles;

namespace Full.NET.Modules.ObservabilityAdmin.Serialization;

/// <summary>为 Host 日志控制面提供 Native AOT 静态 JSON 元数据。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LogFileSummary))]
[JsonSerializable(typeof(IReadOnlyList<LogFileSummary>))]
[JsonSerializable(typeof(LogFileTail))]
internal partial class ObservabilityAdminJsonSerializerContext
    : JsonSerializerContext;

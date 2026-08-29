using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Host.Worker;

/// <summary>Worker 命令行错误的稳定机器输出。</summary>
/// <param name="Code">不包含敏感信息的稳定错误码。</param>
internal sealed record WorkerErrorResponse(string Code);

/// <summary>Worker 控制台协议的源生成 JSON 元数据。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(OutboxVersionRetirementReport))]
[JsonSerializable(typeof(WorkerErrorResponse))]
internal partial class WorkerJsonSerializerContext : JsonSerializerContext;

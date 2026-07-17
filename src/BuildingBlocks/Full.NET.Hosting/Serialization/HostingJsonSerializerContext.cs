using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;

namespace Full.NET.Hosting.Serialization;

/// <summary>
/// 为 Hosting 层公开错误传输契约提供 System.Text.Json 源生成元数据。
/// </summary>
[JsonSerializable(typeof(ValidationViolation))]
[JsonSerializable(typeof(ValidationViolation[]))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
public sealed partial class HostingJsonSerializerContext : JsonSerializerContext;

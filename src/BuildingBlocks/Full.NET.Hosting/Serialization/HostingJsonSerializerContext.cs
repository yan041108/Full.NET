using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;

namespace Full.NET.Hosting.Serialization;

/// <summary>
/// 为 Hosting 层公开错误传输契约提供 System.Text.Json 源生成元数据。
/// </summary>
[JsonSerializable(typeof(Error))]
[JsonSerializable(typeof(ValidationViolation))]
[JsonSerializable(typeof(ValidationViolation[]))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
// ProblemDetails 扩展按具体运行类型序列化；显式登记以消除 IDictionary/IReadOnlyDictionary 最近祖先歧义。
[JsonSerializable(typeof(Dictionary<string, string[]>))]
public sealed partial class HostingJsonSerializerContext : JsonSerializerContext;

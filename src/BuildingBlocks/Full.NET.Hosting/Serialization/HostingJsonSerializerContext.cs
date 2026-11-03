using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Microsoft.AspNetCore.Http;

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
// OpenAPI 也会导出 multipart/原始流声明类型；它们不经过普通 JSON 请求绑定，仍须具备静态 Schema 元数据。
[JsonSerializable(typeof(IFormFile))]
[JsonSerializable(typeof(IFormFileCollection))]
[JsonSerializable(typeof(Stream))]
public sealed partial class HostingJsonSerializerContext : JsonSerializerContext;

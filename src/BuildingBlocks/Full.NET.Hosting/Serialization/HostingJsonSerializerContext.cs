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
// OpenAPI Schema 导出也需要二进制 HTTP 契约的静态元数据，实际上传/下载仍使用流。
[JsonSerializable(typeof(IFormFile))]
[JsonSerializable(typeof(IFormFileCollection))]
[JsonSerializable(typeof(Stream))]
public sealed partial class HostingJsonSerializerContext : JsonSerializerContext;

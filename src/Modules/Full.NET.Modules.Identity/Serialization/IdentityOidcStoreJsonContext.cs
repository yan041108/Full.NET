using System.Text.Json;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.Identity.Serialization;

/// <summary>协议持久化的静态序列化闭包，保持既有默认 JSON 格式与扩展属性键名。</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class IdentityOidcStoreJsonContext : JsonSerializerContext;

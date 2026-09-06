using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Cryptography.Contracts;

namespace Full.NET.Modules.Cryptography.Serialization;

/// <summary>国密模块 JSON 源生成上下文。</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CryptographyStatusResponse))]
[JsonSerializable(typeof(CryptographyKeyResponse))]
[JsonSerializable(typeof(Sm2SignRequest))]
[JsonSerializable(typeof(Sm2SignResponse))]
[JsonSerializable(typeof(Sm2VerifyRequest))]
[JsonSerializable(typeof(Sm2VerifyResponse))]
[JsonSerializable(typeof(IReadOnlyList<CryptographyKeyResponse>))]
internal partial class CryptographyJsonSerializerContext : JsonSerializerContext;

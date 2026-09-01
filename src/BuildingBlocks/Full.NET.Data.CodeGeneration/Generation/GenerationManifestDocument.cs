using System.Text.Json.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 生成清单的 JSON 序列化文档模型；用于 System.Text.Json 源生成器反序列化磁盘清单。
/// 确定性哈希：Parse 时严格校验 schemaVersion=1，artifacts 非空且路径不重复；任何缺失 FAIL-closed 抛出 ArgumentException，不降级兼容旧版。
/// </summary>
internal sealed class GenerationManifestDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("artifacts")]
    public GenerationManifestEntry[]? Artifacts { get; init; }
}

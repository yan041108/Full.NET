using System.Text.Json.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 回滚检查点的 JSON 序列化文档模型；保存 Apply 写盘前清单与旧产物摘要证据。
/// 确定性哈希：ReadAsync 时校验 SchemaVersion=1、ApplyRunId 非空、AppliedManifestSha256 与清单 ToJson 重算一致；任何不一致 FAIL-closed 拒绝读取。
/// </summary>
internal sealed class GenerationRollbackCheckpointDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("applyRunId")]
    public Guid ApplyRunId { get; init; }

    [JsonPropertyName("appliedManifest")]
    public string? AppliedManifest { get; init; }

    [JsonPropertyName("appliedManifestSha256")]
    public string? AppliedManifestSha256 { get; init; }

    [JsonPropertyName("previousManifest")]
    public string? PreviousManifest { get; init; }

    [JsonPropertyName("previousManifestSha256")]
    public string? PreviousManifestSha256 { get; init; }

    [JsonPropertyName("previousArtifacts")]
    public GenerationRollbackCheckpointArtifactDocument[]? PreviousArtifacts { get; init; }
}

/// <summary>
/// 回滚检查点中单个旧产物的路径与摘要文档；用于清单条目与内容文件的双向校验。
/// 确定性哈希：ReadAsync 时每条 artifact.Sha256 必须与 PreviousManifest 对应条目逐字一致；校验失败 FAIL-closed，不跳过条目。
/// </summary>
internal sealed class GenerationRollbackCheckpointArtifactDocument
{
    [JsonPropertyName("relativePath")]
    public string? RelativePath { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}

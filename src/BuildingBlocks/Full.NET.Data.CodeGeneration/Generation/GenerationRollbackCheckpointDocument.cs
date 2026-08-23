using System.Text.Json.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

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

internal sealed class GenerationRollbackCheckpointArtifactDocument
{
    [JsonPropertyName("relativePath")]
    public string? RelativePath { get; init; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }
}

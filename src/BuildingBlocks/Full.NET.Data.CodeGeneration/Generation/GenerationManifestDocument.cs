using System.Text.Json.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

internal sealed class GenerationManifestDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("artifacts")]
    public GenerationManifestEntry[]? Artifacts { get; init; }
}

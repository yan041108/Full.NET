using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Data.CodeGeneration.Serialization;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 记录单个生成产物在上一次成功提交后的内容摘要。
/// </summary>
/// <param name="RelativePath">工作区相对路径。</param>
/// <param name="Sha256">UTF-8 内容的 SHA-256 小写十六进制摘要。</param>
public sealed record GenerationManifestEntry(
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>
/// 表示可持久化的生成产物所有权清单。
/// </summary>
public sealed class GenerationManifest
{
    public const int CurrentSchemaVersion = 1;

    private readonly IReadOnlyDictionary<string, string> _sha256ByPath;

    private GenerationManifest(
        IReadOnlyList<GenerationManifestEntry> artifacts)
    {
        Artifacts = artifacts;
        _sha256ByPath = artifacts.ToDictionary(
            artifact => artifact.RelativePath,
            artifact => artifact.Sha256,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<GenerationManifestEntry> Artifacts { get; }

    public static GenerationManifest Create(
        IEnumerable<GenerationManifestEntry> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var ordered = artifacts
            .Select(ValidateEntry)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
        EnsureUniquePaths(ordered);

        return new GenerationManifest(
            new ReadOnlyCollection<GenerationManifestEntry>(ordered));
    }

    public static GenerationManifest Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        GenerationManifestDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(
                json,
                CodeGenerationToolchainJsonSerializerContext.Default.GenerationManifestDocument);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("生成清单不是有效 JSON。", nameof(json), exception);
        }

        if (document is null
            || document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"仅支持 schemaVersion={CurrentSchemaVersion} 的生成清单。",
                nameof(json));
        }

        if (document.Artifacts is null)
        {
            throw new ArgumentException("生成清单缺少 artifacts。", nameof(json));
        }

        return Create(document.Artifacts);
    }

    public bool TryGetSha256(
        string relativePath,
        out string? sha256)
    {
        GenerationArtifactPath.Validate(relativePath, nameof(relativePath));
        return _sha256ByPath.TryGetValue(relativePath, out sha256);
    }

    public string ToJson()
    {
        var document = new GenerationManifestDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Artifacts = Artifacts.ToArray(),
        };

        return JsonSerializer.Serialize(
                document,
                CodeGenerationToolchainJsonSerializerContext.Default.GenerationManifestDocument)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            + "\n";
    }

    private static GenerationManifestEntry ValidateEntry(
        GenerationManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var relativePath = GenerationArtifactPath.Validate(
            entry.RelativePath,
            nameof(entry.RelativePath));
        if (!GenerationContentHash.IsValid(entry.Sha256))
        {
            throw new ArgumentException(
                "生成清单摘要必须是 SHA-256 小写十六进制文本。",
                nameof(entry.Sha256));
        }

        return new GenerationManifestEntry(relativePath, entry.Sha256);
    }

    private static void EnsureUniquePaths(
        IReadOnlyList<GenerationManifestEntry> artifacts)
    {
        var portablePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in artifacts)
        {
            if (!portablePaths.Add(artifact.RelativePath))
            {
                throw new ArgumentException(
                    $"生成清单包含重复或不可移植的路径别名：{artifact.RelativePath}",
                    nameof(artifacts));
            }
        }
    }
}

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
    /// <summary>当前持久化清单支持的 schema 版本；Parse 拒绝其它版本。</summary>
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

    /// <summary>获取按相对路径排序的产物摘要条目集合。</summary>
    public IReadOnlyList<GenerationManifestEntry> Artifacts { get; }

    /// <summary>
    /// 校验并构造不可变清单；重复路径与不可移植别名会失败，避免双写漂移。
    /// </summary>
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

    /// <summary>
    /// 解析持久化清单 JSON；schema 版本不符、artifacts 缺失或路径重复都会失败关闭。
    /// </summary>
    /// <param name="json">工作区清单文件的 UTF-8 文本。</param>
    /// <returns>经过路径与摘要校验的不可变清单。</returns>
    /// <exception cref="ArgumentException">JSON 无效、schema 版本不支持或 artifacts 缺失。</exception>
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

    /// <summary>
    /// 查询清单是否拥有指定路径的产物及其摘要；路径必须先经 GenerationArtifactPath 校验。
    /// </summary>
    /// <param name="relativePath">要查询的工作区相对路径。</param>
    /// <param name="sha256">命中时输出对应 SHA-256 摘要；未命中为 null。</param>
    /// <returns>清单是否拥有该路径的产物。</returns>
    public bool TryGetSha256(
        string relativePath,
        out string? sha256)
    {
        GenerationArtifactPath.Validate(relativePath, nameof(relativePath));
        return _sha256ByPath.TryGetValue(relativePath, out sha256);
    }

    /// <summary>
    /// 序列化为带 schema 版本的规范 JSON 文本；行尾统一为 LF 并追加末尾换行，便于哈希稳定。
    /// </summary>
    /// <returns>可直接写入磁盘清单文件的 JSON 文本。</returns>
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

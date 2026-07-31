using System.Collections.ObjectModel;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存只读规划时捕获的目标文件文本，不暴露真实文件系统给纯规划器。
/// </summary>
public sealed class ModuleIntegrationSnapshot
{
    private readonly IReadOnlyDictionary<string, string> files;

    public ModuleIntegrationSnapshot(
        IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        var portablePaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var pair in files)
        {
            var path = GenerationArtifactPath.Validate(
                pair.Key,
                nameof(files));
            ArgumentNullException.ThrowIfNull(pair.Value);
            if (!portablePaths.Add(path))
            {
                throw new ArgumentException(
                    $"接入快照包含重复或不可移植的路径别名：{path}",
                    nameof(files));
            }

            copy.Add(path, pair.Value);
        }

        this.files = new ReadOnlyDictionary<string, string>(copy);
    }

    public bool TryGetContent(
        string relativePath,
        out string content) =>
        files.TryGetValue(relativePath, out content!);
}

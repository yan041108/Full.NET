using System.Collections.ObjectModel;
using Full.NET.Data.CodeGeneration.Generation;

namespace Full.NET.Data.CodeGeneration.Integration;

/// <summary>
/// 保存只读规划时捕获的目标文件文本，不暴露真实文件系统给纯规划器。
/// 确定性：路径在构造时通过 GenerationArtifactPath 校验为可移植别名；包含大小写重复路径立即 FAIL-closed 抛异常。
/// </summary>
public sealed class ModuleIntegrationSnapshot
{
    private readonly IReadOnlyDictionary<string, string> files;

    /// <summary>
    /// 构造接入快照；传入路径立即复制并校验可移植性与唯一性，外部后续变更字典不影响实例。
    /// </summary>
    /// <param name="files">相对路径到文件原始文本的字典；键为可移植仓库相对路径。</param>
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

    /// <summary>
    /// 尝试获取规划指定相对路径下的原始文件文本；查找键使用 Ordinal 精确匹配，禁止二次规范化。
    /// </summary>
    /// <param name="relativePath">可移植仓库相对路径；必须与构造时传入的键字面量一致。</param>
    /// <param name="content">命中时返回原始文件文本，未命中时为空字符串。</param>
    /// <returns>命中返回 true，未命中返回 false。</returns>
    public bool TryGetContent(
        string relativePath,
        out string content) =>
        files.TryGetValue(relativePath, out content!);
}

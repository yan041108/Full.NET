using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 表示生成器规划前捕获的目标文件与所有权清单快照。
/// </summary>
public sealed class GenerationWorkspaceSnapshot
{
    internal GenerationWorkspaceSnapshot(
        IReadOnlyDictionary<string, string> existingFiles,
        GenerationManifest? previousManifest)
    {
        ExistingFiles = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                existingFiles,
                StringComparer.Ordinal));
        PreviousManifest = previousManifest;
    }

    /// <summary>获取按相对路径索引的当前磁盘文本快照；缺失文件不进入字典。</summary>
    public IReadOnlyDictionary<string, string> ExistingFiles { get; }

    /// <summary>获取捕获时读取的上一版清单；为空表示工作区尚未受管。</summary>
    public GenerationManifest? PreviousManifest { get; }
}

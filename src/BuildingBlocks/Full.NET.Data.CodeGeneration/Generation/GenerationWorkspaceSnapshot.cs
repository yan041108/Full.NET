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

    public IReadOnlyDictionary<string, string> ExistingFiles { get; }

    public GenerationManifest? PreviousManifest { get; }
}

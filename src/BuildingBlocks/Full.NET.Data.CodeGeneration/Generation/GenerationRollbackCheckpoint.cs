using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 表示一次 Apply 在写盘前留下的不可变逆向证据；是否允许回滚仍由成功运行记录决定。
/// </summary>
public sealed class GenerationRollbackCheckpoint
{
    internal GenerationRollbackCheckpoint(
        Guid applyRunId,
        GenerationManifest appliedManifest,
        GenerationManifest? previousManifest,
        IReadOnlyDictionary<string, string> previousContents)
    {
        ApplyRunId = applyRunId;
        AppliedManifest = appliedManifest;
        PreviousManifest = previousManifest;
        PreviousContents = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                previousContents,
                StringComparer.Ordinal));
    }

    public Guid ApplyRunId { get; }

    public GenerationManifest AppliedManifest { get; }

    public GenerationManifest? PreviousManifest { get; }

    public IReadOnlyDictionary<string, string> PreviousContents { get; }
}

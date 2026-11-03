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

    /// <summary>获取本次 Apply 运行的唯一标识，用于定位检查点目录与配对恢复证据。</summary>
    public Guid ApplyRunId { get; }

    /// <summary>
    /// 获取检查点建立时即将写盘的清单；回滚前必须与当前磁盘清单逐字对齐才能逆向规划。
    /// </summary>
    public GenerationManifest AppliedManifest { get; }

    /// <summary>
    /// 获取本次 Apply 之前的清单；为空表示工作区原本无受管产物，回滚目标是规范空清单。
    /// </summary>
    public GenerationManifest? PreviousManifest { get; }

    /// <summary>
    /// 获取本次变化文件的旧原文；未变化文件仅依靠前后清单摘要校验，避免检查点重复复制全部产物。
    /// </summary>
    public IReadOnlyDictionary<string, string> PreviousContents { get; }
}

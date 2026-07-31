using System.Collections.ObjectModel;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 表示一次纯内存安全写盘规划的完整结果。
/// </summary>
public sealed class GenerationWritePlan
{
    internal GenerationWritePlan(
        IEnumerable<GenerationWriteAction> actions,
        GenerationManifest? previousManifest,
        GenerationManifest? nextManifest)
    {
        Actions = new ReadOnlyCollection<GenerationWriteAction>(
            actions.ToArray());
        PreviousManifest = previousManifest;
        NextManifest = nextManifest;
    }

    public IReadOnlyList<GenerationWriteAction> Actions { get; }

    /// <summary>
    /// 获取规划时读取的上一版清单，供写盘阶段验证清单未被并发替换。
    /// </summary>
    public GenerationManifest? PreviousManifest { get; }

    /// <summary>
    /// 获取计划是否可整体应用；冲突计划必须由调用方先处理。
    /// </summary>
    public bool CanApply => NextManifest is not null;

    /// <summary>
    /// 获取成功应用全部动作后才能提交的下一版清单。
    /// </summary>
    public GenerationManifest? NextManifest { get; }
}

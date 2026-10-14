namespace Full.NET.AI.Abstractions.Models;

/// <summary>仅约束当前异步流程的 AI 诊断输出；不承载授权或数据派发能力。</summary>
public sealed class AiDataObservationScope : IDisposable
{
    private sealed record Node(bool Allowed, Node? Parent);
    private static readonly AsyncLocal<Node?> Current = new();
    private readonly Node node;

    private AiDataObservationScope(AiDataProcessingPolicy policy, string classification)
    {
        if (!AiDataProcessingPolicy.IsKnown(classification)) throw new ArgumentException("Unknown AI data classification.", nameof(classification));
        var parent = Current.Value;
        // 子流程只能继承或收紧限制；不能用默认策略重新开启父范围禁止的诊断。
        node = new((parent?.Allowed ?? true) && policy.Allows("telemetry", classification), parent);
        Current.Value = node;
    }

    /// <summary>当前流程是否允许 AI 诊断；未建立范围时保持既有独立类库行为。</summary>
    public static bool CanObserve => Current.Value?.Allowed ?? true;

    /// <summary>从宿主冻结策略创建范围；未证明的问题和历史默认按 restricted 处理。</summary>
    /// <param name="policy">受信任宿主策略，不读取 Activity 标签或请求分类。</param>
    /// <param name="classification">服务端证明的分类。</param>
    /// <returns>同一异步流程按后进先出释放的范围。</returns>
    public static AiDataObservationScope Enter(AiDataProcessingPolicy policy, string classification = "restricted")
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(policy, classification);
    }

    /// <summary>只恢复自身仍为当前节点的范围；子任务保留不可变限制，不受父流程释放影响。</summary>
    public void Dispose()
    {
        if (ReferenceEquals(Current.Value, node)) Current.Value = node.Parent;
    }
}

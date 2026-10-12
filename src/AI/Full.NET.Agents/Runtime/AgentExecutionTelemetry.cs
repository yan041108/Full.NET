using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Full.NET.Agents.Runtime;

/// <summary>Agent 执行边界的固定分类观测；运行与操作标识只用于 Trace，不读取业务载荷。</summary>
public static class AgentExecutionTelemetry
{
    /// <summary>供宿主静态订阅的指标与追踪源名称。</summary>
    public const string Name = "Full.NET.Ai.Execution";
    private static readonly Lazy<ActivitySource?> Source = new(() =>
    { try { return new ActivitySource(Name); } catch (Exception) { return null; } });
    private static readonly Lazy<Instruments?> Metrics = new(CreateInstruments);

    /// <summary>开启一次执行边界；未知阶段归一化，诊断失败不能改变调用或授权结果。</summary>
    /// <param name="runId">可信运行标识；空值不输出。</param>
    /// <param name="stage">封闭阶段：run、workflow、tool_loop、node、model、tool；其他值归为other。</param>
    /// <param name="operationId">已有持久操作标识；不为观测创建业务操作。</param>
    /// <param name="stepIndex">已有步骤序号；只接受0至63。</param>
    /// <param name="cancellationToken">仅用于收尾分类，不触发取消、注册回调或改变令牌生命周期。</param>
    /// <returns>必须释放的观测范围；不拥有业务客户端、事务或取消源。</returns>
    public static Observation Start(Guid runId, string stage, Guid? operationId = null,
        int? stepIndex = null, CancellationToken cancellationToken = default) =>
        new(runId, stage is "run" or "workflow" or "tool_loop" or "node" or "model" or "tool" ? stage : "other",
            operationId, stepIndex, cancellationToken);

    private static Instruments? CreateInstruments()
    {
        Meter? meter = null;
        try
        {
            meter = new Meter(Name);
            return new(meter, meter.CreateCounter<long>("fullnet.ai.execution.attempts", "{attempt}"),
                meter.CreateHistogram<double>("fullnet.ai.execution.duration", "ms"));
        }
        catch (Exception) { try { meter?.Dispose(); } catch (Exception) { } return null; }
    }
    private sealed record Instruments(Meter Meter, Counter<long> Attempts, Histogram<double> Duration);

    /// <summary>一次执行边界的观测范围；释放幂等，父 Activity 与原业务结果不受监听器故障影响。</summary>
    public sealed class Observation : IDisposable
    {
        private readonly string stage;
        private readonly CancellationToken token;
        private readonly long started = Stopwatch.GetTimestamp();
        private readonly Activity? parent = Activity.Current;
        private readonly Activity? activity;
        private int completed;
        /// <summary>固定结果分类；未知值归为failed。结果只描述当前边界，不替代持久账本。</summary>
        public string Outcome { get; set; } = "failed";

        internal Observation(Guid runId, string stage, Guid? operationId, int? stepIndex, CancellationToken token)
        {
            this.stage = stage; this.token = token; Activity? candidate = null;
            try
            {
                candidate = Source.Value?.CreateActivity("ai.execution", ActivityKind.Internal);
                candidate?.SetTag("ai.stage", stage);
                if (runId != Guid.Empty) candidate?.SetTag("ai.run.id", runId.ToString("D"));
                if (operationId is { } id && id != Guid.Empty) candidate?.SetTag("ai.operation.id", id.ToString("D"));
                if (stepIndex is >= 0 and < 64) candidate?.SetTag("ai.step.index", stepIndex.Value);
                candidate?.Start(); activity = candidate;
            }
            catch (Exception) { try { candidate?.Dispose(); } catch (Exception) { } RestoreParent(parent); }
        }

        /// <summary>关联刚由业务解析的原操作标识；不调用身份工厂或读写持久状态。</summary>
        /// <param name="operationId">业务已确认的操作标识；空值忽略。</param>
        public void SetOperationId(Guid operationId)
        {
            try { if (operationId != Guid.Empty) activity?.SetTag("ai.operation.id", operationId.ToString("D")); }
            catch (Exception) { }
        }

        /// <summary>独立记录固定标签的计数、耗时并结束 Trace；诊断回调异常不向业务传播。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref completed, 1) != 0) return;
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var outcome = Outcome is "processed" or "not_processed" or "completed" or "succeeded" or "denied"
                or "approval_required" or "reconciliation_required" or "budget_rejected" or "authorization_required"
                or "cancelled" or "refused" ? Outcome : "failed";
            if (outcome == "failed" && token.IsCancellationRequested) outcome = "cancelled";
            TagList tags = new() { { "ai.stage", stage }, { "ai.outcome", outcome } };
            try
            {
                Instruments? instruments = null;
                // 首次发布可能递归进入；两个同步测量回调分别隔离，不能覆盖原调用结果。
                try { instruments = Metrics.Value; } catch (Exception) { }
                try { instruments?.Attempts.Add(1, tags); } catch (Exception) { }
                try { instruments?.Duration.Record(elapsed, tags); } catch (Exception) { }
                activity?.SetTag("ai.outcome", outcome);
                activity?.SetStatus(outcome is "processed" or "completed" or "succeeded" ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            }
            finally { try { activity?.Dispose(); } catch (Exception) { } RestoreParent(parent); }
        }
        private static void RestoreParent(Activity? parent)
        {
            try { if (!ReferenceEquals(Activity.Current, parent)) Activity.Current = parent; } catch (Exception) { }
        }
    }
}

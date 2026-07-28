using System.Collections.Concurrent;

namespace Full.NET.Benchmarks.MixedLoad;

/// <summary>
/// 以确定性轮转方式为请求选择 Audit 写入组合，避免 profile 与随机场景长期偏斜。
/// </summary>
public sealed class MixedLoadAuditWriteProfileSelector
{
    private readonly IReadOnlyList<MixedLoadAuditWriteProfile> _profiles;
    private readonly int _offset;

    /// <summary>
    /// 创建按 Worker 错位轮转的 profile 选择器。
    /// </summary>
    public MixedLoadAuditWriteProfileSelector(
        IReadOnlyList<MixedLoadAuditWriteProfile> profiles,
        int workerId)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0)
        {
            throw new ArgumentException("Audit 写入 profile 不能为空。", nameof(profiles));
        }

        _profiles = profiles;
        _offset = Math.Abs(workerId % profiles.Count);
    }

    /// <summary>
    /// 根据 Worker 内请求序号选择本次测量使用的 Audit 写入组合。
    /// </summary>
    public MixedLoadAuditWriteProfile Select(long sequence)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), sequence, null);
        }

        return _profiles[(int)((sequence + _offset) % _profiles.Count)];
    }
}

/// <summary>
/// 保存 Benchmark Host 内各 Audit 写入组合的真实尝试次数、失败次数与耗时样本。
/// </summary>
public sealed class MixedLoadAuditWriteTelemetry
{
    private readonly ConcurrentDictionary<
        (MixedLoadAuditWriteProfile Profile, string StatementName),
        ObservationAccumulator> _observations = new();

    /// <summary>
    /// 清除预热阶段样本，仅保留正式采样窗口的数据。
    /// </summary>
    public void Reset() => _observations.Clear();

    /// <summary>
    /// 记录一次已实际执行的 Audit INSERT；被 profile 屏蔽的写入不进入样本。
    /// </summary>
    public void Record(
        MixedLoadAuditWriteProfile profile,
        string statementName,
        double durationMilliseconds,
        bool succeeded)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementName);
        if (durationMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMilliseconds),
                durationMilliseconds,
                null);
        }

        var accumulator = _observations.GetOrAdd(
            (profile, statementName),
            static _ => new ObservationAccumulator());
        accumulator.Durations.Enqueue(durationMilliseconds);
        Interlocked.Increment(ref accumulator.Attempts);
        if (!succeeded)
        {
            Interlocked.Increment(ref accumulator.Failures);
        }
    }

    /// <summary>
    /// 生成不可变快照，供报告按低基数 profile 和稳定 Statement 名称归因。
    /// </summary>
    public MixedLoadAuditWriteSnapshot Snapshot()
    {
        var observations = _observations
            .OrderBy(pair => pair.Key.Profile)
            .ThenBy(pair => pair.Key.StatementName, StringComparer.Ordinal)
            .Select(pair =>
            {
                var durations = pair.Value.Durations.ToArray();
                return new MixedLoadAuditWriteObservation(
                    pair.Key.Profile,
                    pair.Key.StatementName,
                    Interlocked.Read(ref pair.Value.Attempts),
                    Interlocked.Read(ref pair.Value.Failures),
                    durations.Length == 0
                        ? null
                        : MixedLoadLatencyStatistics.Calculate(durations));
            })
            .ToArray();
        return new MixedLoadAuditWriteSnapshot(observations);
    }

    private sealed class ObservationAccumulator
    {
        public readonly ConcurrentQueue<double> Durations = new();
        public long Attempts;
        public long Failures;
    }
}

/// <summary>
/// 描述一个 Audit Statement 在指定写入组合下的聚合观测。
/// </summary>
public sealed record MixedLoadAuditWriteObservation(
    MixedLoadAuditWriteProfile Profile,
    string StatementName,
    long Attempts,
    long Failures,
    MixedLoadLatencyStatistics? Duration);

/// <summary>
/// 描述正式采样窗口内全部 Audit 写入观测。
/// </summary>
public sealed record MixedLoadAuditWriteSnapshot(
    IReadOnlyList<MixedLoadAuditWriteObservation> Observations);

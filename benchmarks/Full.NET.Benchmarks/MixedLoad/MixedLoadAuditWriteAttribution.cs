namespace Full.NET.Benchmarks.MixedLoad;

/// <summary>
/// 汇总单个 Audit 写入组合的请求尾延迟、预期 INSERT 次数和真实观测。
/// </summary>
public sealed record MixedLoadAuditWriteProfileResult(
    MixedLoadAuditWriteProfile Profile,
    int RequestCount,
    int UnexpectedErrors,
    MixedLoadLatencyStatistics Latency,
    IReadOnlyDictionary<string, long> ExpectedStatementExecutions,
    IReadOnlyDictionary<string, long> ObservedStatementExecutions,
    IReadOnlyList<MixedLoadAuditWriteObservation> Observations,
    bool EvidenceComplete);

/// <summary>
/// 汇总同一并发档内各 Audit 写入组合的可比归因结果。
/// </summary>
public sealed record MixedLoadAuditWriteAttributionResult(
    IReadOnlyList<MixedLoadAuditWriteProfileResult> Profiles,
    bool EvidenceComplete);

/// <summary>
/// 按请求 profile 将端到端尾延迟与真实 Audit INSERT 观测关联。
/// </summary>
public static class MixedLoadAuditWriteAttribution
{
    private static readonly IReadOnlyList<StatementContract> Statements =
    [
        new(
            "auditing.insert_access_log",
            MixedLoadAuditWriteProfile.Access),
        new(
            "auditing.insert_operation_log",
            MixedLoadAuditWriteProfile.Operation),
        new(
            "auditing.insert_exception_log",
            MixedLoadAuditWriteProfile.Exception),
    ];

    /// <summary>
    /// 创建归因结果；任一 profile 缺少请求，或预期与真实 INSERT 次数不一致时证据不完整。
    /// </summary>
    public static MixedLoadAuditWriteAttributionResult Create(
        IReadOnlyList<MixedLoadRequestSample> samples,
        IReadOnlyList<MixedLoadScenario> scenarios,
        MixedLoadAuditWriteSnapshot telemetry,
        IReadOnlyList<MixedLoadAuditWriteProfile> configuredProfiles)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(scenarios);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(configuredProfiles);

        var scenarioByName = scenarios.ToDictionary(
            scenario => scenario.Name,
            StringComparer.Ordinal);
        var results = configuredProfiles.Select(profile =>
        {
            var profileSamples = samples
                .Where(sample => sample.AuditWriteProfile == profile)
                .ToArray();
            if (profileSamples.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Audit 写入 profile {profile} 未产生请求样本。");
            }

            var expected = Statements.ToDictionary(
                contract => contract.StatementName,
                contract => profileSamples.LongCount(sample =>
                {
                    if (!scenarioByName.TryGetValue(sample.Scenario, out var scenario))
                    {
                        throw new InvalidOperationException(
                            $"请求样本引用未知场景：{sample.Scenario}");
                    }

                    return profile.HasFlag(contract.Flag)
                        && scenario.ExpectedAuditWrites.HasFlag(contract.Flag);
                }),
                StringComparer.Ordinal);
            var observations = telemetry.Observations
                .Where(observation => observation.Profile == profile)
                .ToArray();
            var observed = Statements.ToDictionary(
                contract => contract.StatementName,
                contract => observations
                    .Where(observation =>
                        string.Equals(
                            observation.StatementName,
                            contract.StatementName,
                            StringComparison.Ordinal))
                    .Sum(observation => observation.Attempts),
                StringComparer.Ordinal);
            var evidenceComplete = expected.All(pair =>
                    observed[pair.Key] == pair.Value)
                && observations.All(observation => observation.Failures == 0);
            return new MixedLoadAuditWriteProfileResult(
                profile,
                profileSamples.Length,
                profileSamples.Count(sample => sample.IsUnexpected),
                MixedLoadLatencyStatistics.Calculate(
                    profileSamples
                        .Select(sample => sample.DurationMilliseconds)
                        .ToArray()),
                expected,
                observed,
                observations,
                evidenceComplete);
        }).ToArray();

        return new MixedLoadAuditWriteAttributionResult(
            results,
            results.All(result => result.EvidenceComplete));
    }

    private sealed record StatementContract(
        string StatementName,
        MixedLoadAuditWriteProfile Flag);
}

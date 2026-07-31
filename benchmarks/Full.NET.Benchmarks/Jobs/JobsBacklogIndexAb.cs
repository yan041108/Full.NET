namespace Full.NET.Benchmarks.Jobs;

public enum JobsBacklogIndexVariant
{
    Baseline = 0,
    Candidate = 1,
}

public sealed record JobsBacklogIndexSampleBlock(
    JobsBacklogIndexVariant Variant,
    int SampleCount);

public sealed record JobsBacklogIndexDefinition(
    string Name,
    string CreateSql,
    string DropSql);

public static class JobsBacklogIndexCandidate
{
    public const string IndexName =
        "IX_fn_jobs_execution_BacklogStatusTenant";

    public static JobsBacklogIndexDefinition ForProvider(
        string provider) =>
        provider switch
        {
            "sqlserver" => new JobsBacklogIndexDefinition(
                IndexName,
                $"""
                CREATE INDEX {IndexName}
                    ON dbo.fn_jobs_execution (Status, TenantId)
                    INCLUDE (NextAttemptAtUtc, CreatedAtUtc);
                """,
                $"""
                DROP INDEX IF EXISTS {IndexName}
                    ON dbo.fn_jobs_execution;
                """),
            "mysql" => new JobsBacklogIndexDefinition(
                IndexName,
                $"""
                CREATE INDEX {IndexName}
                    ON fn_jobs_execution
                        (Status, TenantId, NextAttemptAtUtc, CreatedAtUtc);
                """,
                $"""
                DROP INDEX {IndexName}
                    ON fn_jobs_execution;
                """),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的 Jobs backlog 索引 A/B Provider。"),
        };
}

public static class JobsBacklogIndexAbSampling
{
    public static IReadOnlyList<JobsBacklogIndexSampleBlock> CreateBlocks(
        int sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleCount);
        var firstBlockCount = (sampleCount + 1) / 2;
        var secondBlockCount = sampleCount / 2;
        return
        [
            new JobsBacklogIndexSampleBlock(
                JobsBacklogIndexVariant.Baseline,
                firstBlockCount),
            new JobsBacklogIndexSampleBlock(
                JobsBacklogIndexVariant.Candidate,
                firstBlockCount),
            new JobsBacklogIndexSampleBlock(
                JobsBacklogIndexVariant.Candidate,
                secondBlockCount),
            new JobsBacklogIndexSampleBlock(
                JobsBacklogIndexVariant.Baseline,
                secondBlockCount),
        ];
    }
}

public static class JobsBacklogIndexPlanInspector
{
    public static bool UsesCandidateIndex(
        string provider,
        string planContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planContent);
        return provider switch
        {
            "sqlserver" => planContent.Contains(
                $"Index=\"[{JobsBacklogIndexCandidate.IndexName}]\"",
                StringComparison.OrdinalIgnoreCase),
            "mysql" => planContent.Contains(
                $"using {JobsBacklogIndexCandidate.IndexName}",
                StringComparison.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException(
                nameof(provider),
                provider,
                "不支持的 Jobs backlog 执行计划 Provider。"),
        };
    }
}

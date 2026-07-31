using Full.NET.Benchmarks.Jobs;
using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class JobsBacklogQueryBenchmarkTests
{
    [TestMethod]
    public void Defaults_define_representative_sequential_dual_database_run()
    {
        var options = JobsBacklogQueryBenchmarkOptions.Parse([]);

        Assert.AreEqual(100_000, options.Rows);
        Assert.AreEqual(5, options.WarmupIterations);
        Assert.AreEqual(30, options.MeasurementIterations);
        Assert.AreEqual(10, options.MutationIterations);
        Assert.AreEqual(1, options.Concurrency);
        Assert.AreEqual(
            JobsBacklogQueryBenchmarkMode.Baseline,
            options.Mode);
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
            options.ReferenceUtc);
        CollectionAssert.AreEqual(
            new[] { "sqlserver", "mysql" },
            options.Providers.ToArray());
        StringAssert.Contains(
            options.OutputDirectory.Replace('\\', '/'),
            "jobs-backlog-query/");
    }

    [TestMethod]
    public void Parser_accepts_short_runs_and_rejects_ambiguous_shapes()
    {
        var options = JobsBacklogQueryBenchmarkOptions.Parse(
        [
            "--rows", "2000",
            "--warmup", "1",
            "--iterations", "5",
            "--providers", "mysql",
            "--reference-utc", "2026-07-30T01:02:03Z",
            "--output", "artifacts/jobs-backlog-query",
        ]);

        Assert.AreEqual(2_000, options.Rows);
        Assert.AreEqual(1, options.WarmupIterations);
        Assert.AreEqual(5, options.MeasurementIterations);
        CollectionAssert.AreEqual(
            new[] { "mysql" },
            options.Providers.ToArray());
        Assert.AreEqual(
            new DateTimeOffset(2026, 7, 30, 1, 2, 3, TimeSpan.Zero),
            options.ReferenceUtc);
        Assert.AreEqual(
            "artifacts/jobs-backlog-query",
            options.OutputDirectory);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--rows", "999"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--rows", "1001"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--providers", "mysql,mysql"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--providers", "postgres"]));
        Assert.ThrowsExactly<ArgumentException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--unknown", "value"]));
    }

    [TestMethod]
    public void Parser_accepts_index_ab_and_rejects_invalid_mutation_samples()
    {
        var options = JobsBacklogQueryBenchmarkOptions.Parse(
        [
            "--mode", "index-ab",
            "--mutation-iterations", "5",
        ]);

        Assert.AreEqual(
            JobsBacklogQueryBenchmarkMode.IndexAb,
            options.Mode);
        Assert.AreEqual(5, options.MutationIterations);
        Assert.ThrowsExactly<ArgumentException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--mode", "unknown"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--mutation-iterations", "2"]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogQueryBenchmarkOptions.Parse(
                ["--mutation-iterations", "101"]));
    }

    [TestMethod]
    public void Candidate_index_contract_is_stable_and_provider_specific()
    {
        var sqlServer = JobsBacklogIndexCandidate.ForProvider(
            "sqlserver");
        var mySql = JobsBacklogIndexCandidate.ForProvider("mysql");

        Assert.AreEqual(
            "IX_fn_jobs_execution_BacklogStatusTenant",
            sqlServer.Name);
        Assert.AreEqual(sqlServer.Name, mySql.Name);
        StringAssert.Contains(
            sqlServer.CreateSql,
            "(Status, TenantId)");
        StringAssert.Contains(
            sqlServer.CreateSql,
            "INCLUDE (NextAttemptAtUtc, CreatedAtUtc)");
        StringAssert.Contains(
            mySql.CreateSql,
            "(Status, TenantId, NextAttemptAtUtc, CreatedAtUtc)");
        StringAssert.Contains(sqlServer.DropSql, sqlServer.Name);
        StringAssert.Contains(mySql.DropSql, mySql.Name);
        Assert.IsFalse(
            JobsBacklogIndexPlanInspector.UsesCandidateIndex(
                "mysql",
                $$"""
                {"possible_keys":["{{mySql.Name}}"],"key":"other"}
                """));
        Assert.IsTrue(
            JobsBacklogIndexPlanInspector.UsesCandidateIndex(
                "mysql",
                $"Index lookup using {mySql.Name}"));
        Assert.IsTrue(
            JobsBacklogIndexPlanInspector.UsesCandidateIndex(
                "sqlserver",
                $"<Object Index=\"[{sqlServer.Name}]\" />"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogIndexCandidate.ForProvider("postgres"));
    }

    [TestMethod]
    public void Index_size_probe_uses_metadata_available_to_application_user()
    {
        var sqlServer = JobsBacklogIndexSizeSql.ForProvider(
            "sqlserver");
        var mySql = JobsBacklogIndexSizeSql.ForProvider("mysql");

        StringAssert.Contains(
            sqlServer,
            "sys.dm_db_partition_stats");
        StringAssert.Contains(
            mySql,
            "INFORMATION_SCHEMA.TABLES");
        StringAssert.Contains(
            JobsBacklogIndexSizeSql.MySqlStatisticsRefreshSql,
            "information_schema_stats_expiry = 0");
        StringAssert.Contains(
            JobsBacklogIndexSizeSql.MySqlAnalyzeTableSql,
            "ANALYZE TABLE fn_jobs_execution");
        Assert.IsFalse(
            mySql.Contains(
                "mysql.",
                StringComparison.OrdinalIgnoreCase));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogIndexSizeSql.ForProvider("postgres"));
    }

    [TestMethod]
    public void Index_ab_sampling_mirrors_blocks_and_preserves_sample_counts()
    {
        var blocks = JobsBacklogIndexAbSampling.CreateBlocks(5);

        CollectionAssert.AreEqual(
            new[]
            {
                new JobsBacklogIndexSampleBlock(
                    JobsBacklogIndexVariant.Baseline,
                    3),
                new JobsBacklogIndexSampleBlock(
                    JobsBacklogIndexVariant.Candidate,
                    3),
                new JobsBacklogIndexSampleBlock(
                    JobsBacklogIndexVariant.Candidate,
                    2),
                new JobsBacklogIndexSampleBlock(
                    JobsBacklogIndexVariant.Baseline,
                    2),
            },
            blocks.ToArray());
        Assert.AreEqual(
            5,
            blocks
                .Where(block =>
                    block.Variant == JobsBacklogIndexVariant.Baseline)
                .Sum(block => block.SampleCount));
        Assert.AreEqual(
            5,
            blocks
                .Where(block =>
                    block.Variant == JobsBacklogIndexVariant.Candidate)
                .Sum(block => block.SampleCount));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogIndexAbSampling.CreateBlocks(0));
    }

    [TestMethod]
    public void Index_ab_entrypoint_documents_candidate_and_write_gates()
    {
        Assert.IsNotNull(typeof(JobsBacklogIndexAbBenchmarkRunner));
        StringAssert.Contains(
            JobsBacklogQueryBenchmarkOptions.HelpText,
            "index-ab");
        StringAssert.Contains(
            JobsBacklogQueryBenchmarkOptions.HelpText,
            JobsBacklogIndexCandidate.IndexName);
        StringAssert.Contains(
            JobsBacklogQueryBenchmarkOptions.HelpText,
            "trigger_insert");
        StringAssert.Contains(
            JobsBacklogQueryBenchmarkOptions.HelpText,
            "claim");
        StringAssert.Contains(
            JobsBacklogQueryBenchmarkOptions.HelpText,
            "terminal_success");
    }

    [TestMethod]
    public void Dataset_freezes_host_backlog_retry_and_tenant_noise_distribution()
    {
        var referenceUtc = new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero);

        var expectation = JobsBacklogDataset.CreateExpectation(
            100_000,
            referenceUtc);

        Assert.AreEqual(50_000L, expectation.PendingCount);
        Assert.AreEqual(15_000L, expectation.DueRetryCount);
        Assert.AreEqual(40_000L, expectation.ClaimableCount);
        Assert.AreEqual(20_000L, expectation.TenantPendingNoiseCount);
        Assert.IsNotNull(expectation.OldestClaimableCreatedAtUtc);
        Assert.IsNotNull(expectation.OldestDueRetryAtUtc);
        Assert.IsTrue(
            expectation.OldestClaimableCreatedAtUtc < referenceUtc);
        Assert.IsTrue(expectation.OldestDueRetryAtUtc < referenceUtc);
    }

    [TestMethod]
    public void Statistics_use_nearest_rank_tail_percentiles()
    {
        var samples = Enumerable.Range(1, 100)
            .Select(value => TimeSpan.FromMilliseconds(value))
            .ToArray();

        var statistics = JobsBacklogQueryStatistics.Calculate(samples);

        Assert.AreEqual(100, statistics.SampleCount);
        Assert.AreEqual(1d, statistics.MinimumMilliseconds);
        Assert.AreEqual(50d, statistics.P50Milliseconds);
        Assert.AreEqual(95d, statistics.P95Milliseconds);
        Assert.AreEqual(99d, statistics.P99Milliseconds);
        Assert.AreEqual(100d, statistics.MaximumMilliseconds);
    }

    [TestMethod]
    public void Benchmark_sql_is_the_exact_production_backlog_statement()
    {
        Assert.AreEqual(
            JobSql.ReadBacklogSqlServer.Text.ReplaceLineEndings("\n"),
            JobsBacklogQuerySql.ForProvider("sqlserver")
                .ReplaceLineEndings("\n"));
        Assert.AreEqual(
            JobSql.ReadBacklogMySql.Text.ReplaceLineEndings("\n"),
            JobsBacklogQuerySql.ForProvider("mysql")
                .ReplaceLineEndings("\n"));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogQuerySql.ForProvider("postgres"));
    }

    [TestMethod]
    public void Mutation_probes_use_exact_production_job_statements()
    {
        var sqlServer = JobsBacklogMutationSql.ForProvider("sqlserver");
        var mySql = JobsBacklogMutationSql.ForProvider("mysql");

        AssertStatementEqual(
            JobSql.InsertExecution.Text,
            sqlServer.TriggerInsertSql);
        AssertStatementEqual(
            JobSql.AcquireExecutionsSqlServer.Text,
            sqlServer.ClaimSelectSql);
        Assert.IsNull(sqlServer.ClaimUpdateSql);
        AssertStatementEqual(
            JobSql.MarkExecutionSucceeded.Text,
            sqlServer.TerminalSuccessSql);
        AssertStatementEqual(
            JobSql.InsertExecution.Text,
            mySql.TriggerInsertSql);
        AssertStatementEqual(
            JobSql.SelectClaimableExecutionIdsMySql.Text,
            mySql.ClaimSelectSql);
        AssertStatementEqual(
            JobSql.ClaimExecutionsByIdsMySql.Text,
            mySql.ClaimUpdateSql);
        AssertStatementEqual(
            JobSql.MarkExecutionSucceeded.Text,
            mySql.TerminalSuccessSql);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => JobsBacklogMutationSql.ForProvider("postgres"));
    }

    [TestMethod]
    public void Query_result_gate_requires_counts_and_time_boundaries_to_match()
    {
        var referenceUtc = new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero);
        var expectation = JobsBacklogDataset.CreateExpectation(
            2_000,
            referenceUtc);
        var matching = new JobsBacklogQueryResult(
            expectation.PendingCount,
            expectation.OldestClaimableCreatedAtUtc,
            expectation.DueRetryCount,
            expectation.OldestDueRetryAtUtc);

        Assert.IsTrue(matching.Matches(expectation));
        Assert.IsTrue(
            (matching with
            {
                OldestDueRetryAtUtc =
                    expectation.OldestDueRetryAtUtc?.AddTicks(1),
            }).Matches(expectation));
        Assert.IsFalse(
            (matching with
            {
                PendingCount = expectation.PendingCount - 1,
            }).Matches(expectation));
        Assert.IsFalse(
            (matching with
            {
                OldestClaimableCreatedAtUtc =
                    expectation.OldestClaimableCreatedAtUtc?.AddSeconds(1),
            }).Matches(expectation));
    }

    [TestMethod]
    public void Index_ab_assessment_requires_tail_gain_and_bounded_write_cost()
    {
        var referenceUtc = new DateTimeOffset(
            2026,
            7,
            30,
            0,
            0,
            0,
            TimeSpan.Zero);
        var expectation = JobsBacklogDataset.CreateExpectation(
            2_000,
            referenceUtc);
        var queryResult = new JobsBacklogQueryResult(
            expectation.PendingCount,
            expectation.OldestClaimableCreatedAtUtc,
            expectation.DueRetryCount,
            expectation.OldestDueRetryAtUtc);
        var baseline = CreateIndexVariantResult(
            JobsBacklogIndexVariant.Baseline,
            queryResult,
            queryMilliseconds: 100,
            mutationMilliseconds: 10,
            ["sqlserver/baseline/actual.showplan.xml"]);
        var candidate = CreateIndexVariantResult(
            JobsBacklogIndexVariant.Candidate,
            queryResult,
            queryMilliseconds: 60,
            mutationMilliseconds: 11,
            ["sqlserver/candidate/actual.showplan.xml"]);

        var passing = JobsBacklogIndexAbAssessment.Assess(
            expectation,
            baseline,
            candidate);
        var writeRegression = JobsBacklogIndexAbAssessment.Assess(
            expectation,
            baseline,
            candidate with
            {
                Mutations = CreateMutationStatistics(13),
            });
        var noTailGain = JobsBacklogIndexAbAssessment.Assess(
            expectation,
            baseline,
            candidate with
            {
                QueryStatistics = CreateStatistics(100),
            });
        var missingPlan = JobsBacklogIndexAbAssessment.Assess(
            expectation,
            baseline,
            candidate with
            {
                PlanFiles = [],
            });
        var unusedCandidateIndex = JobsBacklogIndexAbAssessment.Assess(
            expectation,
            baseline,
            candidate with
            {
                UsesCandidateIndex = false,
            });

        Assert.IsTrue(passing.MigrationAllowed);
        Assert.AreEqual(0, passing.Reasons.Count);
        Assert.IsFalse(writeRegression.MigrationAllowed);
        StringAssert.Contains(
            string.Join(Environment.NewLine, writeRegression.Reasons),
            "20%");
        Assert.IsFalse(noTailGain.MigrationAllowed);
        StringAssert.Contains(
            string.Join(Environment.NewLine, noTailGain.Reasons),
            "P95/P99");
        Assert.IsFalse(missingPlan.MigrationAllowed);
        StringAssert.Contains(
            string.Join(Environment.NewLine, missingPlan.Reasons),
            "计划");
        Assert.IsFalse(unusedCandidateIndex.MigrationAllowed);
        StringAssert.Contains(
            string.Join(
                Environment.NewLine,
                unusedCandidateIndex.Reasons),
            "未采用候选索引");
    }

    [TestMethod]
    public async Task Index_ab_report_persists_costs_and_migration_gate()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-jobs-backlog-index-ab-{Guid.NewGuid():N}");
        var options = JobsBacklogQueryBenchmarkOptions.Parse(
        [
            "--mode", "index-ab",
            "--rows", "2000",
            "--warmup", "1",
            "--iterations", "5",
            "--mutation-iterations", "3",
            "--providers", "sqlserver",
            "--output", outputDirectory,
        ]);
        var expectation = JobsBacklogDataset.CreateExpectation(
            options.Rows,
            options.ReferenceUtc);
        var queryResult = new JobsBacklogQueryResult(
            expectation.PendingCount,
            expectation.OldestClaimableCreatedAtUtc,
            expectation.DueRetryCount,
            expectation.OldestDueRetryAtUtc);
        var baseline = CreateIndexVariantResult(
            JobsBacklogIndexVariant.Baseline,
            queryResult,
            queryMilliseconds: 100,
            mutationMilliseconds: 10,
            ["sqlserver/baseline/actual.showplan.xml"]);
        var candidate = CreateIndexVariantResult(
            JobsBacklogIndexVariant.Candidate,
            queryResult,
            queryMilliseconds: 60,
            mutationMilliseconds: 11,
            ["sqlserver/candidate/actual.showplan.xml"]);
        var provider = new JobsBacklogIndexProviderResult(
            "sqlserver",
            "sqlserver:test",
            "test-version",
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(300),
            CandidateIndexSizeBytes: 1_048_576,
            expectation,
            baseline,
            candidate,
            JobsBacklogIndexAbAssessment.Assess(
                expectation,
                baseline,
                candidate));

        try
        {
            var report = JobsBacklogIndexAbReportWriter.CreateReport(
                options,
                [provider]);
            await JobsBacklogIndexAbReportWriter.WriteAsync(
                outputDirectory,
                report,
                CancellationToken.None);

            var markdown = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "README.md"));
            StringAssert.Contains(markdown, "index-ab");
            StringAssert.Contains(markdown, "1048576");
            StringAssert.Contains(markdown, "trigger_insert");
            StringAssert.Contains(markdown, "claim");
            StringAssert.Contains(markdown, "terminal_success");
            StringAssert.Contains(markdown, "允许进入独立迁移切片");
            Assert.IsTrue(File.Exists(
                Path.Combine(outputDirectory, "summary.json")));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task Report_persists_environment_tail_latency_and_plan_paths()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-jobs-backlog-query-{Guid.NewGuid():N}");
        var options = JobsBacklogQueryBenchmarkOptions.Parse(
        [
            "--rows", "2000",
            "--warmup", "1",
            "--iterations", "5",
            "--providers", "sqlserver",
            "--output", outputDirectory,
        ]);
        var expectation = JobsBacklogDataset.CreateExpectation(
            options.Rows,
            options.ReferenceUtc);
        var result = new JobsBacklogQueryResult(
            expectation.PendingCount,
            expectation.OldestClaimableCreatedAtUtc,
            expectation.DueRetryCount,
            expectation.OldestDueRetryAtUtc);
        var provider = new JobsBacklogQueryProviderResult(
            "sqlserver",
            "sqlserver:test",
            "test-version",
            TimeSpan.FromSeconds(1),
            expectation,
            result,
            JobsBacklogQueryStatistics.Calculate(
                Enumerable.Range(1, 5)
                    .Select(value => TimeSpan.FromMilliseconds(value))
                    .ToArray()),
            [1d, 2d, 3d, 4d, 5d],
            ["sqlserver/actual.showplan.xml"]);

        try
        {
            var report = JobsBacklogQueryReportWriter.CreateReport(
                options,
                [provider]);
            await JobsBacklogQueryReportWriter.WriteAsync(
                outputDirectory,
                report,
                CancellationToken.None);

            var markdown = await File.ReadAllTextAsync(
                Path.Combine(outputDirectory, "README.md"));
            StringAssert.Contains(markdown, "P95 ms");
            StringAssert.Contains(markdown, "1000");
            StringAssert.Contains(markdown, "300");
            StringAssert.Contains(
                markdown,
                "sqlserver/actual.showplan.xml");
            StringAssert.Contains(markdown, "正确性门禁");
            Assert.IsTrue(File.Exists(
                Path.Combine(outputDirectory, "summary.json")));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static JobsBacklogIndexVariantResult CreateIndexVariantResult(
        JobsBacklogIndexVariant variant,
        JobsBacklogQueryResult queryResult,
        double queryMilliseconds,
        double mutationMilliseconds,
        IReadOnlyList<string> planFiles) =>
        new(
            variant,
            queryResult,
            CreateStatistics(queryMilliseconds),
            Enumerable.Repeat(queryMilliseconds, 5).ToArray(),
            CreateMutationStatistics(mutationMilliseconds),
            planFiles,
            UsesCandidateIndex:
                variant == JobsBacklogIndexVariant.Candidate);

    private static JobsBacklogMutationStatistics CreateMutationStatistics(
        double milliseconds) =>
        new(
            CreateStatistics(milliseconds),
            CreateStatistics(milliseconds),
            CreateStatistics(milliseconds));

    private static JobsBacklogQueryStatistics CreateStatistics(
        double milliseconds) =>
        JobsBacklogQueryStatistics.Calculate(
            Enumerable.Repeat(
                    TimeSpan.FromMilliseconds(milliseconds),
                    5)
                .ToArray());

    private static void AssertStatementEqual(
        string expected,
        string? actual) =>
        Assert.AreEqual(
            expected.ReplaceLineEndings("\n"),
            actual?.ReplaceLineEndings("\n"));
}

using Full.NET.Benchmarks.Auditing;
using Full.NET.Modules.Auditing.Persistence;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingQueryBenchmarkTests
{
    [TestMethod]
    public void Default_options_define_representative_sequential_workload()
    {
        var options = AuditingQueryBenchmarkOptions.Parse([]);

        Assert.AreEqual(100_000, options.Rows);
        Assert.AreEqual(5, options.WarmupIterations);
        Assert.AreEqual(30, options.MeasurementIterations);
        Assert.AreEqual(50, options.PageSize);
        Assert.AreEqual(1, options.Concurrency);
        CollectionAssert.AreEquivalent(
            new[] { "sqlserver", "mysql" },
            options.Providers.ToArray());
    }

    [TestMethod]
    public void Statistics_use_nearest_rank_percentiles()
    {
        var samples = Enumerable.Range(1, 100)
            .Select(value => TimeSpan.FromMilliseconds(value))
            .ToArray();

        var statistics = AuditingQueryStatistics.Calculate(samples);

        Assert.AreEqual(50d, statistics.P50Milliseconds);
        Assert.AreEqual(95d, statistics.P95Milliseconds);
        Assert.AreEqual(99d, statistics.P99Milliseconds);
        Assert.AreEqual(1d, statistics.MinimumMilliseconds);
        Assert.AreEqual(100d, statistics.MaximumMilliseconds);
    }

    [TestMethod]
    public void Scenarios_cover_first_page_deep_offset_and_contains_bounds()
    {
        var options = AuditingQueryBenchmarkOptions.Parse([]);

        var scenarios = AuditingQueryScenarios.Create(
            options,
            new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero));

        CollectionAssert.AreEqual(
            new[]
            {
                "first_page",
                "deep_offset",
                "contains_unbounded",
                "contains_bounded",
            },
            scenarios.Select(scenario => scenario.Name).ToArray());
        Assert.AreEqual(99_950, scenarios[1].Offset);
        Assert.IsNull(scenarios[2].FromUtc);
        Assert.IsNotNull(scenarios[3].FromUtc);
    }

    [TestMethod]
    public void Benchmark_sql_stays_identical_to_production_access_log_sql()
    {
        var productionSqlServer = AccessLogSql.CreatePageFilteredSqlServer(
            hasFromUtc: true,
            hasToUtc: true,
            hasHttpMethod: false,
            hasStatusCode: false,
            hasPathContains: true);
        var options = AuditingQueryBenchmarkOptions.Parse([]);
        var boundedScenario = AuditingQueryScenarios
            .Create(options, options.ReferenceUtc)
            .Single(scenario => scenario.Name == "contains_bounded");
        var benchmarkSqlServer = AuditingSqlServerQueryFactory.Create(
            AuditingSqlServerQueryStrategy.BranchSpecific,
            boundedScenario);

        Assert.AreEqual(
            productionSqlServer.Text.ReplaceLineEndings("\n"),
            benchmarkSqlServer.PageSql.ReplaceLineEndings("\n"));
        Assert.AreEqual(
            AccessLogSql.PageFilteredMySql.Text.ReplaceLineEndings("\n"),
            AuditingQuerySql.MySqlPage.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void SqlServer_ab_mode_is_explicit_and_rejects_other_providers()
    {
        var options = AuditingQueryBenchmarkOptions.Parse(
        [
            "--mode",
            "sqlserver-plan-ab",
            "--providers",
            "sqlserver",
        ]);

        Assert.AreEqual(
            AuditingQueryBenchmarkMode.SqlServerPlanAb,
            options.Mode);
        StringAssert.Contains(
            options.OutputDirectory.Replace('\\', '/'),
            "auditing-query-sqlserver-ab/");
        Assert.ThrowsExactly<ArgumentException>(() =>
            AuditingQueryBenchmarkOptions.Parse(
            [
                "--mode",
                "sqlserver-plan-ab",
                "--providers",
                "mysql",
            ]));
    }

    [TestMethod]
    public void SqlServer_ab_sequences_reverse_the_first_compilation_scenario()
    {
        var options = AuditingQueryBenchmarkOptions.Parse([]);
        var scenarios = AuditingQueryScenarios.Create(
            options,
            options.ReferenceUtc);

        var sequences = AuditingSqlServerAbSequences.Create(scenarios);

        CollectionAssert.AreEqual(
            new[] { "broad_first", "bounded_first" },
            sequences.Select(sequence => sequence.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "first_page", "contains_bounded" },
            sequences[0].Scenarios.Select(scenario => scenario.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { "contains_bounded", "first_page" },
            sequences[1].Scenarios.Select(scenario => scenario.Name).ToArray());
    }

    [TestMethod]
    public void SqlServer_query_strategies_keep_parameters_and_remove_optional_predicates()
    {
        var options = AuditingQueryBenchmarkOptions.Parse([]);
        var bounded = AuditingQueryScenarios.Create(options, options.ReferenceUtc)
            .Single(scenario => scenario.Name == "contains_bounded");

        var current = AuditingSqlServerQueryFactory.Create(
            AuditingSqlServerQueryStrategy.CurrentOptional,
            bounded);
        var branch = AuditingSqlServerQueryFactory.Create(
            AuditingSqlServerQueryStrategy.BranchSpecific,
            bounded);
        var recompile = AuditingSqlServerQueryFactory.Create(
            AuditingSqlServerQueryStrategy.Recompile,
            bounded);

        Assert.AreEqual(
            AuditingQuerySql.SqlServerPage.ReplaceLineEndings("\n"),
            current.PageSql.ReplaceLineEndings("\n"));
        StringAssert.Contains(branch.CountSql, "OccurredAtUtc >= @FromUtc");
        StringAssert.Contains(branch.CountSql, "OccurredAtUtc <= @ToUtc");
        StringAssert.Contains(
            branch.CountSql,
            "CHARINDEX(@PathContains, RequestPath) > 0");
        Assert.DoesNotContain(
            "@FromUtc IS NULL",
            branch.CountSql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "@HttpMethod",
            branch.CountSql,
            StringComparison.Ordinal);
        StringAssert.Contains(recompile.CountSql, "OPTION (RECOMPILE)");
        StringAssert.Contains(recompile.ListSql, "OPTION (RECOMPILE)");
    }

    [TestMethod]
    public void SqlServer_plan_metrics_extract_compile_cache_and_runtime_cost()
    {
        const string showPlan =
            """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple RetrievedFromCache="true">
                      <QueryPlan CompileTime="7" CompileCPU="5" CompileMemory="128">
                        <QueryTimeStats ElapsedTime="13" CpuTime="11" />
                        <RelOp>
                          <RunTimeInformation>
                            <RunTimeCountersPerThread ActualLogicalReads="41" ActualRowsRead="1000" />
                            <RunTimeCountersPerThread ActualLogicalReads="3" ActualRowsRead="10" />
                          </RunTimeInformation>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var metrics = AuditingSqlServerPlanMetrics.Parse(showPlan);

        Assert.AreEqual(7L, metrics.CompileTimeMilliseconds);
        Assert.AreEqual(5L, metrics.CompileCpuMilliseconds);
        Assert.AreEqual(128L, metrics.CompileMemoryKilobytes);
        Assert.AreEqual(13L, metrics.ElapsedTimeMilliseconds);
        Assert.AreEqual(11L, metrics.CpuTimeMilliseconds);
        Assert.AreEqual(44L, metrics.ActualLogicalReads);
        Assert.AreEqual(1_010L, metrics.ActualRowsRead);
        Assert.IsTrue(metrics.RetrievedFromCache);
    }

    [TestMethod]
    public void MySql_index_ab_mode_is_explicit_and_rejects_other_providers()
    {
        var options = AuditingQueryBenchmarkOptions.Parse(
        [
            "--mode",
            "mysql-index-ab",
            "--providers",
            "mysql",
        ]);

        Assert.AreEqual(
            AuditingQueryBenchmarkMode.MySqlIndexAb,
            options.Mode);
        StringAssert.Contains(
            options.OutputDirectory.Replace('\\', '/'),
            "auditing-query-mysql-index-ab/");
        Assert.ThrowsExactly<ArgumentException>(() =>
            AuditingQueryBenchmarkOptions.Parse(
            [
                "--mode",
                "mysql-index-ab",
                "--providers",
                "sqlserver",
            ]));
    }

    [TestMethod]
    public void MySql_index_strategy_keeps_count_and_uses_only_the_fixed_index()
    {
        var current = AuditingMySqlQueryFactory.Create(
            AuditingMySqlQueryStrategy.CurrentOptimizer);
        var forced = AuditingMySqlQueryFactory.Create(
            AuditingMySqlQueryStrategy.ForceOccurredAtIndex);

        Assert.AreEqual(
            current.CountSql.ReplaceLineEndings("\n"),
            forced.CountSql.ReplaceLineEndings("\n"));
        Assert.AreEqual(
            AuditingQuerySql.MySqlList.ReplaceLineEndings("\n"),
            current.ListSql.ReplaceLineEndings("\n"));
        StringAssert.Contains(
            forced.ListSql,
            "FORCE INDEX (IX_fn_auditing_access_log_OccurredAtUtc_Id)");
        StringAssert.Contains(forced.ListSql, "LIMIT @PageSize OFFSET @Offset");
        Assert.DoesNotContain(
            "FORCE INDEX",
            forced.CountSql,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void MySql_index_ab_sampling_reverses_each_measurement_pair()
    {
        var order = AuditingMySqlIndexAbSampling.CreateStrategyOrder(3);

        CollectionAssert.AreEqual(
            new[]
            {
                AuditingMySqlQueryStrategy.CurrentOptimizer,
                AuditingMySqlQueryStrategy.ForceOccurredAtIndex,
                AuditingMySqlQueryStrategy.ForceOccurredAtIndex,
                AuditingMySqlQueryStrategy.CurrentOptimizer,
                AuditingMySqlQueryStrategy.CurrentOptimizer,
                AuditingMySqlQueryStrategy.ForceOccurredAtIndex,
            },
            order.ToArray());
    }

    [TestMethod]
    public void MySql_late_materialization_mode_is_explicit_and_rejects_other_providers()
    {
        var options = AuditingQueryBenchmarkOptions.Parse(
        [
            "--mode",
            "mysql-late-materialization-ab",
            "--providers",
            "mysql",
        ]);

        Assert.AreEqual(
            AuditingQueryBenchmarkMode.MySqlLateMaterializationAb,
            options.Mode);
        StringAssert.Contains(
            options.OutputDirectory.Replace('\\', '/'),
            "auditing-query-mysql-late-materialization-ab/");
        Assert.ThrowsExactly<ArgumentException>(() =>
            AuditingQueryBenchmarkOptions.Parse(
            [
                "--mode",
                "mysql-late-materialization-ab",
                "--providers",
                "sqlserver",
            ]));
    }

    [TestMethod]
    public void MySql_late_materialization_uses_fixed_inner_keys_and_separate_strategy_set()
    {
        var indexStrategies = AuditingMySqlQueryFactory.GetStrategies(
            AuditingQueryBenchmarkMode.MySqlIndexAb);
        var lateMaterializationStrategies =
            AuditingMySqlQueryFactory.GetStrategies(
                AuditingQueryBenchmarkMode.MySqlLateMaterializationAb);
        var query = AuditingMySqlQueryFactory.Create(
            AuditingMySqlQueryStrategy.LateMaterialization);

        CollectionAssert.AreEqual(
            new[]
            {
                AuditingMySqlQueryStrategy.CurrentOptimizer,
                AuditingMySqlQueryStrategy.ForceOccurredAtIndex,
            },
            indexStrategies.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                AuditingMySqlQueryStrategy.CurrentOptimizer,
                AuditingMySqlQueryStrategy.LateMaterialization,
            },
            lateMaterializationStrategies.ToArray());
        StringAssert.Contains(
            query.ListSql,
            "SELECT Id, OccurredAtUtc\n    FROM fn_auditing_access_log");
        StringAssert.Contains(
            query.ListSql,
            "INNER JOIN");
        StringAssert.Contains(
            query.ListSql,
            "page_keys.Id = access_log.Id");
        StringAssert.Contains(
            query.ListSql,
            "ORDER BY page_keys.OccurredAtUtc DESC, page_keys.Id DESC");
        Assert.DoesNotContain(
            "FORCE INDEX",
            query.ListSql,
            StringComparison.Ordinal);
    }

    [TestMethod]
    public void Query_page_result_equality_includes_ordered_row_ids()
    {
        var baseline = new AuditingQueryPageResult(100, 2, "01|02");
        var differentRows = new AuditingQueryPageResult(100, 2, "01|03");

        Assert.AreNotEqual(baseline, differentRows);
    }

    [TestMethod]
    public void Cursor_ab_mode_accepts_both_providers_and_uses_isolated_artifact_group()
    {
        var options = AuditingQueryBenchmarkOptions.Parse(
        [
            "--mode",
            "cursor-ab",
        ]);

        Assert.AreEqual(AuditingQueryBenchmarkMode.CursorAb, options.Mode);
        CollectionAssert.AreEquivalent(
            new[] { "sqlserver", "mysql" },
            options.Providers.ToArray());
        StringAssert.Contains(
            options.OutputDirectory.Replace('\\', '/'),
            "auditing-query-cursor-ab/");
    }

    [TestMethod]
    [DataRow("sqlserver")]
    [DataRow("mysql")]
    public void Cursor_ab_uses_production_keyset_shape_without_count_or_offset(
        string provider)
    {
        var query = AuditingCursorQueryFactory.Create(
            provider,
            AuditingCursorQueryStrategy.CursorEndpoint);

        Assert.IsNull(query.CountSql);
        Assert.DoesNotContain(
            "COUNT(",
            query.ListSql,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "OFFSET",
            query.ListSql,
            StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(
            query.ListSql,
            "OccurredAtUtc < @CursorOccurredAtUtc");
        StringAssert.Contains(query.ListSql, "Id < @CursorId");
    }

    [TestMethod]
    public void Cursor_ab_sql_matches_production_statements()
    {
        var sqlServerOffset = AuditingCursorQueryFactory.Create(
            "sqlserver",
            AuditingCursorQueryStrategy.OffsetEndpoint);
        var sqlServer = AuditingCursorQueryFactory.Create(
            "sqlserver",
            AuditingCursorQueryStrategy.CursorEndpoint);
        var mySqlOffset = AuditingCursorQueryFactory.Create(
            "mysql",
            AuditingCursorQueryStrategy.OffsetEndpoint);
        var mySql = AuditingCursorQueryFactory.Create(
            "mysql",
            AuditingCursorQueryStrategy.CursorEndpoint);

        Assert.AreEqual(
            NormalizeSql(AccessLogSql.CreatePageFilteredSqlServer(
                hasFromUtc: false,
                hasToUtc: false,
                hasHttpMethod: false,
                hasStatusCode: false,
                hasPathContains: false).Text),
            NormalizeSql(
                $"{sqlServerOffset.CountSql};{Environment.NewLine}{sqlServerOffset.ListSql}"));
        Assert.AreEqual(
            NormalizeSql(AccessLogSql.CreateCursorListSqlServer(
                hasCursor: true,
                hasFromUtc: false,
                hasToUtc: false,
                hasHttpMethod: false,
                hasStatusCode: false,
                hasPathContains: false).Text),
            NormalizeSql(sqlServer.ListSql));
        Assert.AreEqual(
            NormalizeSql(AccessLogSql.PageFilteredMySql.Text),
            NormalizeSql(
                $"{mySqlOffset.CountSql};{Environment.NewLine}{mySqlOffset.ListSql}"));
        Assert.AreEqual(
            NormalizeSql(AccessLogSql.CursorListAfterMySql.Text),
            NormalizeSql(mySql.ListSql));
    }

    private static string NormalizeSql(string sql) =>
        string.Join(
            " ",
            sql.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

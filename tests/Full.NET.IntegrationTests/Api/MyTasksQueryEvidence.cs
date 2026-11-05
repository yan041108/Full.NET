using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.IntegrationTests.Api;

/// <summary>只比较本人摘要查询的索引成本；小数据单并发结果不代表系统容量。</summary>
internal static class MyTasksQueryEvidence
{
    public static async Task SeedAsync(DbConnection database, DatabaseProvider provider, Guid tenant, Guid otherTenant, Guid user)
    {
        await using var transaction = await database.BeginTransactionAsync();
        for (var t = 0; t < 2; t++)
        for (var u = 0; u < 10; u++)
        {
            var tenantId = t == 0 ? tenant : otherTenant; var userId = t == 0 && u == 0 ? user : Guid.CreateVersion7();
            var imports = Enumerable.Range(0, 100).Select(i => Import(tenantId, userId, i)).ToArray();
            var reports = Enumerable.Range(0, 100).Select(i => Report(tenantId, userId, i)).ToArray();
            await database.ExecuteAsync(ImportExportTaskSql.Insert.Text, imports, transaction);
            await database.ExecuteAsync((provider == DatabaseProvider.SqlServer ? ReportingExportTaskSql.InsertSqlServer : ReportingExportTaskSql.Insert).Text, reports, transaction);
        }
        await transaction.CommitAsync();
    }

    public static async Task MeasureAsync(DbConnection database, DatabaseProvider provider, string connectionString, Guid tenant, Guid user)
    {
        var evidenceRoot = Environment.GetEnvironmentVariable("FULLNET_MY_TASKS_EVIDENCE_ROOT");
        var directory = evidenceRoot is null ? Directory.CreateTempSubdirectory("fullnet-my-tasks-query-evidence-").FullName
            : Directory.CreateDirectory(Path.Combine(evidenceRoot, provider.ToString())).FullName;
        var results = new List<object>();
        foreach (var source in new[] { "import", "report" })
        {
            var table = source == "import" ? "fn_import_export_task" : "fn_reporting_export_task";
            var index = "IX_" + table + "_Tenant_User_Created";
            await database.ExecuteAsync(provider == DatabaseProvider.SqlServer ? $"DROP INDEX {index} ON {table}" : $"DROP INDEX {index} ON {table}");
            foreach (var indexed in new[] { false, true })
            {
                if (indexed)
                {
                    var migration = Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",
                        provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql", "354_OwnTaskCursorIndexes.sql");
                    // 另一来源已完成时重跑完整脚本，覆盖部分 DDL 已提交但尚未记账的恢复边界。
                    var script = await File.ReadAllTextAsync(migration);
                    if (provider == DatabaseProvider.MySql)
                    {
                        // 迁移 DDL 允许固定会话变量；业务查询连接仍禁止未绑定参数被误当作变量。
                        var ddlConnection = Full.NET.Data.MySql.MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true);
                        await using var ddl = new MySqlConnector.MySqlConnection(ddlConnection); await ddl.OpenAsync();
                        await ddl.ExecuteAsync(script); await ddl.ExecuteAsync(script);
                    }
                    else { await database.ExecuteAsync(script); await database.ExecuteAsync(script); }
                }
                if (provider == DatabaseProvider.MySql)
                    await database.ExecuteAsync($"ANALYZE TABLE {table}");
                foreach (var shape in new[] { "first", "after", "filtered" })
                {
                    var statement = Statement(source, provider, shape == "after");
                    Assert.AreEqual(1, statement.Text.Split("SELECT", StringSplitOptions.None).Length - 1);
                    foreach (var forbidden in new[] { "COUNT(", "PreviewRowsJson", "ExecutionRowsJson", "ActorSessionBindingJson", "ParametersJson" })
                        Assert.IsFalse(statement.Text.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
                    var parameters = new { TenantId = tenant, RequestedByUserId = user, Take = 21,
                        StatusKey = shape == "filtered" ? source == "import" ? "execution_failed" : "failed" : (string?)null,
                        BeforeCreatedAtUtc = new DateTime(2026, 11, 3, 0, 0, 4, DateTimeKind.Utc), BeforeId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff") };
                    var samples = new List<double>();
                    for (var i = 0; i < 35; i++)
                    {
                        var timer = Stopwatch.StartNew(); var rows = (await database.QueryAsync(statement.Text, parameters)).AsList(); timer.Stop();
                        Assert.IsTrue(rows.Count <= 21); if (i >= 5) samples.Add(timer.Elapsed.TotalMilliseconds);
                    }
                    string plan;
                    if (provider == DatabaseProvider.MySql)
                    {
                        plan = await database.QuerySingleAsync<string>("EXPLAIN FORMAT=JSON " + statement.Text, parameters);
                        plan += Environment.NewLine + await database.QuerySingleAsync<string>("EXPLAIN ANALYZE " + statement.Text, parameters);
                    }
                    else
                    {
                        await database.ExecuteAsync("SET STATISTICS XML ON");
                        try
                        {
                            using var reader = await database.ExecuteReaderAsync(statement.Text, parameters);
                            plan = "";
                            do { while (reader.Read()) if (reader.FieldCount == 1 && reader.GetValue(0) is string value && value.Contains("ShowPlanXML", StringComparison.Ordinal)) plan = value; } while (reader.NextResult());
                            Assert.IsFalse(string.IsNullOrEmpty(plan));
                        }
                        finally { await database.ExecuteAsync("SET STATISTICS XML OFF"); }
                    }
                    var planFile = Path.Combine(directory, $"{provider}-{source}-{indexed}-{shape}.plan.txt"); await File.WriteAllTextAsync(planFile, plan);
                    if (indexed)
                    {
                        StringAssert.Contains(plan, index);
                        if (provider == DatabaseProvider.MySql)
                        {
                            using var explained = JsonDocument.Parse(plan[..plan.IndexOf(Environment.NewLine + "->", StringComparison.Ordinal)]);
                            Assert.IsTrue(ChosenIndex(explained.RootElement, index), "候选名称出现在 possible_keys 不代表实际选中。" + plan);
                            Assert.IsFalse(plan.Contains("\"using_filesort\": true", StringComparison.Ordinal), plan);
                        }
                    }
                    var readRows = provider == DatabaseProvider.SqlServer ? XDocument.Parse(plan).Descendants().Where(node => node.Name.LocalName == "RunTimeCountersPerThread")
                        .Sum(node => double.TryParse(node.Attribute("ActualRowsRead")?.Value, System.Globalization.CultureInfo.InvariantCulture, out var rows) ? rows : 0) : (double?)null;
                    results.Add(new { source, indexed, shape, latencyMs = Percentiles(samples), readRows,
                        sort = provider == DatabaseProvider.SqlServer ? plan.Contains("PhysicalOp=\"Sort\"", StringComparison.Ordinal) : plan.Contains("using_filesort\": true", StringComparison.Ordinal), planFile });
                }
                var writes = new List<double>(); var updates = new List<double>();
                for (var i = 0; i < 30; i++)
                {
                    var id = Guid.CreateVersion7(); var timer = Stopwatch.StartNew();
                    if (source == "import") await database.ExecuteAsync(ImportExportTaskSql.Insert.Text, Import(tenant, user, i, id));
                    else await database.ExecuteAsync((provider == DatabaseProvider.SqlServer ? ReportingExportTaskSql.InsertSqlServer : ReportingExportTaskSql.Insert).Text, Report(tenant, user, i, id));
                    timer.Stop(); writes.Add(timer.Elapsed.TotalMilliseconds); await database.ExecuteAsync($"DELETE FROM {table} WHERE Id=@Id", new { Id = id });
                    timer.Restart(); await database.ExecuteAsync($"UPDATE {table} SET StatusKey=@Status WHERE TenantId=@TenantId AND RequestedByUserId=@UserId", new
                    { Status = i % 2 == 0 ? source == "import" ? "execution_failed" : "failed" : source == "import" ? "execution_succeeded" : "succeeded", TenantId = tenant, UserId = user });
                    timer.Stop(); updates.Add(timer.Elapsed.TotalMilliseconds);
                }
                results.Add(new { source, indexed, insertLatencyMs = Percentiles(writes), update100RowsLatencyMs = Percentiles(updates) });
                // 下一轮读取保持相同状态分布。
                await database.ExecuteAsync($"UPDATE {table} SET StatusKey=@Failed WHERE TenantId=@TenantId AND RequestedByUserId=@UserId AND CreatedAtUtc IN (@T0,@T2,@T4,@T6,@T8)", new
                { Failed = source == "import" ? "execution_failed" : "failed", TenantId = tenant, UserId = user,
                    T0 = At(0), T2 = At(2), T4 = At(4), T6 = At(6), T8 = At(8) });
            }
        }
        var summary = new { provider = provider.ToString(), rowsPerSource = 2000, tenants = 2, usersPerTenant = 10, concurrency = 1, warmup = 5, samples = 30,
            capacityStatus = "Capacity-not-verified", results };
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("本人任务查询证据：" + directory);
    }

    private static object Percentiles(List<double> values)
    {
        values.Sort(); double AtPercent(double p) => Math.Round(values[(int)Math.Ceiling(values.Count * p) - 1], 4);
        return new { p50 = AtPercent(.5), p95 = AtPercent(.95), p99 = AtPercent(.99) };
    }
    private static bool ChosenIndex(JsonElement element, string index) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Any(property => property.Name == "key" && property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() == index
            || ChosenIndex(property.Value, index)),
        JsonValueKind.Array => element.EnumerateArray().Any(value => ChosenIndex(value, index)),
        _ => false,
    };
    private static SqlStatement Statement(string source, DatabaseProvider provider, bool after) => (source, provider, after) switch
    {
        ("import", DatabaseProvider.SqlServer, false) => ImportExportTaskSql.MineFirstSqlServer,
        ("import", DatabaseProvider.SqlServer, true) => ImportExportTaskSql.MineAfterSqlServer,
        ("import", DatabaseProvider.MySql, false) => ImportExportTaskSql.MineFirstMySql,
        ("import", DatabaseProvider.MySql, true) => ImportExportTaskSql.MineAfterMySql,
        ("report", DatabaseProvider.SqlServer, false) => ReportingExportTaskSql.MineFirstSqlServer,
        ("report", DatabaseProvider.SqlServer, true) => ReportingExportTaskSql.MineAfterSqlServer,
        ("report", DatabaseProvider.MySql, false) => ReportingExportTaskSql.MineFirstMySql,
        _ => ReportingExportTaskSql.MineAfterMySql,
    };
    private static DateTime At(int second) => new(2026, 11, 3, 0, 0, second, DateTimeKind.Utc);
    private static ImportExportTaskRecord Import(Guid tenant, Guid user, int i, Guid? id = null) => new()
    {
        SourceExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7),
        Id = id ?? Guid.CreateVersion7(), TenantId = tenant, RequestedByUserId = user, CreatedAtUtc = At(i % 10),
        SchemaKey = "my_tasks", SchemaDisplayName = "Task evidence", WorksheetKey = "sheet1", SourceFileId = Guid.CreateVersion7(), SourceFileName = "evidence.xlsx",
        StatusKey = i % 2 == 0 ? "execution_failed" : "execution_succeeded", TotalRows = 1, ValidRowCount = 1,
        PreviewRowsJson = "[\"" + new string('p', 2048) + "\"]", ExecutionRowsJson = "[]", Version = 1,
    };
    private static ReportingExportTaskRecord Report(Guid tenant, Guid user, int i, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(), TenantId = tenant, RequestedByUserId = user, CreatedAtUtc = At(i % 10), DefinitionId = Guid.CreateVersion7(), VersionNumber = 1,
        DefinitionKey = "my_tasks", DefinitionName = "Task evidence", FormatKey = "excel", ParametersJson = "[\"" + new string('p', 2048) + "\"]",
        StatusKey = i % 2 == 0 ? "failed" : "succeeded", Version = 1,
    };
}

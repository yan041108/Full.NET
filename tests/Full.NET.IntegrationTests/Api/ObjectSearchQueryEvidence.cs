using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;

namespace Full.NET.IntegrationTests.Api;

/// <summary>只在显式采集时建立两千行来源夹具，保留实际近期索引计划，不推导容量。</summary>
internal static class ObjectSearchQueryEvidence
{
    internal static async Task<bool> CaptureAsync(DbConnection database, DatabaseProvider provider, string connectionString, Guid actor, Guid tenant, Guid unit)
    {
        var root = Environment.GetEnvironmentVariable("FULLNET_OBJECT_SEARCH_EVIDENCE"); if (string.IsNullOrWhiteSpace(root)) return false;
        var directory = Directory.CreateDirectory(Path.Combine(root, provider.ToString())).FullName;
        // 文档索引已经完成而样例索引缺失，模拟部分 DDL 完成但尚未记账；重复恢复不能移除已有结构。
        var index = "IX_demo_enterprise_request_enterprise_request_Search_Recent";
        await database.ExecuteAsync(provider == DatabaseProvider.SqlServer ? $"DROP INDEX {index} ON demo_enterprise_request_enterprise_request"
            : $"DROP INDEX {index} ON demo_enterprise_request_enterprise_request");
        var repository = FullNetApiFactory.FindRepositoryRoot();
        var scripts = new[] { Path.Combine(repository, "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations", provider.ToString(), "356_DocumentRecentSearchIndex.sql"),
            Path.Combine(repository, "samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/Migrations", provider.ToString(), "004_EnterpriseRequestRecentSearchIndex.sql") };
        if (provider == DatabaseProvider.MySql)
        {
            await using var ddl = new MySqlConnector.MySqlConnection(Full.NET.Data.MySql.MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true));
            await ddl.OpenAsync(); for (var repeat = 0; repeat < 2; repeat++) foreach (var script in scripts) await ddl.ExecuteAsync(await File.ReadAllTextAsync(script));
        }
        else for (var repeat = 0; repeat < 2; repeat++) foreach (var script in scripts) await database.ExecuteAsync(await File.ReadAllTextAsync(script));
        var now = DateTimeOffset.UtcNow; var otherTenant = Guid.CreateVersion7();
        await database.ExecuteAsync(DocumentItemSql.Insert.Text, Enumerable.Range(0, 2000).Select(i => new DocumentItemRecord {
            Id = Guid.CreateVersion7(), Title = "BudgetFind " + i, DocumentNo = "EVIDENCE-" + i, Description = "Body must not enter search summaries",
            Status = 0, DocumentType = 1, CreatedAtUtc = now.AddDays(i % 3 == 0 ? -100 : -(i % 30)), CreatedByUserId = actor, Version = 1 }));
        await database.ExecuteAsync(EnterpriseRequestSql.Insert, Enumerable.Range(0, 2000).Select(i => new EnterpriseRequestRecord(Guid.CreateVersion7(),
            i % 2 == 0 ? tenant : otherTenant, unit, "EVIDENCE-" + i, "BudgetFind " + i, "Draft", 0m, actor, 1,
            now.AddDays(i % 3 == 0 ? -100 : -(i % 30)), actor, null, null, false, null, null)));
        if (provider == DatabaseProvider.MySql) { await database.ExecuteAsync("ANALYZE TABLE fn_document_item"); await database.ExecuteAsync("ANALYZE TABLE demo_enterprise_request_enterprise_request"); }
        var results = new List<object>();
        foreach (var source in new[] { "document", "request" })
        foreach (var page in new[] { 1, 20 })
        {
            var statement = (source, provider) switch { ("document", DatabaseProvider.SqlServer) => DocumentSearchSql.SqlServer,
                ("document", _) => DocumentSearchSql.MySql, ("request", DatabaseProvider.SqlServer) => EnterpriseRequestSearchSql.SqlServer, _ => EnterpriseRequestSearchSql.MySql };
            var parameters = new DynamicParameters(); parameters.Add("TenantId", tenant); parameters.Add("ActorUserId", actor);
            parameters.Add("SinceUtc", now.UtcDateTime.Date.AddDays(-89), DbType.DateTime2); parameters.Add("UntilUtc", now.UtcDateTime.Date.AddDays(1), DbType.DateTime2);
            parameters.Add("Pattern", "%BudgetFind%"); parameters.Add("Offset", (page - 1) * 5); var samples = new List<double>();
            for (var attempt = 0; attempt < 25; attempt++)
            {
                var timer = Stopwatch.StartNew(); var rows = (await database.QueryAsync(statement.Text, parameters)).AsList(); timer.Stop(); Assert.AreEqual(6, rows.Count);
                if (attempt >= 5) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            string plan;
            if (provider == DatabaseProvider.MySql) plan = await database.QuerySingleAsync<string>("EXPLAIN FORMAT=JSON " + statement.Text, parameters)
                + Environment.NewLine + await database.QuerySingleAsync<string>("EXPLAIN ANALYZE " + statement.Text, parameters);
            else
            {
                await database.ExecuteAsync("SET STATISTICS XML ON"); plan = "";
                try
                {
                    using var reader = await database.ExecuteReaderAsync(statement.Text, parameters);
                    do { while (reader.Read()) if (reader.FieldCount == 1 && reader.GetValue(0) is string value && value.Contains("ShowPlanXML", StringComparison.Ordinal)) plan = value; } while (reader.NextResult());
                }
                finally { await database.ExecuteAsync("SET STATISTICS XML OFF"); }
            }
            Assert.IsFalse(string.IsNullOrEmpty(plan)); var name = source + "-page" + page;
            await File.WriteAllTextAsync(Path.Combine(directory, name + ".plan.txt"), plan);
            samples.Sort(); results.Add(new { source, page, p50 = samples[9], p95 = samples[18], p99 = samples[19] });
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new { rowsPerSource = 2000, concurrency = 1, warmup = 5, samples = 20, results }, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}

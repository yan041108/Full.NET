using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Settings.Persistence;

namespace Full.NET.IntegrationTests.Api;

/// <summary>采集本人/共享摘要的实际查询计划；单并发小数据证据不代表系统容量。</summary>
internal static class QuerySchemeQueryEvidence
{
    internal static async Task CaptureAsync(DbConnection database, DatabaseProvider provider)
    {
        var root = Environment.GetEnvironmentVariable("FULLNET_QUERY_SCHEME_QUERY_EVIDENCE");
        if (string.IsNullOrEmpty(root)) return;
        var directory = Directory.CreateDirectory(Path.Combine(root, provider.ToString())).FullName;
        var tenant = Guid.CreateVersion7(); var otherTenant = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var timestamp = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc).AddTicks(1234560);
        var rows = new List<object>();
        foreach (var tenantId in new[] { tenant, otherTenant })
        for (var owner = 0; owner < 32; owner++)
        {
            var ownerId = tenantId == tenant && owner == 0 ? actor : Guid.CreateVersion7();
            for (var index = 0; index < 32; index++) rows.Add(new { Id = Guid.CreateVersion7(), TenantId = tenantId,
                ScopeKey = $"tenant:{tenantId:N}", UserId = ownerId, Name = "Evidence " + index,
                Shared = owner != 0 && index % 2 == 0, Now = timestamp });
        }
        await database.ExecuteAsync("""
            INSERT INTO fn_settings_query_scheme (Id,TenantId,ScopeKey,GridKey,OwnerUserId,Name,SchemaVersion,ConfigurationJson,IsShared,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@TenantId,@ScopeKey,'test.query_evidence',@UserId,@Name,1,'{}',@Shared,@Now,NULL,1)
            """, rows);
        if (provider == DatabaseProvider.MySql) await database.ExecuteAsync("ANALYZE TABLE fn_settings_query_scheme");
        var sql = QuerySchemeSql.For(host: false, provider); var results = new List<object>();
        foreach (var shared in new[] { false, true })
        {
            var parameters = new DynamicParameters(); parameters.Add("TenantId", tenant); parameters.Add("ScopeKey", $"tenant:{tenant:N}");
            parameters.Add("GridKey", "test.query_evidence"); parameters.Add("UserId", actor); parameters.Add("Take", 8);
            var first = shared ? sql.SharedFirst : sql.MineFirst;
            var firstRows = (await database.QueryAsync<QuerySchemeRecord>(first.Text, parameters)).ToArray(); Assert.AreEqual(8, firstRows.Length);
            parameters.Add("BeforeCreatedAtUtc", firstRows[6].CreatedAtUtc.UtcDateTime, DbType.DateTime2);
            parameters.Add("BeforeId", firstRows[6].Id);
            foreach (var after in new[] { false, true })
            {
                var statement = after ? shared ? sql.SharedAfter : sql.MineAfter : first;
                var samples = new List<double>();
                for (var attempt = 0; attempt < 25; attempt++)
                {
                    var timer = Stopwatch.StartNew(); var page = (await database.QueryAsync<QuerySchemeRecord>(statement.Text, parameters)).ToArray(); timer.Stop();
                    Assert.AreEqual(8, page.Length); if (after) Assert.IsFalse(page.Take(7).Any(row => firstRows.Take(7).Any(firstRow => firstRow.Id == row.Id)));
                    if (attempt >= 5) samples.Add(timer.Elapsed.TotalMilliseconds);
                }
                string plan;
                if (provider == DatabaseProvider.MySql)
                    plan = await database.QuerySingleAsync<string>("EXPLAIN FORMAT=JSON " + statement.Text, parameters) + Environment.NewLine
                        + await database.QuerySingleAsync<string>("EXPLAIN ANALYZE " + statement.Text, parameters);
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
                Assert.IsFalse(string.IsNullOrEmpty(plan));
                var name = (shared ? "shared" : "mine") + (after ? "-after" : "-first");
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".plan.txt"), plan);
                samples.Sort(); results.Add(new { name, p50 = samples[9], p95 = samples[18], p99 = samples[19] });
            }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "summary.json"), JsonSerializer.Serialize(new { provider, rows = 2048, tenants = 2,
            ownersPerTenant = 32, maximumPerOwner = 32, concurrency = 1, warmup = 5, samples = 20, results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

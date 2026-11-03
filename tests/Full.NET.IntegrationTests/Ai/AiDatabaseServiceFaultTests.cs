using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.IntegrationTests.NativeAot;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>独占数据库服务停止并恢复后，原运行与预算仍沿原回执收敛。</summary>
[TestClass, DoNotParallelize]
public sealed class AiDatabaseServiceFaultTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_worker_reconciles_original_receipts_after_owned_database_service_restart(DatabaseProvider provider) => VerifyAsync(provider, crash: false);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_worker_reconciles_original_receipts_after_owned_database_service_crash(DatabaseProvider provider) => VerifyAsync(provider, crash: true);

    private static async Task VerifyAsync(DatabaseProvider provider, bool crash)
    {
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_SERVICE_FAULT_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a06-service-fault", stamp);
        var keys = Path.Combine(directory, "keys");
        var files = Path.Combine(directory, "files");
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { phase = "fixture-intent", provider = provider.ToString(), directory, keys, files }) + "\n");
        Directory.CreateDirectory(directory);
        // 仅成功的完整用例主动清理容器；失败时保留准确登记的独立实例供诊断。
        var service = await AiOwnedDatabaseService.StartAsync(provider, registry);
        string connection;
        if (provider == DatabaseProvider.SqlServer)
        {
            var database = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { phase = "database-intent", provider = provider.ToString(), containerId = service.Id, database }) + "\n");
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(service.ConnectionString) { InitialCatalog = "master", Pooling = false }.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE [{database}]");
            connection = new SqlConnectionStringBuilder(service.ConnectionString) { InitialCatalog = database, Pooling = false, ConnectTimeout = 3 }.ConnectionString;
        }
        else connection = new MySqlConnectionStringBuilder(service.ConnectionString) { Pooling = false, ConnectionTimeout = 3 }.ConnectionString;

        var runs = new List<Guid>();
        var settings = new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = keys, ["Files:Local:RootPath"] = files };
        using (var factory = new FullNetApiFactory(provider, connection, settings))
        {
            await factory.InitializeAsync(useSchemaTemplate: false);
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            context.SetHost();
            var tenantKey = "a06-service-" + stamp;
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new(tenantKey, "服务故障测试", tenantKey + ".localhost"));
            Assert.IsTrue(tenant.IsSuccess);
            context.Clear();
            foreach (var tenantId in new Guid?[] { null, tenant.Value!.Id })
            {
                var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId);
                if (tenantId is { } id) context.SetTenant(new(id, tenantKey, "服务故障测试")); else context.SetHost();
                try
                {
                    var store = scope.ServiceProvider.GetRequiredService<AiAgentRunStore>();
                    var budget = scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>();
                    var modelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
                    await budget.ReserveAsync(new(runId, runId, modelId, "agent", new('F', 64), 100, 100, ProviderKey: "ollama", ModelId: "model"));
                    var run = (await store.FindByIdAsync(runId))!;
                    var lease = (await store.TryAcquireAsync(runId, "a06-service-before", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                    var receipt = AiModelRunCheckpoint.Start(run, new() { Id = modelId, Version = 1 }, "")
                        .Complete(new("synthetic-receipt", JsonSerializer.SerializeToElement(true), 2, 1));
                    var payload = receipt.ToJson();
                    Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
                        new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload, AiModelRunCheckpoint.Digest(payload)),
                        new(Guid.CreateVersion7(), 1, "run.node_completed", 1, "{}"))));
                    await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new("test.ai.service.reconciliation",
                        "UPDATE fn_ai_agent_run SET StatusKey='reconciliation_required',Version=Version+1,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@RunId", SqlDataScope.Global),
                        AiSqlParameters.Create(("RunId", runId)));
                    await budget.SettleAsync(runId, new(null, null), "failed");
                    runs.Add(runId);
                }
                finally { context.Clear(); }
            }
        }
        var versions = new Dictionary<Guid, long>();
        await using (var before = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection))
            foreach (var run in runs) versions[run] = await before.ExecuteScalarAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run });

        if (!NativeWorkerArtifactLocator.TryResolve(out var artifact, out var reason)) Assert.Fail(reason ?? "缺少 Native Worker");
        var workerSettings = new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = keys, ["FullNet:Ai:AgentRuntime:ReconcileCompletedReceipts"] = "false", ["FullNet:Ai:AgentRuntime:PollMilliseconds"] = "500" };
        await using (var worker = await NativeWorkerProcessHost.StartAsync(artifact!, provider, connection, NativeAotTestTimeouts.ProcessStartup, filesRootPath: files, additionalSettings: workerSettings))
        {
            Console.WriteLine($"A06_SERVICE_WORKER_PID={worker.ProcessId}");
            if (crash) await service.KillAsync(); else await service.StopAsync();
            var unavailable = false;
            try
            {
                await using var probe = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
                await probe.OpenAsync();
            }
            catch (SqlException) { unavailable = true; }
            catch (MySqlException) { unavailable = true; }
            Assert.IsTrue(unavailable, "自有数据库服务停止后必须实际不可连接。");
            Assert.IsNull(worker.ExitCode, "数据库停止时原 Native Worker 必须仍在运行。");
            await service.RestartAsync();
            // Docker 随机映射的宿主端口在容器重启后可能变化；显式读取同一容器的新映射，不伪装原地址自动恢复。
            if (provider == DatabaseProvider.SqlServer)
            {
                var old = new SqlConnectionStringBuilder(connection);
                var current = new SqlConnectionStringBuilder(service.ConnectionString) { InitialCatalog = old.InitialCatalog, Pooling = false, ConnectTimeout = 3 };
                Console.WriteLine($"A06_SERVICE_PORT_REBOUND={old.DataSource != current.DataSource}");
                connection = current.ConnectionString;
                // SQL Server 的 master 就绪可能早于目标库恢复；只对测试探针的 4060 等待目标库可用。
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                while (true)
                {
                    try
                    {
                        await using var readiness = new SqlConnection(connection);
                        await readiness.OpenAsync();
                        break;
                    }
                    catch (SqlException error) when (error.Number == 4060 && DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(200);
                    }
                }
            }
            else
            {
                var old = new MySqlConnectionStringBuilder(connection);
                var current = new MySqlConnectionStringBuilder(service.ConnectionString) { Database = old.Database, Pooling = false, ConnectionTimeout = 3 };
                Console.WriteLine($"A06_SERVICE_PORT_REBOUND={old.Port != current.Port}");
                connection = current.ConnectionString;
            }
            await using var after = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            foreach (var run in runs)
            {
                Assert.AreEqual("reconciliation_required", await after.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
                Assert.AreEqual(versions[run], await after.ExecuteScalarAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
                Assert.AreEqual(1L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
                Assert.AreEqual(1L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id AND UsageStatus='unknown'", new { Id = run }));
            }
            await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, worker.ExitCode);
        }
        workerSettings["FullNet:Ai:AgentRuntime:ReconcileCompletedReceipts"] = "true";
        await using (var worker = await NativeWorkerProcessHost.StartAsync(artifact!, provider, connection, NativeAotTestTimeouts.ProcessStartup, filesRootPath: files, additionalSettings: workerSettings))
        {
            Console.WriteLine($"A06_SERVICE_WORKER_PID={worker.ProcessId}");
            await using var after = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(75);
            while (DateTime.UtcNow < deadline)
            {
                var count = 0;
                foreach (var run in runs) count += await after.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id AND UsageStatus='known' AND InputTokens=2 AND OutputTokens=1", new { Id = run });
                if (count == runs.Count) break;
                await Task.Delay(200);
            }
            foreach (var run in runs)
            {
                Assert.AreEqual("failed", await after.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
                Assert.AreEqual(versions[run] + 1, await after.ExecuteScalarAsync<long>("SELECT Version FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
                Assert.AreEqual(1L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE RunId=@Id AND Id=@Id AND UsageStatus='known' AND InputTokens=2 AND OutputTokens=1", new { Id = run }));
                Assert.AreEqual(1L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE RunId=@Id", new { Id = run }));
                Assert.AreEqual(1L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_checkpoint WHERE RunId=@Id", new { Id = run }));
                Assert.AreEqual(1L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id AND EventType='run.automatically_reconciled'", new { Id = run }));
                Assert.AreEqual(3L, await after.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
            }
            await worker.StopGracefullyAsync(TimeSpan.FromSeconds(30));
            Assert.AreEqual(0, worker.ExitCode);
        }
        await service.DisposeAsync();
    }
}

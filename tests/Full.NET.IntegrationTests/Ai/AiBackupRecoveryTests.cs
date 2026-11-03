using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Agents.Definitions;
using Full.NET.Agents.Runtime;
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

/// <summary>真实备份恢复后，原运行、检查点与未知用量只能沿原操作对账。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AiBackupRecoveryTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Original_receipt_and_ledger_survive_real_backup_restore(DatabaseProvider provider) => VerifyAsync(provider, native: false);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_worker_recovers_restored_receipt_after_database_disconnect(DatabaseProvider provider) => VerifyAsync(provider, native: true);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Interrupted_backup_preserves_original_receipt_and_ledger(DatabaseProvider provider) => VerifyAsync(provider, native: false, interruptBackup: true);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_worker_reconciles_after_interrupted_backup_and_restore(DatabaseProvider provider) => VerifyAsync(provider, native: true, interruptBackup: true);

    private static async Task VerifyAsync(DatabaseProvider provider, bool native, bool interruptBackup = false)
    {
        var registry = Environment.GetEnvironmentVariable("FULLNET_A06_BACKUP_REGISTRY")
            ?? throw new InvalidOperationException("必须提供本批独立 registry。");
        var source = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var target = "fullnet_it_" + Guid.CreateVersion7().ToString("N");
        var stamp = Guid.CreateVersion7().ToString("N");
        var directory = Path.Combine(Path.GetTempPath(), "fullnet-a06-recovery", stamp);
        var keys = Path.Combine(directory, "keys");
        var files = Path.Combine(directory, "files");
        var backup = provider == DatabaseProvider.SqlServer
            ? "/var/opt/mssql/data/a06_recovery_" + stamp + ".bak" : Path.Combine(directory, "backup.sql");
        var interrupted = provider == DatabaseProvider.SqlServer && interruptBackup
            ? "/var/opt/mssql/data/a06_recovery_" + Guid.CreateVersion7().ToString("N") + ".bak" : null;
        await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), source, target, backup, interrupted, keys, files }) + "\n");
        Directory.CreateDirectory(directory);
        var databases = new HashSet<string>(StringComparer.Ordinal) { source, target };
        var media = new HashSet<string>(StringComparer.Ordinal) { backup };
        if (interrupted is not null) media.Add(interrupted);
        var settings = new Dictionary<string, string?> { ["DataProtection:KeyRingPath"] = keys, ["Files:Local:RootPath"] = files };
        string sourceConnection;
        if (provider == DatabaseProvider.SqlServer)
        {
            var master = await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync();
            await using var admin = new SqlConnection(master);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.databases WHERE name=@Name", new { Name = source }));
            await admin.ExecuteAsync($"CREATE DATABASE [{source}]");
            sourceConnection = new SqlConnectionStringBuilder(master) { InitialCatalog = source, Pooling = false }.ConnectionString;
        }
        else
        {
            var root = await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync();
            await using var admin = new MySqlConnection(new MySqlConnectionStringBuilder(root) { Database = "mysql", Pooling = false }.ConnectionString);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME=@Name", new { Name = source }));
            await admin.ExecuteAsync($"CREATE DATABASE `{source}`");
            await admin.ExecuteAsync($"GRANT ALL PRIVILEGES ON `{source}`.* TO 'fullnet'@'%'");
            sourceConnection = new MySqlConnectionStringBuilder(root) { UserID = "fullnet", Database = source, Pooling = false }.ConnectionString;
        }
        var runs = new List<(Guid Run, Guid? Tenant)>();
        using (var factory = new FullNetApiFactory(provider, sourceConnection, settings))
        {
            await factory.InitializeAsync();
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
            context.SetHost();
            var tenantKey = "a06-backup-" + stamp;
            var tenant = await scope.ServiceProvider.GetRequiredService<ITenantProvisioningService>()
                .ProvisionAsync(new(tenantKey, "备份恢复测试", tenantKey + ".localhost"));
            Assert.IsTrue(tenant.IsSuccess);
            context.Clear();
            foreach (var tenantId in new Guid?[] { null, tenant.Value!.Id })
            {
                var runId = await AiWorkflowCheckpointAssertions.SeedWorkflowRunAsync(factory, AgentDefinitionRegistry.SingleTextKey, tenantId);
                if (tenantId is { } id) context.SetTenant(new(id, tenantKey, "备份恢复测试")); else context.SetHost();
                try
                {
                    var store = scope.ServiceProvider.GetRequiredService<AiAgentRunStore>();
                    var budget = scope.ServiceProvider.GetRequiredService<IAiOperationBudgetStore>();
                    var modelId = Guid.Parse("00000000-0000-0000-0000-000000000001");
                    await budget.ReserveAsync(new(runId, runId, modelId, "agent", new('F', 64), 100, 100, ProviderKey: "ollama", ModelId: "model"));
                    var run = (await store.FindByIdAsync(runId))!;
                    var lease = (await store.TryAcquireAsync(runId, "a06-before-backup", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)))!;
                    var state = AiModelRunCheckpoint.Start(run, new() { Id = modelId, Version = 1 }, "")
                        .Complete(new("synthetic-receipt", JsonSerializer.SerializeToElement(true), 2, 1));
                    var payload = state.ToJson();
                    Assert.IsTrue(await store.CommitProgressAsync(new(lease, "running", null,
                        new(Guid.CreateVersion7(), 1, 1, AgentFrameworkRuntime.FrameworkVersion, 1, payload, AiModelRunCheckpoint.Digest(payload)),
                        new(Guid.CreateVersion7(), 1, "run.node_completed", 1, "{}"))));
                    await scope.ServiceProvider.GetRequiredService<ICommandExecutor>().ExecuteAsync(new("test.ai.backup.reconciliation",
                        "UPDATE fn_ai_agent_run SET StatusKey='reconciliation_required',Version=Version+1,LeaseOwner=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@RunId", SqlDataScope.Global),
                        AiSqlParameters.Create(("RunId", runId)));
                    await budget.SettleAsync(runId, new(null, null), "failed");
                    runs.Add((runId, tenantId));
                }
                finally { context.Clear(); }
            }
        }

        string targetConnection;
        if (provider == DatabaseProvider.SqlServer)
        {
            var master = await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync();
            await using var admin = new SqlConnection(master);
            await admin.OpenAsync();
            if (interruptBackup) await AiBackupInterruptionAssertions.InterruptSqlServerAsync(admin, master, source, interrupted!, databases, media);
            await AiRecoveryBackupTransport.BackupSqlServerAsync(admin, source, backup, databases, media, default);
            await AiRecoveryBackupTransport.RestoreSqlServerAsync(admin, target, backup, databases, media, default);
            targetConnection = new SqlConnectionStringBuilder(sourceConnection) { InitialCatalog = target, Pooling = false }.ConnectionString;
        }
        else
        {
            var root = await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync();
            await using var admin = new MySqlConnection(new MySqlConnectionStringBuilder(root) { Database = "mysql", Pooling = false }.ConnectionString);
            await admin.OpenAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME=@Name", new { Name = target }));
            await admin.ExecuteAsync($"CREATE DATABASE `{target}`");
            await admin.ExecuteAsync($"GRANT ALL PRIVILEGES ON `{target}`.* TO 'fullnet'@'%'");
            if (interruptBackup) await AiBackupInterruptionAssertions.InterruptMySqlAsync(admin, root, source);
            await AiRecoveryBackupTransport.ExportMySqlAsync(root, source, backup, databases, media, default);
            await AiRecoveryBackupTransport.ImportMySqlAsync(root, target, backup, databases, media, default);
            targetConnection = new MySqlConnectionStringBuilder(sourceConnection) { Database = target, Pooling = false }.ConnectionString;
        }

        await using (var restored = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, targetConnection))
        {
            foreach (var (run, _) in runs)
            {
                Assert.AreEqual("reconciliation_required", await restored.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_ai_agent_run WHERE Id=@Id", new { Id = run }));
                Assert.AreEqual(1L, await restored.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_checkpoint WHERE RunId=@Id", new { Id = run }));
                Assert.AreEqual(1L, await restored.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_agent_event WHERE RunId=@Id", new { Id = run }));
                Assert.AreEqual(1L, await restored.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE Id=@Id AND UsageStatus='unknown'", new { Id = run }));
            }
        }
        if (native)
        {
            await NativeBackupRecoveryAssertions.VerifyAsync(provider, targetConnection, keys, files, runs);
            return;
        }
        // 使用恢复数据库与原 key ring 装配应用；不运行会重建模板的 InitializeAsync。
        using var restoredFactory = new FullNetApiFactory(provider, targetConnection, settings);
        await using var restoredScope = restoredFactory.Services.CreateAsyncScope();
        var restoredContext = restoredScope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        var reconciler = ActivatorUtilities.CreateInstance<AiAgentCompletedReceiptReconciliation>(restoredScope.ServiceProvider);
        foreach (var (run, tenantId) in runs)
        {
            if (tenantId is { } id) restoredContext.SetTenant(new(id, "a06-recovery", "备份恢复测试")); else restoredContext.SetHost();
            try
            {
                Assert.IsTrue(await reconciler.ReconcileAsync(run, default));
                Assert.IsFalse(await reconciler.ReconcileAsync(run, default), "同一原回执不得重复收敛。");
                var scopeKey = tenantId?.ToString("N") ?? "host";
                var operation = await restoredScope.ServiceProvider.GetRequiredService<IQueryExecutor>()
                    .QuerySingleOrDefaultAsync<AiOperationBudgetRecord>(AiOperationBudgetSql.Find, AiSqlParameters.Create(("OperationId", run), ("ScopeKey", scopeKey)));
                Assert.IsNotNull(operation);
                Assert.AreEqual("known", operation.UsageStatus);
                Assert.AreEqual(2L, operation.InputTokens);
                Assert.AreEqual(1L, operation.OutputTokens);
            }
            finally { restoredContext.Clear(); }
        }
    }
}

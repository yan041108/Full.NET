using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Jobs.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Jobs;

/// <summary>以独立 Worker 进程的正式后台循环验证发布默认数据库准入预算。</summary>
[TestClass]
[DoNotParallelize]
public sealed class JobsWorkerProcessAdmissionTests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Separate_worker_processes_isolate_pressure_and_resume_hosted_job_polling(DatabaseProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var token = timeout.Token;
        var database = provider == DatabaseProvider.SqlServer
            ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync()
            : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnectionStringBuilder(database) { MaxPoolSize = 10 }.ConnectionString
            : new MySqlConnectionStringBuilder(database) { MaximumPoolSize = 10 }.ConnectionString;
        var evidence = Directory.CreateTempSubdirectory("fullnet-worker-admission-process-");
        var passed = false;
        try
        {
            using var api = new FullNetApiFactory(provider, connection, new Dictionary<string, string?>
            {
                ["Files:Local:RootPath"] = Path.Combine(evidence.FullName, "files"),
                ["DataProtection:KeyRingPath"] = Path.Combine(evidence.FullName, "keys")
            });
            await api.InitializeAsync(token, useSchemaTemplate: false);
            await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            var originals = new[] { await SeedAsync(api, token), await SeedAsync(api, token) };
            var gate = Path.Combine(evidence.FullName, "release");
            var settings = new Dictionary<string, string?>
            {
                ["DatabaseCapacity:Enabled"] = "true", ["DatabaseCapacity:HostRole"] = "Worker",
                ["DatabaseCapacity:PermitLimit"] = "7", ["DatabaseCapacity:QueueLimit"] = "1",
                ["DatabaseCapacity:AcquireTimeoutMilliseconds"] = "1000", ["DatabaseCapacity:ExpectedMaxPoolSize"] = "10",
                ["DatabaseCapacity:HealthReserve"] = "2", ["DatabaseCapacity:CriticalWorkerReserve"] = "1",
                ["DatabaseCapacity:AuthorizationReadReserve"] = "0", ["DatabaseCapacity:ApiMaxReplicas"] = "12",
                ["DatabaseCapacity:ApiMaxPoolSize"] = "40", ["DatabaseCapacity:WorkerMaxReplicas"] = "8",
                ["DatabaseCapacity:WorkerMaxPoolSize"] = "10", ["DatabaseCapacity:MigrationReserve"] = "20",
                ["DatabaseCapacity:TotalBudget"] = "600",
                ["Notifications:Worker:PollMilliseconds"] = "1000", ["Workflow:RecoveryWorker:PollMilliseconds"] = "1000",
                ["OutboxWorker:PollMilliseconds"] = "1000"
            };
            await using var first = await EnterpriseRequestWorkerProcess.StartAsync(api, evidence.FullName, "admission-pressure",
                gate, 1, additionalSettings: settings);
            await WaitUntilAsync(() => Task.FromResult(File.Exists(gate + ".rejected") && File.Exists(gate + ".timeout")), token,
                "第一进程应观测到队列拒绝和等待超时。");
            await WaitUntilAsync(async () => await first.HasAdmissionFailureAsync("jobs.", token)
                && await first.HasAdmissionFailureAsync("workflow.", token), token, "Jobs 和 Workflow 循环应实际遇到准入压力。");
            Assert.AreEqual(2, await CountAsync(originals, JobExecutionStatuses.Pending, 0), "普通许可未释放时，第一进程应保留原积压。");

            await using (var second = await EnterpriseRequestWorkerProcess.StartAsync(api, evidence.FullName, "admission-observer",
                gate, 2, additionalSettings: settings))
            {
                Assert.AreNotEqual(first.Id, second.Id);
                await WaitUntilAsync(async () => await CountAsync(originals, JobExecutionStatuses.Succeeded, 1) == 2, token,
                    "第二进程应独立排空原积压。");
                Assert.IsFalse(File.Exists(gate + ".released"));
            }

            var resumed = await SeedAsync(api, token);
            await File.WriteAllTextAsync(gate, string.Empty, token);
            await WaitUntilAsync(async () => File.Exists(gate + ".released") && await CountAsync([resumed], JobExecutionStatuses.Succeeded, 1) == 1,
                token, "第一进程应在释放压力后恢复消费。");
            Assert.AreEqual(3, await CountAsync([.. originals, resumed], JobExecutionStatuses.Succeeded, 1));
            passed = true;

            Task<int> CountAsync(Guid[] ids, string status, int attempts) => db.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM fn_jobs_execution
                WHERE Id IN @Ids AND TenantId IS NULL AND Status=@Status AND AttemptCount=@Attempts
                  AND LeaseId IS NULL AND LeaseExpiresAtUtc IS NULL
                """, new { Ids = ids, Status = status, Attempts = attempts }, cancellationToken: token));
        }
        finally
        {
            if (passed && evidence.Parent?.FullName == Path.GetFullPath(Path.TrimEndingDirectorySeparator(Path.GetTempPath())))
                evidence.Delete(recursive: true);
        }
    }

    private static async Task<Guid> SeedAsync(FullNetApiFactory api, CancellationToken token)
    {
        var id = Guid.CreateVersion7();
        await JobsExecutionCancelAssertions.SeedDefinitionAndPendingExecutionAsync(api, Guid.CreateVersion7(), id, token);
        return id;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, CancellationToken token, string message)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (!await condition()) await Task.Delay(100, deadline.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Assert.Fail(message);
        }
    }
}

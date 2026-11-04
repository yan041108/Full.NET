using Dapper;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Data;

[TestClass]
public sealed class OwnedTestDatabasesTests
{
    [TestMethod]
    public async Task Private_container_retirement_replaces_database_drops()
    {
        var owner = new OwnedTestDatabases();
        var removed = 0;
        var dropped = 0;
        await owner.CreateAsync(_ => Task.CompletedTask, _ => { dropped++; return Task.CompletedTask; });
        await owner.RetireAsync(() => { removed++; return Task.CompletedTask; });
        await owner.CleanupAsync();
        Assert.AreEqual(1, removed);
        Assert.AreEqual(0, dropped);
    }

    [TestMethod]
    public async Task Retirement_rejects_creation_during_container_removal()
    {
        var owner = new OwnedTestDatabases();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retirement = owner.RetireAsync(async () => { entered.SetResult(); await release.Task; });
        var created = false;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var late = owner.CreateAsync(_ => { created = true; return Task.CompletedTask; }, _ => Task.CompletedTask);
            release.TrySetResult();
            await retirement;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => late);
            Assert.IsFalse(created);
        }
        finally { release.TrySetResult(); await retirement; }
    }

    [TestMethod]
    public async Task Retirement_waits_for_inflight_configuration_before_removing_container()
    {
        var owner = new OwnedTestDatabases();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configured = false;
        var removed = false;
        var creation = owner.CreateAsync(_ => Task.CompletedTask, _ => Task.CompletedTask,
            async _ => { entered.SetResult(); await release.Task; configured = true; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var retirement = owner.RetireAsync(() =>
        {
            Assert.IsTrue(configured);
            removed = true;
            return Task.CompletedTask;
        });
        try { Assert.IsFalse(retirement.IsCompleted); }
        finally { release.TrySetResult(); await creation; await retirement; }
        Assert.IsTrue(removed);
    }

    [TestMethod]
    public async Task Retirement_attempts_other_containers_and_preserves_ownership_on_failure()
    {
        var owner = new OwnedTestDatabases();
        var removed = 0;
        var dropped = 0;
        await owner.CreateAsync(_ => Task.CompletedTask, _ => { dropped++; return Task.CompletedTask; });
        var error = await Assert.ThrowsExactlyAsync<AggregateException>(() => owner.RetireAsync(
            () => throw new InvalidOperationException("container removal failed"),
            () => { removed++; return Task.CompletedTask; }));
        Assert.AreEqual(1, error.InnerExceptions.Count);
        Assert.AreEqual(1, removed);
        await owner.CleanupAsync();
        Assert.AreEqual(1, dropped);
    }

    [TestMethod]
    public async Task SqlServer_cleanup_drops_owned_databases_and_preserves_other_run()
    {
        var owner = new OwnedTestDatabases();
        var foreignOwner = new OwnedTestDatabases();
        try
        {
            var foreign = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(foreignOwner);
            var own = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync(owner);
            await using (var connection = new SqlConnection(own))
                await connection.ExecuteAsync("CREATE TABLE CleanupProbe (Value int); INSERT INTO CleanupProbe (Value) VALUES (7);");
            await using var admin = new SqlConnection(await SharedDatabaseFixture.GetSqlServerMasterConnectionStringAsync());
            var name = SharedDatabaseFixture.GetSqlServerDatabaseName(own);
            Assert.IsNotNull(await admin.ExecuteScalarAsync<int?>("SELECT DB_ID(@Name);", new { Name = name }));
            await owner.CleanupAsync();
            Assert.IsNull(await admin.ExecuteScalarAsync<int?>("SELECT DB_ID(@Name);", new { Name = name }));
            Assert.IsNotNull(await admin.ExecuteScalarAsync<int?>("SELECT DB_ID(@Name);",
                new { Name = SharedDatabaseFixture.GetSqlServerDatabaseName(foreign) }));
            await owner.CleanupAsync();
        }
        finally { await owner.CleanupAsync(); await foreignOwner.CleanupAsync(); }
    }

    [TestMethod]
    public async Task MySql_cleanup_drops_owned_database_and_grant_and_preserves_other_run()
    {
        var owner = new OwnedTestDatabases();
        var foreignOwner = new OwnedTestDatabases();
        try
        {
            var foreign = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(foreignOwner);
            var own = await SharedDatabaseFixture.CreateMySqlDatabaseAsync(owner);
            await using (var connection = new MySqlConnection(own))
                await connection.ExecuteAsync("CREATE TABLE CleanupProbe (Value int); INSERT INTO CleanupProbe (Value) VALUES (7);");
            await using var admin = new MySqlConnection(await SharedDatabaseFixture.GetMySqlRootConnectionStringAsync());
            var name = SharedDatabaseFixture.GetMySqlDatabaseName(own);
            const string exists = "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @Name;";
            const string grant = "SELECT COUNT(*) FROM mysql.db WHERE User = 'fullnet' AND Host = '%' AND Db = @Name;";
            Assert.AreEqual(1, await admin.ExecuteScalarAsync<int>(exists, new { Name = name }));
            Assert.AreEqual(1, await admin.ExecuteScalarAsync<int>(grant, new { Name = name }));
            await owner.CleanupAsync();
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>(exists, new { Name = name }));
            Assert.AreEqual(0, await admin.ExecuteScalarAsync<int>(grant, new { Name = name }));
            var foreignName = new { Name = SharedDatabaseFixture.GetMySqlDatabaseName(foreign) };
            Assert.AreEqual(1, await admin.ExecuteScalarAsync<int>(exists, foreignName));
            Assert.AreEqual(1, await admin.ExecuteScalarAsync<int>(grant, foreignName));
            await owner.CleanupAsync();
        }
        finally { await owner.CleanupAsync(); await foreignOwner.CleanupAsync(); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Failed_or_cancelled_configuration_keeps_created_database_owned(bool cancelled)
    {
        var owner = new OwnedTestDatabases();
        var created = string.Empty;
        var dropped = string.Empty;
        var error = cancelled ? (Exception)new OperationCanceledException() : new InvalidOperationException();
        var actual = await Assert.ThrowsAsync<Exception>(() => owner.CreateAsync(
            n => { created = n; return Task.CompletedTask; },
            n => { dropped = n; return Task.CompletedTask; }, _ => throw error));
        Assert.AreSame(error, actual);
        await owner.CleanupAsync();
        Assert.AreEqual(created, dropped);
        Assert.AreNotEqual(string.Empty, dropped);
    }

    [TestMethod]
    public async Task Cleanup_waits_until_database_configuration_finishes()
    {
        var owner = new OwnedTestDatabases();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = false;
        var creation = owner.CreateAsync(_ => Task.CompletedTask,
            _ => { dropped = true; return Task.CompletedTask; }, async _ => { entered.SetResult(); await release.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = owner.CleanupAsync();
        try { Assert.IsFalse(cleanup.IsCompleted); }
        finally { release.SetResult(); await creation; await cleanup; }
        Assert.IsTrue(dropped);
    }

    [TestMethod]
    public async Task Cleanup_removes_only_successfully_created_databases_and_is_idempotent()
    {
        var owner = new OwnedTestDatabases();
        var databases = new HashSet<string> { "fullnet_it_schema_sql", "fullnet_it_foreign" };
        var name = await owner.CreateAsync(n => { databases.Add(n); return Task.CompletedTask; },
            n => { Assert.IsTrue(databases.Remove(n)); return Task.CompletedTask; });
        Assert.IsTrue(name.StartsWith("fullnet_it_", StringComparison.Ordinal));
        Assert.IsTrue(Guid.TryParseExact(name[11..], "N", out _));
        await owner.CleanupAsync();
        await owner.CleanupAsync();
        CollectionAssert.AreEquivalent(new[] { "fullnet_it_schema_sql", "fullnet_it_foreign" }, databases.ToArray());
    }

    [TestMethod]
    public async Task Failed_creation_does_not_claim_an_existing_database()
    {
        var owner = new OwnedTestDatabases();
        var drops = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.CreateAsync(
            _ => throw new InvalidOperationException("create failed"), _ => { drops++; return Task.CompletedTask; }));
        await owner.CleanupAsync();
        Assert.AreEqual(0, drops);
    }

    [TestMethod]
    public async Task Cleanup_failure_does_not_skip_other_databases_and_retries_only_failed_entry()
    {
        var owner = new OwnedTestDatabases();
        var firstAttempts = 0;
        var secondAttempts = 0;
        await owner.CreateAsync(_ => Task.CompletedTask, _ =>
        {
            if (++firstAttempts == 1) throw new InvalidOperationException("drop failed");
            return Task.CompletedTask;
        });
        await owner.CreateAsync(_ => Task.CompletedTask, _ => { secondAttempts++; return Task.CompletedTask; });
        var failure = await Assert.ThrowsExactlyAsync<AggregateException>(owner.CleanupAsync);
        Assert.AreEqual(1, failure.InnerExceptions.Count);
        Assert.AreEqual(1, secondAttempts);
        await owner.CleanupAsync();
        Assert.AreEqual(2, firstAttempts);
        Assert.AreEqual(1, secondAttempts);
    }

    [TestMethod]
    public async Task Cleanup_closes_registration_without_invoking_late_creation()
    {
        var owner = new OwnedTestDatabases();
        await owner.CleanupAsync();
        var created = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.CreateAsync(
            _ => { created = true; return Task.CompletedTask; }, _ => Task.CompletedTask));
        Assert.IsFalse(created);
    }

    [TestMethod]
    public async Task Cleanup_waits_for_inflight_creation_and_keeps_its_ownership()
    {
        var owner = new OwnedTestDatabases();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dropped = false;
        var creation = owner.CreateAsync(async _ => { entered.SetResult(); await release.Task; },
            _ => { dropped = true; return Task.CompletedTask; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = owner.CleanupAsync();
        try { Assert.IsFalse(cleanup.IsCompleted); }
        finally { release.SetResult(); await creation; await cleanup; }
        Assert.IsTrue(dropped);
    }

    [TestMethod]
    public async Task Parallel_cleanup_executes_each_drop_once()
    {
        var owner = new OwnedTestDatabases();
        var attempts = 0;
        await owner.CreateAsync(_ => Task.CompletedTask, async _ => { Interlocked.Increment(ref attempts); await Task.Yield(); });
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => owner.CleanupAsync()));
        Assert.AreEqual(1, attempts);
    }

    [TestMethod]
    public async Task Owners_keep_provider_and_server_cleanup_actions_separate()
    {
        var first = new OwnedTestDatabases();
        var second = new OwnedTestDatabases();
        var deleted = new List<string>();
        var sql = await first.CreateAsync(_ => Task.CompletedTask, n => { deleted.Add("sql:" + n); return Task.CompletedTask; });
        var mysql = await first.CreateAsync(_ => Task.CompletedTask, n => { deleted.Add("mysql:" + n); return Task.CompletedTask; });
        await second.CreateAsync(_ => Task.CompletedTask, _ => throw new InvalidOperationException("foreign owner"));
        await first.CleanupAsync();
        CollectionAssert.AreEquivalent(new[] { "sql:" + sql, "mysql:" + mysql }, deleted);
    }
}

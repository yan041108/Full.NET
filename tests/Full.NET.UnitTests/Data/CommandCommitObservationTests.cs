using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Data;

/// <summary>观测不进入业务可靠投递；共享命令事务只在最外层成功提交后发布。</summary>
[TestClass]
public sealed class CommandCommitObservationTests
{
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Nested_observations_wait_for_outer_commit_in_registration_order(bool resultTransaction)
    {
        var session = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(session);
        var observed = new List<int>();
        async Task<bool> Action(CancellationToken token)
        {
            transaction.Observe(() => { Assert.IsFalse(session.HasTransaction); observed.Add(1); });
            await transaction.ExecuteAsync(_ =>
            {
                transaction.Observe(() => observed.Add(2));
                return Task.FromResult(true);
            }, token);
            Assert.HasCount(0, observed);
            return true;
        }
        if (resultTransaction) await transaction.ExecuteResultAsync(async t => Result<bool>.Success(await Action(t)), CancellationToken.None);
        else await transaction.ExecuteAsync(Action, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 1, 2 }, observed);
        Assert.AreEqual(1, session.CommitCount);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Rollback_discards_observations_and_next_transaction_has_no_stale_callbacks(bool failureResult)
    {
        var session = new RecordingDbTransactionCoordinator();
        var transaction = new DapperCommandTransaction(session);
        var calls = 0;
        if (failureResult)
            await transaction.ExecuteResultAsync(_ =>
            {
                transaction.Observe(() => calls++);
                return Task.FromResult(Result<bool>.Failure(new Error("test.failure", "Failure.", ErrorType.Validation)));
            }, CancellationToken.None);
        else
            await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync<bool>(_ =>
            {
                transaction.Observe(() => calls++);
                throw new IOException("rollback");
            }, CancellationToken.None));
        await transaction.ExecuteAsync(_ => Task.FromResult(true), CancellationToken.None);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(1, session.RollbackCount);
        Assert.AreEqual(1, session.CommitCount);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Commit_failure_discards_observation(bool resultTransaction)
    {
        var session = new FailingCommit(); var transaction = new DapperCommandTransaction(session); var calls = 0;
        Task<bool> Action(CancellationToken _)
        {
            transaction.Observe(() => calls++);
            return Task.FromResult(true);
        }
        if (resultTransaction)
            await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteResultAsync(async t => Result<bool>.Success(await Action(t)), CancellationToken.None));
        else await Assert.ThrowsAsync<IOException>(() => transaction.ExecuteAsync(Action, CancellationToken.None));
        Assert.AreEqual(0, calls);
        Assert.AreEqual(1, session.RollbackCount);
    }

    [TestMethod]
    public async Task Cancellation_discards_observation()
    {
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator()); var calls = 0;
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => transaction.ExecuteAsync<bool>(_ =>
        {
            transaction.Observe(() => calls++);
            cts.Cancel();
            return Task.FromCanceled<bool>(cts.Token);
        }, cts.Token));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Ignored_cancellation_cannot_commit_or_publish_observation(bool resultTransaction)
    {
        var session = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(session); var calls = 0;
        using var cts = new CancellationTokenSource();
        Task<bool> Action(CancellationToken _)
        {
            transaction.Observe(() => calls++);
            cts.Cancel();
            return Task.FromResult(true);
        }
        if (resultTransaction)
            await Assert.ThrowsAsync<OperationCanceledException>(() => transaction.ExecuteResultAsync(async t => Result<bool>.Success(await Action(t)), cts.Token));
        else await Assert.ThrowsAsync<OperationCanceledException>(() => transaction.ExecuteAsync(Action, cts.Token));
        Assert.AreEqual(0, calls);
        Assert.AreEqual(0, session.CommitCount);
        Assert.AreEqual(1, session.RollbackCount);
    }

    [TestMethod]
    public async Task Throwing_observer_does_not_rollback_or_prevent_later_observers()
    {
        var session = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(session); var calls = 0;
        await transaction.ExecuteAsync(_ =>
        {
            transaction.Observe(() => throw new IOException("observer failed"));
            transaction.Observe(() => calls++);
            return Task.FromResult(true);
        }, CancellationToken.None);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(1, session.CommitCount);
        Assert.AreEqual(0, session.RollbackCount);
    }

    [TestMethod]
    public async Task Observer_reentry_starts_a_new_transaction_without_replaying_old_callbacks()
    {
        var session = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(session); var calls = new List<int>();
        await transaction.ExecuteAsync(_ =>
        {
            transaction.Observe(() =>
            {
                calls.Add(1);
                transaction.ExecuteAsync(_ =>
                {
                    transaction.Observe(() => calls.Add(3));
                    return Task.FromResult(true);
                }, CancellationToken.None).GetAwaiter().GetResult();
            });
            transaction.Observe(() => calls.Add(2));
            return Task.FromResult(true);
        }, CancellationToken.None);
        await transaction.ExecuteAsync(_ => Task.FromResult(true), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { 1, 3, 2 }, calls);
        Assert.AreEqual(3, session.CommitCount);
    }

    [TestMethod]
    public void Registration_without_owned_transaction_is_rejected()
    {
        var transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        Assert.Throws<InvalidOperationException>(() => transaction.Observe(() => { }));
        Assert.Throws<ArgumentNullException>(() => transaction.Observe(null!));
    }

    [TestMethod]
    public async Task Externally_started_transaction_cannot_leak_observation_to_later_commit()
    {
        var session = new RecordingDbTransactionCoordinator(); var transaction = new DapperCommandTransaction(session);
        await session.BeginAsync(CancellationToken.None);
        await transaction.ExecuteAsync(_ =>
        {
            Assert.Throws<InvalidOperationException>(() => transaction.Observe(() => { }));
            return Task.FromResult(true);
        }, CancellationToken.None);
        await session.RollbackAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task Dependency_injection_shares_transaction_and_observer_with_scope_isolation()
    {
        var services = new ServiceCollection();
        services.AddFullNetDapper(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Provider"] = "SqlServer", ["Database:ConnectionString"] = "Server=localhost;Database=test;Integrated Security=true" }).Build(), "Development");
        await using var provider = services.BuildServiceProvider();
        await using var first = provider.CreateAsyncScope(); await using var second = provider.CreateAsyncScope();
        Assert.AreSame<object>(first.ServiceProvider.GetRequiredService<ICommandTransaction>(), first.ServiceProvider.GetRequiredService<ICommandCommitObserver>());
        Assert.AreNotSame(first.ServiceProvider.GetRequiredService<ICommandCommitObserver>(), second.ServiceProvider.GetRequiredService<ICommandCommitObserver>());
    }

    private sealed class FailingCommit : RecordingDbTransactionCoordinator
    {
        public override Task CommitAsync(CancellationToken token) => Task.FromException(new IOException("commit failed"));
    }
}

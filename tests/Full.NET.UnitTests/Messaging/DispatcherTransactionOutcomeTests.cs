using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Dapper;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Messaging;

/// <summary>验证分发器到持久化协调器的真实失败提交策略。</summary>
[TestClass]
public sealed class DispatcherTransactionOutcomeTests
{
    [TestMethod]
    [DataRow("business.failure", false, false)]
    [DataRow("security.rejected", true, true)]
    [DataRow("late.failure", true, false)]
    public async Task Rejected_command_commits_only_explicit_security_outcomes(string code, bool securityCommand, bool commits)
    {
        var session = new RecordingDbTransactionCoordinator();
        await using var provider = new ServiceCollection()
            .AddSingleton<ICommandHandler<BusinessCommand, bool>>(new BusinessHandler(code))
            .AddSingleton<ICommandHandler<SecurityCommand, bool>>(new SecurityHandler(code))
            .BuildServiceProvider();
        var dispatcher = new CommandDispatcher(provider, new DapperCommandTransaction(session));
        var result = securityCommand
            ? await dispatcher.SendAsync<SecurityCommand, bool>(new SecurityCommand())
            : await dispatcher.SendAsync<BusinessCommand, bool>(new BusinessCommand());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(code, result.Error!.Code);
        Assert.AreEqual(commits ? 1 : 0, session.CommitCount);
        Assert.AreEqual(commits ? 0 : 1, session.RollbackCount);
    }

    [TestMethod]
    public async Task Swallowed_nested_failure_cannot_commit_through_another_coordinator()
    {
        var session = new RecordingDbTransactionCoordinator();
        ICommandTransaction outer = new DapperCommandTransaction(session);
        ICommandTransaction inner = new DapperCommandTransaction(session);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => outer.ExecuteAsync(async token =>
        {
            await inner.ExecuteResultAsync(_ => Task.FromResult(Failure("nested.failure")), token);
            return true;
        }, CancellationToken.None));
        Assert.AreEqual(0, session.CommitCount);
        Assert.AreEqual(1, session.RollbackCount);
        var next = await outer.ExecuteResultAsync(_ => Task.FromResult(Result<bool>.Success(true)), CancellationToken.None);
        Assert.IsTrue(next.IsSuccess);
        Assert.AreEqual(1, session.CommitCount);
    }

    [TestMethod]
    public async Task Default_result_adapter_uses_exception_rollback_before_returning_failure()
    {
        var adapter = new DefaultAdapter();
        var result = await ((ICommandTransaction)adapter).ExecuteResultAsync(
            _ => Task.FromResult(Failure("business.failure")), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(adapter.RolledBack);
        Assert.IsFalse(adapter.Committed);
    }

    private static Result<bool> Failure(string code) => Result<bool>.Failure(new Error(code, code, ErrorType.Conflict));

    [TestMethod]
    public async Task Default_adapter_nested_rejection_cannot_be_swallowed_by_owner()
    {
        var session = new RecordingDbTransactionCoordinator();
        ICommandTransaction owner = new DapperCommandTransaction(session);
        ICommandTransaction adapter = new DelegatingAdapter(owner);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            var rejected = await adapter.ExecuteResultAsync(_ => Task.FromResult(Failure("nested.failure")), token);
            Assert.IsFalse(rejected.IsSuccess);
            return true;
        }, CancellationToken.None));
        Assert.AreEqual(0, session.CommitCount);
        Assert.AreEqual(1, session.RollbackCount);
        Assert.IsTrue((await owner.ExecuteResultAsync(_ => Task.FromResult(Result<bool>.Success(true)), CancellationToken.None)).IsSuccess);
    }

    private sealed class DelegatingAdapter(ICommandTransaction inner) : ICommandTransaction
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) =>
            inner.ExecuteAsync(action, cancellationToken);
    }
    private sealed record BusinessCommand : ITransactionalCommand<bool>;
    private sealed record SecurityCommand : ITransactionalCommand<bool>, ITransactionalRejectionPolicy
    {
        public bool ShouldCommitRejection(Error error) => error.Code == "security.rejected";
    }
    private sealed class BusinessHandler(string code) : ICommandHandler<BusinessCommand, bool>
    {
        public Task<Result<bool>> HandleAsync(BusinessCommand command, CancellationToken cancellationToken) => Task.FromResult(Failure(code));
    }
    private sealed class SecurityHandler(string code) : ICommandHandler<SecurityCommand, bool>
    {
        public Task<Result<bool>> HandleAsync(SecurityCommand command, CancellationToken cancellationToken) => Task.FromResult(Failure(code));
    }
    private sealed class DefaultAdapter : ICommandTransaction
    {
        public bool RolledBack { get; private set; }
        public bool Committed { get; private set; }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            try { var result = await action(cancellationToken); Committed = true; return result; }
            catch { RolledBack = true; throw; }
        }
    }
}

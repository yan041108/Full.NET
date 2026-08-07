using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Dapper;

namespace Full.NET.UnitTests.Data;

[TestClass]
public sealed class CommandTransactionResultTests
{
    private static readonly Error SampleError = new(
        "test.failure",
        "Failure.",
        ErrorType.Validation);

    [TestMethod]
    public async Task ExecuteResultAsync_commits_when_result_is_success()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction transaction = new DapperCommandTransaction(coordinator);

        var result = await transaction.ExecuteResultAsync(
            _ => Task.FromResult(Result<string>.Success("ok")),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("ok", result.Value);
        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(1, coordinator.CommitCount);
        Assert.AreEqual(0, coordinator.RollbackCount);
        Assert.IsFalse(coordinator.HasTransaction);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_rolls_back_when_result_is_failure()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction transaction = new DapperCommandTransaction(coordinator);

        var result = await transaction.ExecuteResultAsync(
            _ => Task.FromResult(Result<string>.Failure(SampleError)),
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(SampleError.Code, result.Error!.Code);
        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
        Assert.IsFalse(coordinator.HasTransaction);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_rolls_back_when_action_throws()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction transaction = new DapperCommandTransaction(coordinator);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            transaction.ExecuteResultAsync<string>(
                _ => throw new InvalidOperationException("boom"),
                CancellationToken.None));

        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
        Assert.IsFalse(coordinator.HasTransaction);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_rolls_back_when_action_is_canceled()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction transaction = new DapperCommandTransaction(coordinator);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            transaction.ExecuteResultAsync<string>(
                token => Task.FromCanceled<Result<string>>(token),
                cts.Token));

        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_participates_in_existing_transaction_without_commit()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction transaction = new DapperCommandTransaction(coordinator);
        await coordinator.BeginAsync(CancellationToken.None);

        var result = await transaction.ExecuteResultAsync(
            _ => Task.FromResult(Result<int>.Success(7)),
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(0, coordinator.RollbackCount);
        Assert.IsTrue(coordinator.HasTransaction);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_rolls_back_outer_when_nested_result_fails()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction outer = new DapperCommandTransaction(coordinator);
        ICommandTransaction inner = new DapperCommandTransaction(coordinator);

        var result = await outer.ExecuteResultAsync(
            async _ =>
            {
                var innerResult = await inner.ExecuteResultAsync(
                    __ => Task.FromResult(Result<string>.Failure(SampleError)),
                    CancellationToken.None).ConfigureAwait(false);
                return innerResult.IsSuccess
                    ? Result<string>.Success(innerResult.Value!)
                    : Result<string>.Failure(innerResult.Error!);
            },
            CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(0, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_commits_outer_when_nested_result_succeeds()
    {
        var coordinator = new RecordingDbTransactionCoordinator();
        ICommandTransaction outer = new DapperCommandTransaction(coordinator);
        ICommandTransaction inner = new DapperCommandTransaction(coordinator);

        var result = await outer.ExecuteResultAsync(
            async _ =>
            {
                var innerResult = await inner.ExecuteResultAsync(
                    __ => Task.FromResult(Result<string>.Success("nested")),
                    CancellationToken.None).ConfigureAwait(false);
                return innerResult.IsSuccess
                    ? Result<string>.Success(innerResult.Value!)
                    : Result<string>.Failure(innerResult.Error!);
            },
            CancellationToken.None);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("nested", result.Value);
        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(1, coordinator.CommitCount);
        Assert.AreEqual(0, coordinator.RollbackCount);
    }

    [TestMethod]
    public async Task ExecuteResultAsync_rolls_back_when_commit_fails()
    {
        var coordinator = new FailingCommitCoordinator();
        ICommandTransaction transaction = new DapperCommandTransaction(coordinator);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            transaction.ExecuteResultAsync(
                _ => Task.FromResult(Result<string>.Success("ok")),
                CancellationToken.None));

        Assert.AreEqual(1, coordinator.BeginCount);
        Assert.AreEqual(1, coordinator.CommitCount);
        Assert.AreEqual(1, coordinator.RollbackCount);
        Assert.IsFalse(coordinator.HasTransaction);
    }

    private sealed class FailingCommitCoordinator : RecordingDbTransactionCoordinator
    {
        public override Task CommitAsync(CancellationToken cancellationToken)
        {
            if (!HasTransaction)
            {
                throw new InvalidOperationException("No database transaction is active.");
            }

            CommitCount++;
            throw new InvalidOperationException("commit failed");
        }
    }
}
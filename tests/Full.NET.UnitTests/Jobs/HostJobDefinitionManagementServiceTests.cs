using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Jobs.Features.ManageHostJobDefinitions;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HostJobDefinitionManagementServiceTests
{
    [TestMethod]
    public async Task DeleteAsync_UsesResultAwareTransaction()
    {
        var expected = Result<bool>.Failure(new Error(
            "jobs.test.rollback",
            "rollback",
            ErrorType.Conflict));
        var transaction = new ResultAwareTransaction(expected);
        var service = new HostJobDefinitionManagementService(
            null!,
            null!,
            transaction,
            null!,
            null!,
            null!,
            null!);

        var actual = await service.DeleteAsync(
            Guid.CreateVersion7(),
            version: 1,
            CancellationToken.None);

        Assert.AreSame(expected, actual);
        Assert.AreEqual(1, transaction.ResultExecutionCount);
    }

    private sealed class ResultAwareTransaction(Result<bool> result)
        : ICommandTransaction
    {
        public int ResultExecutionCount { get; private set; }

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken) =>
            throw new AssertFailedException(
                "返回 Result 的写操作不得使用会提交失败结果的 ExecuteAsync。");

        public Task<Result<T>> ExecuteResultAsync<T>(
            Func<CancellationToken, Task<Result<T>>> action,
            CancellationToken cancellationToken)
        {
            ResultExecutionCount++;
            return Task.FromResult((Result<T>)(object)result);
        }
    }
}

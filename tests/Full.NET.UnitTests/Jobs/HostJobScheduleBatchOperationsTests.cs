using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Features.ManageHostJobSchedules;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HostJobScheduleBatchOperationsTests
{
    [TestMethod]
    public async Task BatchPauseAsync_RejectsEmptyRequest()
    {
        var service = CreateService();

        var result = await service.BatchPauseAsync(
            Guid.CreateVersion7(),
            new BatchChangeHostJobScheduleStateRequest([]));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(JobsErrorCodes.ScheduleInvalidBatch, result.Error?.Code);
    }

    [TestMethod]
    public async Task BatchPauseAsync_RejectsOversizedRequest()
    {
        var service = CreateService();
        var items = Enumerable.Range(0, HostJobScheduleBatchLimits.MaxStateChangeCount + 1)
            .Select(_ => new BatchChangeHostJobScheduleStateItem(
                Guid.CreateVersion7(),
                1))
            .ToArray();

        var result = await service.BatchPauseAsync(
            Guid.CreateVersion7(),
            new BatchChangeHostJobScheduleStateRequest(items));

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(JobsErrorCodes.ScheduleInvalidBatch, result.Error?.Code);
    }

    private static HostJobScheduleService CreateService() =>
        new(
            new EmptyQueryExecutor(),
            new EmptyCommandExecutor(),
            new EmptyTransaction(),
            new FixedClock(DateTimeOffset.UtcNow),
            new FixedIdGenerator(Guid.CreateVersion7()),
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

    private sealed class EmptyQueryExecutor : IQueryExecutor
    {
        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(default);
    }

    private sealed class EmptyCommandExecutor : ICommandExecutor
    {
        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class EmptyTransaction : ICommandTransaction
    {
        public Task ExecuteAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);

        public Task<Result<T>> ExecuteResultAsync<T>(
            Func<CancellationToken, Task<Result<T>>> action,
            CancellationToken cancellationToken = default) =>
            action(cancellationToken);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class FixedIdGenerator(Guid id) : IIdGenerator
    {
        public Guid NewId() => id;
    }
}

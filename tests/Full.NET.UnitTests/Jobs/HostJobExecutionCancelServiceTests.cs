using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Features.ManageHostJobExecutions;
using Full.NET.Modules.Jobs.Persistence;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HostJobExecutionCancelServiceTests
{
    [TestMethod]
    public async Task CancelAsync_PendingExecution_MarksCancelledImmediately()
    {
        var executionId = Guid.CreateVersion7();
        var definitionId = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero);
        var query = new RecordingJobsQueryExecutor(
            new JobExecutionRecord
            {
                Id = executionId,
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Pending,
                TriggerKind = JobTriggerKinds.Manual,
                CreatedAtUtc = now,
            },
            new JobExecutionRecord
            {
                Id = executionId,
                JobDefinitionId = definitionId,
                Status = JobExecutionStatuses.Cancelled,
                TriggerKind = JobTriggerKinds.Manual,
                FinishedAtUtc = now,
                CreatedAtUtc = now,
            });
        var command = new RecordingJobsCommandExecutor(
            JobSql.CancelPendingExecution.Name,
            1);
        var service = new HostJobExecutionCancelService(
            query,
            command,
            new FixedClock(now),
            new HostJobExecutionQueryService(
                query,
                command,
                new RecordingTransaction(),
                Microsoft.Extensions.Options.Options.Create(
                    new DatabaseOptions { Provider = DatabaseProvider.SqlServer })));

        var result = await service.CancelAsync(executionId);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(JobExecutionStatuses.Cancelled, result.Value!.Status);
    }

    [TestMethod]
    public async Task CancelAsync_SucceededExecution_ReturnsConflict()
    {
        var executionId = Guid.CreateVersion7();
        var service = new HostJobExecutionCancelService(
            new RecordingJobsQueryExecutor(
                new JobExecutionRecord
                {
                    Id = executionId,
                    JobDefinitionId = Guid.CreateVersion7(),
                    Status = JobExecutionStatuses.Succeeded,
                    TriggerKind = JobTriggerKinds.Manual,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }),
            new RecordingJobsCommandExecutor(),
            new FixedClock(DateTimeOffset.UtcNow),
            new HostJobExecutionQueryService(
                new RecordingJobsQueryExecutor(),
                new RecordingJobsCommandExecutor(),
                new RecordingTransaction(),
                Microsoft.Extensions.Options.Options.Create(
                    new DatabaseOptions { Provider = DatabaseProvider.SqlServer })));

        var result = await service.CancelAsync(executionId);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(JobsErrorCodes.ExecutionNotCancellable, result.Error!.Code);
        Assert.AreEqual(ErrorType.Conflict, result.Error.Type);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class RecordingJobsQueryExecutor : IQueryExecutor
    {
        private readonly Queue<JobExecutionRecord> _records;

        public RecordingJobsQueryExecutor(params JobExecutionRecord[] records) =>
            _records = new Queue<JobExecutionRecord>(records);

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(JobExecutionRecord) && _records.Count > 0)
            {
                return Task.FromResult<IReadOnlyList<T>>(
                    new List<T> { (T)(object)_records.Dequeue() });
            }

            if (typeof(T) == typeof(long))
            {
                return Task.FromResult<IReadOnlyList<T>>(new List<T> { (T)(object)0L });
            }

            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (typeof(T) == typeof(JobExecutionRecord) && _records.Count > 0)
            {
                return Task.FromResult<T?>((T)(object)_records.Dequeue());
            }

            if (typeof(T) == typeof(long))
            {
                return Task.FromResult<T?>((T)(object)0L);
            }

            return Task.FromResult<T?>(default);
        }
    }

    private sealed class RecordingJobsCommandExecutor : ICommandExecutor
    {
        private readonly string? _expectedStatementName;
        private readonly int _rows;

        public RecordingJobsCommandExecutor(
            string? expectedStatementName = null,
            int rows = 0)
        {
            _expectedStatementName = expectedStatementName;
            _rows = rows;
        }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            if (_expectedStatementName is not null)
            {
                Assert.AreEqual(_expectedStatementName, statement.Name);
            }

            return Task.FromResult(_rows);
        }
    }

    private sealed class RecordingTransaction : ICommandTransaction
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
}

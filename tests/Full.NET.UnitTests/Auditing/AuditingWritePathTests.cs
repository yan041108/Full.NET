using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Auditing;

[TestClass]
public sealed class AuditingWritePathTests
{
    [TestMethod]
    public void Request_buffer_rejects_duplicate_categories()
    {
        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateAccessModel());

        Assert.ThrowsExactly<InvalidOperationException>(
            () => buffer.Capture(CreateAccessModel()));
        Assert.AreEqual(1, buffer.Snapshot().Count);
    }

    [TestMethod]
    public async Task Access_transitional_path_writes_single_insert()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);

        var succeeded = await writer.TryWriteAccessAsync(
            CreateAccessModel(),
            CancellationToken.None);

        Assert.IsTrue(succeeded);
        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual(1, executor.ExecutionCount);
        Assert.AreEqual(
            "auditing.insert_request_audit_batch.access",
            executor.Statement!.Name);
    }

    [TestMethod]
    public async Task Microbatch_operations_use_one_multirow_insert_per_table()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);
        var envelopes = new[]
        {
            AuditWriteEnvelope.ForOperation(CreateOperationModel("op-a")),
            AuditWriteEnvelope.ForOperation(CreateOperationModel("op-b")),
            AuditWriteEnvelope.ForException(CreateExceptionModel()),
        };

        await writer.WriteMicroBatchAsync(envelopes, CancellationToken.None);

        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual(2, executor.ExecutionCount);
        Assert.AreEqual("auditing.microbatch.insert_operation_log", executor.Statements[0].Name);
        Assert.AreEqual("auditing.microbatch.insert_exception_log", executor.Statements[1].Name);
        Assert.AreEqual(
            1,
            executor.Statements[0].Text.Split("INSERT INTO", StringSplitOptions.None).Length - 1);
        Assert.IsTrue(envelopes.All(item => item.Completion.Task.IsCompletedSuccessfully
            && item.Completion.Task.Result.Succeeded));
    }

    [TestMethod]
    public async Task Microbatch_failure_fail_opens_without_escaping()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor
        {
            Exception = new InvalidOperationException("database unavailable"),
        };
        var writer = CreateWriter(transaction, executor);
        var envelope = AuditWriteEnvelope.ForOperation(CreateOperationModel("op-fail"));

        await writer.WriteMicroBatchAsync([envelope], CancellationToken.None);

        var result = await envelope.Completion.Task;
        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Poisoned);
        Assert.IsTrue(transaction.ExecutionCount >= 1);
    }

    [TestMethod]
    public async Task Poison_binary_split_commits_healthy_rows()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor
        {
            PoisonToken = "POISON",
            Exception = new InvalidOperationException("constraint"),
        };
        var writer = CreateWriter(transaction, executor);
        var healthy = AuditWriteEnvelope.ForOperation(CreateOperationModel("healthy"));
        var poison = AuditWriteEnvelope.ForOperation(CreateOperationModel("POISON"));

        await writer.WriteMicroBatchAsync([healthy, poison], CancellationToken.None);

        Assert.IsTrue((await healthy.Completion.Task).Succeeded);
        var poisonResult = await poison.Completion.Task;
        Assert.IsFalse(poisonResult.Succeeded);
        Assert.IsTrue(poisonResult.Poisoned);
        Assert.IsGreaterThanOrEqualTo(1, executor.CommittedIdCount);
    }

    [TestMethod]
    public async Task Coordinator_flushes_on_max_batch_rows()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 64,
                MaxBatchRows = 2,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromSeconds(30),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        var flush = Task.WhenAll(
            harness.Coordinator.FlushImportantAsync(
                CreateOperationModel("row-1"),
                null,
                CancellationToken.None),
            harness.Coordinator.FlushImportantAsync(
                CreateOperationModel("row-2"),
                null,
                CancellationToken.None));

        await flush.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsGreaterThanOrEqualTo(2, harness.Executor.CommittedIdCount);
        Assert.IsGreaterThanOrEqualTo(1, harness.Transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Coordinator_flushes_on_max_batch_delay()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 64,
                MaxBatchRows = 64,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        await harness.Coordinator.FlushImportantAsync(
                CreateOperationModel("delay-row"),
                null,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(1, harness.Executor.CommittedIdCount);
    }

    [TestMethod]
    public async Task Queue_full_fail_opens_without_outbox()
    {
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 1,
                MaxBatchRows = 1,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromHours(1),
                EnqueueTimeout = TimeSpan.FromMilliseconds(50),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(1),
            },
            flushGate);

        // 第一条进入 flush 并阻塞；第二条占满 Channel；第三条入队超时 fail-open。
        var first = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("held-1"),
            null,
            CancellationToken.None);
        await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1, TimeSpan.FromSeconds(2));
        var second = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("held-2"),
            null,
            CancellationToken.None);
        await Task.Delay(40);
        await harness.Coordinator.FlushImportantAsync(
                CreateOperationModel("rejected"),
                null,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(first.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        flushGate.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - started > timeout)
            {
                throw new TimeoutException("Condition was not met before timeout.");
            }

            await Task.Delay(10);
        }
    }

    [TestMethod]
    public async Task Coordinator_ignores_request_abort_when_flushing_audit()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 16,
                MaxBatchRows = 8,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        var buffer = new AuditWriteBuffer();
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = aborted.Token,
        };
        var middleware = new AuditWriteCoordinatorMiddleware(_ =>
        {
            buffer.Capture(CreateAccessModel());
            buffer.Capture(CreateOperationModel("abort-safe"));
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
                context,
                buffer,
                harness.Writer,
                harness.Coordinator)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsGreaterThanOrEqualTo(2, harness.Executor.ExecutionCount);
        Assert.IsFalse(harness.Executor.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Access_does_not_enter_b1_channel()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                MaxBatchRows = 8,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(1),
            });

        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateAccessModel());
        var middleware = new AuditWriteCoordinatorMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            new DefaultHttpContext(),
            buffer,
            harness.Writer,
            harness.Coordinator);

        Assert.AreEqual(1, harness.Executor.ExecutionCount);
        Assert.AreEqual(
            "auditing.insert_request_audit_batch.access",
            harness.Executor.Statement!.Name);
    }

    private static AuditWriteBatchWriter CreateWriter(
        ICommandTransaction transaction,
        ICommandExecutor executor) =>
        new(
            transaction,
            executor,
            new FixedClock(),
            new SequenceIdGenerator(),
            NullLogger<AuditWriteBatchWriter>.Instance);

    private static AccessLogWriteModel CreateAccessModel() =>
        new(
            "GET",
            "/api/v1/platform/host-dashboard-summary",
            StatusCodes.Status200OK,
            10,
            Guid.CreateVersion7(),
            null,
            "trace",
            "fingerprint",
            true);

    private static OperationLogWriteModel CreateOperationModel(string actionKey) =>
        new(
            actionKey,
            "PUT",
            "/api/v1/tenancy/tenants/1",
            StatusCodes.Status200OK,
            20,
            true,
            Guid.CreateVersion7(),
            null,
            "trace",
            "fingerprint",
            "tenancy.tenants.update");

    private static ExceptionLogWriteModel CreateExceptionModel() =>
        new(
            "System.InvalidOperationException",
            "Unhandled application exception.",
            null,
            "POST",
            "/api/v1/auditing/exception-probes",
            Guid.CreateVersion7(),
            null,
            "trace",
            "fingerprint");

    private sealed class MicroBatchHarness : IAsyncDisposable
    {
        private MicroBatchHarness(
            ServiceProvider provider,
            AuditMicroBatchCoordinator coordinator,
            AuditWriteBatchWriter writer,
            RecordingCommandTransaction transaction,
            RecordingCommandExecutor executor)
        {
            Provider = provider;
            Coordinator = coordinator;
            Writer = writer;
            Transaction = transaction;
            Executor = executor;
        }

        private ServiceProvider Provider { get; }

        public AuditMicroBatchCoordinator Coordinator { get; }

        public AuditWriteBatchWriter Writer { get; }

        public RecordingCommandTransaction Transaction { get; }

        public RecordingCommandExecutor Executor { get; }

        public static Task<MicroBatchHarness> CreateAsync(AuditMicroBatchOptions options) =>
            CreateAsync(options, flushGate: null);

        public static async Task<MicroBatchHarness> CreateAsync(
            AuditMicroBatchOptions options,
            TaskCompletionSource? flushGate)
        {
            var transaction = new RecordingCommandTransaction(flushGate);
            var executor = new RecordingCommandExecutor();
            var writer = CreateWriter(transaction, executor);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IOptionsMonitor<AuditMicroBatchOptions>>(
                new StaticOptionsMonitor(options));
            services.AddSingleton<IClock, FixedClock>();
            services.AddSingleton<IIdGenerator, SequenceIdGenerator>();
            services.AddSingleton<ICommandTransaction>(transaction);
            services.AddSingleton<ICommandExecutor>(executor);
            services.AddScoped(_ => writer);

            var provider = services.BuildServiceProvider();
            var coordinator = ActivatorUtilities.CreateInstance<AuditMicroBatchCoordinator>(provider);
            await coordinator.StartAsync(CancellationToken.None);
            return new MicroBatchHarness(provider, coordinator, writer, transaction, executor);
        }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private sealed class StaticOptionsMonitor(AuditMicroBatchOptions current)
        : IOptionsMonitor<AuditMicroBatchOptions>
    {
        public AuditMicroBatchOptions CurrentValue { get; } = current;

        public AuditMicroBatchOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<AuditMicroBatchOptions, string?> listener) => null;
    }

    private sealed class RecordingCommandTransaction(TaskCompletionSource? flushGate = null)
        : ICommandTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            if (flushGate is not null)
            {
                await flushGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await action(cancellationToken);
        }
    }

    private sealed class RecordingCommandExecutor : ICommandExecutor
    {
        public int ExecutionCount { get; private set; }

        public int CommittedIdCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public List<SqlStatement> Statements { get; } = [];

        public CancellationToken CancellationToken { get; private set; }

        public Exception? Exception { get; init; }

        public string? PoisonToken { get; init; }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            Statement = statement;
            Statements.Add(statement);
            CancellationToken = cancellationToken;
            var dict = parameters as Dictionary<string, object?> ?? [];
            if (PoisonToken is not null
                && Exception is not null
                && dict.Values.Any(value =>
                    value is string text
                    && text.Contains(PoisonToken, StringComparison.Ordinal)))
            {
                return Task.FromException<int>(Exception);
            }

            if (Exception is not null && PoisonToken is null)
            {
                return Task.FromException<int>(Exception);
            }

            var rows = CountRows(dict, statement);
            CommittedIdCount += rows;
            return Task.FromResult(rows);
        }

        private static int CountRows(Dictionary<string, object?> dict, SqlStatement statement)
        {
            var idCount = dict.Keys.Count(key =>
                key.EndsWith("_Id", StringComparison.Ordinal) || key == "AccessId");
            if (idCount > 0)
            {
                return idCount;
            }

            return Math.Max(
                1,
                statement.Text.Split("INSERT INTO", StringSplitOptions.None).Length - 1);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.CreateVersion7();
    }
}

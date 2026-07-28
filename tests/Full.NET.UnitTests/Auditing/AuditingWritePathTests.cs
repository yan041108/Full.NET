using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteAccessLogs;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

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
    public async Task Empty_request_buffer_does_not_open_a_transaction()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);

        var succeeded = await writer.TryWriteAsync(
            new AuditWriteBuffer(),
            CancellationToken.None);

        Assert.IsTrue(succeeded);
        Assert.AreEqual(0, transaction.ExecutionCount);
        Assert.AreEqual(0, executor.ExecutionCount);
    }

    [TestMethod]
    public async Task One_audit_category_uses_the_exact_single_insert_statement()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);
        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateAccessModel());

        var succeeded = await writer.TryWriteAsync(
            buffer,
            CancellationToken.None);

        Assert.IsTrue(succeeded);
        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual(1, executor.ExecutionCount);
        Assert.AreEqual(
            "auditing.insert_request_audit_batch.access",
            executor.Statement!.Name);
        Assert.AreEqual(
            1,
            executor.Statement.Text.Split(
                "INSERT INTO",
                StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public async Task Two_audit_categories_share_one_transaction_and_one_command()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);
        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateAccessModel());
        buffer.Capture(CreateOperationModel());

        var succeeded = await writer.TryWriteAsync(
            buffer,
            CancellationToken.None);

        Assert.IsTrue(succeeded);
        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual(1, executor.ExecutionCount);
        Assert.AreEqual(
            "auditing.insert_request_audit_batch.access_operation",
            executor.Statement!.Name);
        Assert.AreEqual(
            2,
            executor.Statement.Text.Split(
                "INSERT INTO",
                StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    public async Task Three_audit_categories_use_one_transaction_and_one_command()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);
        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateAccessModel());
        buffer.Capture(CreateOperationModel());
        buffer.Capture(CreateExceptionModel());

        var succeeded = await writer.TryWriteAsync(
            buffer,
            CancellationToken.None);

        Assert.IsTrue(succeeded);
        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual(1, executor.ExecutionCount);
        Assert.AreEqual(
            "auditing.insert_request_audit_batch.access_operation_exception",
            executor.Statement!.Name);
        StringAssert.Contains(
            executor.Statement.Text,
            "INSERT INTO fn_auditing_access_log");
        StringAssert.Contains(
            executor.Statement.Text,
            "INSERT INTO fn_auditing_operation_log");
        StringAssert.Contains(
            executor.Statement.Text,
            "INSERT INTO fn_auditing_exception_log");
    }

    [TestMethod]
    public async Task Batch_failure_is_reported_without_escaping_the_request_path()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor
        {
            Exception = new InvalidOperationException("database unavailable"),
        };
        var writer = CreateWriter(transaction, executor);
        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateOperationModel());

        var succeeded = await writer.TryWriteAsync(
            buffer,
            CancellationToken.None);

        Assert.IsFalse(succeeded);
        Assert.AreEqual(1, transaction.ExecutionCount);
        Assert.AreEqual(1, executor.ExecutionCount);
    }

    [TestMethod]
    public async Task Coordinator_ignores_request_abort_when_flushing_audit()
    {
        var transaction = new RecordingCommandTransaction();
        var executor = new RecordingCommandExecutor();
        var writer = CreateWriter(transaction, executor);
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
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, buffer, writer);

        Assert.AreEqual(1, executor.ExecutionCount);
        Assert.IsFalse(executor.CancellationToken.IsCancellationRequested);
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

    private static OperationLogWriteModel CreateOperationModel() =>
        new(
            "PUT /api/v1/tenancy/tenants/1",
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

    private sealed class RecordingCommandTransaction : ICommandTransaction
    {
        public int ExecutionCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return await action(cancellationToken);
        }
    }

    private sealed class RecordingCommandExecutor : ICommandExecutor
    {
        public int ExecutionCount { get; private set; }

        public SqlStatement? Statement { get; private set; }

        public CancellationToken CancellationToken { get; private set; }

        public Exception? Exception { get; init; }

        public Task<int> ExecuteAsync(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            Statement = statement;
            CancellationToken = cancellationToken;
            return Exception is null
                ? Task.FromResult(
                    statement.Text.Split(
                        "INSERT INTO",
                        StringSplitOptions.None).Length - 1)
                : Task.FromException<int>(Exception);
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

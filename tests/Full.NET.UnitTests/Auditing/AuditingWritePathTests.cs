using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteAuditBatch;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Middleware;
using Full.NET.Modules.Identity.Contracts;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        buffer.Capture(CreateOperationModel("op-1"));

        Assert.ThrowsExactly<InvalidOperationException>(
            () => buffer.Capture(CreateOperationModel("op-2")));
        Assert.AreEqual(1, buffer.Snapshot().Count);
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
            buffer.Capture(CreateOperationModel("abort-safe"));
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, buffer, harness.Coordinator)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsGreaterThanOrEqualTo(1, harness.Executor.ExecutionCount);
        Assert.IsFalse(harness.Executor.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public void Operation_detail_and_absolute_expiry_share_one_insert_and_byte_budget()
    {
        var expiresAtUtc = new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
        var contextJson = "{\"schemaVersion\":1,\"clientIp\":\"2001:db8::1\"}";
        var summary = CreateOperationModel("op-detail");
        var detailed = summary with
        {
            Details = new AuditOperationDetails(contextJson, expiresAtUtc),
        };

        var built = AuditWriteBatchSql.BuildOperations(
            [(Guid.CreateVersion7(), detailed)], expiresAtUtc.AddDays(-1));

        Assert.IsNotNull(built.Statement);
        StringAssert.Contains(built.Statement.Text, "ContextJson, DetailsExpiresAtUtc");
        Assert.AreEqual(contextJson, built.Parameters["o0_ContextJson"]);
        Assert.AreEqual(expiresAtUtc, built.Parameters["o0_DetailsExpiresAtUtc"]);
        Assert.AreEqual(15, built.ParameterCount);
        Assert.IsGreaterThan(
            AuditWriteEnvelope.ForOperation(summary).EstimatedBytes,
            AuditWriteEnvelope.ForOperation(detailed).EstimatedBytes);
    }

    [TestMethod]
    public void Summary_only_operation_writes_null_detail_columns()
    {
        var built = AuditWriteBatchSql.BuildOperations(
            [(Guid.CreateVersion7(), CreateOperationModel("op-summary"))],
            new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero));

        Assert.IsNull(built.Parameters["o0_ContextJson"]);
        Assert.IsNull(built.Parameters["o0_DetailsExpiresAtUtc"]);
    }

    [TestMethod]
    public void Oversize_detail_downgrades_to_summary_without_dropping_operation()
    {
        var model = CreateOperationModel("op-oversize") with
        {
            Details = new AuditOperationDetails(
                "{\"payload\":\"" + new string('中', 3000) + "\"}",
                new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero)),
        };

        var envelope = AuditWriteEnvelope.ForOperation(model);

        Assert.IsNotNull(envelope.Operation);
        Assert.IsNull(envelope.Operation.Details);
        Assert.AreEqual(model.ActionKey, envelope.Operation.ActionKey);
    }

    [TestMethod]
    public async Task Microbatch_byte_budget_rejects_while_first_row_is_in_flight_and_recovers()
    {
        var model = CreateOperationModel("same-sized-row");
        var charge = AuditWriteEnvelope.ForOperation(model).EstimatedBytes;
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                QueueMaxBytes = charge,
                MaxBatchRows = 1,
                MaxBatchBytes = charge,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            },
            flushGate);

        var first = harness.Coordinator.FlushImportantAsync(model, null, CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1, TimeSpan.FromSeconds(2));
            var rejected = harness.Coordinator.FlushImportantAsync(
                model, null, CancellationToken.None);
            await rejected.WaitAsync(TimeSpan.FromMilliseconds(500));
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(charge, harness.Coordinator.QueueBytesInUse);
        }
        finally
        {
            flushGate.TrySetResult();
        }

        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse == 0,
            TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
        await harness.Coordinator.FlushImportantAsync(model, null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse == 0,
            TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
        Assert.AreEqual(2, harness.Executor.CommittedIdCount);
    }

    [TestMethod]
    public async Task Microbatch_enqueue_timeout_releases_byte_reservation()
    {
        var model = CreateOperationModel("same-sized-row");
        var charge = AuditWriteEnvelope.ForOperation(model).EstimatedBytes;
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 1,
                QueueMaxBytes = charge * 3L,
                MaxBatchRows = 1,
                MaxBatchBytes = charge,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromMilliseconds(100),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            },
            flushGate);

        var first = harness.Coordinator.FlushImportantAsync(model, null, CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1, TimeSpan.FromSeconds(2));
            var second = harness.Coordinator.FlushImportantAsync(model, null, CancellationToken.None);
            await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse >= charge * 2L,
                TimeSpan.FromSeconds(2));
            await harness.Coordinator.FlushImportantAsync(model, null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2));
            Assert.AreEqual(charge * 2L, harness.Coordinator.QueueBytesInUse);
            flushGate.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            flushGate.TrySetResult();
        }

        await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse == 0,
            TimeSpan.FromSeconds(2));
        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
    }

    [TestMethod]
    public void Microbatch_options_reject_queue_budget_smaller_than_one_batch()
    {
        var options = new AuditMicroBatchOptions
        {
            QueueMaxBytes = 255,
            MaxBatchBytes = 256,
        };

        var result = new AuditMicroBatchOptionsValidator().Validate(null, options);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(string.Join("; ", result.Failures ?? []), "QueueMaxBytes");
    }

    [TestMethod]
    public void Microbatch_byte_budget_is_atomic_under_parallel_producers()
    {
        var budget = new AuditQueueByteBudget();
        var accepted = 0;

        Parallel.For(0, 128, _ =>
        {
            if (budget.TryReserve(100, 1000))
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.AreEqual(10, accepted);
        Assert.AreEqual(1000, budget.ReservedBytes);
        for (var index = 0; index < accepted; index++)
        {
            budget.Release(100);
        }

        Assert.AreEqual(0, budget.ReservedBytes);
    }

    [TestMethod]
    public async Task Microbatch_stopped_channel_fails_open_and_releases_byte_reservation()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions());
        await harness.Coordinator.StopAsync(CancellationToken.None);

        await harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("after-stop"), null, CancellationToken.None);

        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
    }

    [TestMethod]
    public async Task Microbatch_writer_resolution_failure_fails_open_and_releases_budget()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                MaxBatchRows = 1,
                MaxBatchBytes = 1024,
                ShutdownFlushTimeout = TimeSpan.FromMilliseconds(100),
            },
            flushGate: null,
            failWriterResolutionFromAttempt: 1);

        var pending = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("resolve-failure"), null, CancellationToken.None);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
    }

    [TestMethod]
    public async Task Microbatch_shutdown_drain_failure_completes_queued_row_and_releases_budget()
    {
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                MaxBatchRows = 1,
                MaxBatchBytes = 1024,
                ShutdownFlushTimeout = TimeSpan.FromMilliseconds(500),
            },
            flushGate,
            failWriterResolutionFromAttempt: 2);

        var first = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("active-row"), null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1,
            TimeSpan.FromSeconds(2));
        var second = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("queued-row"), null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse >= 2 *
            AuditWriteEnvelope.ForOperation(CreateOperationModel("active-row")).EstimatedBytes,
            TimeSpan.FromSeconds(2));

        await harness.Coordinator.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
    }

    [TestMethod]
    public async Task Microbatch_shutdown_with_invalid_hot_options_completes_all_reserved_rows()
    {
        var options = new AuditMicroBatchOptions
        {
            Capacity = 8,
            MaxBatchRows = 1,
            MaxBatchBytes = 1024,
            ShutdownFlushTimeout = TimeSpan.FromMilliseconds(500),
        };
        var optionsMonitor = new SwitchingOptionsMonitor(options);
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            options,
            flushGate,
            optionsMonitor: optionsMonitor);

        var first = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("active-row"), null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1,
            TimeSpan.FromSeconds(2));
        var second = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("queued-row"), null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse >= 2 *
            AuditWriteEnvelope.ForOperation(CreateOperationModel("active-row")).EstimatedBytes,
            TimeSpan.FromSeconds(2));
        optionsMonitor.FailReads = true;

        await harness.Coordinator.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
    }

    [TestMethod]
    public async Task Microbatch_invalid_hot_options_fail_open_before_reservation()
    {
        var options = new AuditMicroBatchOptions();
        var optionsMonitor = new SwitchingOptionsMonitor(options);
        await using var harness = await MicroBatchHarness.CreateAsync(
            options,
            flushGate: null,
            optionsMonitor: optionsMonitor);
        optionsMonitor.FailReads = true;

        await harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("invalid-hot-options"), null, CancellationToken.None);

        Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
        Assert.AreEqual(0, harness.Transaction.ExecutionCount);
    }

    [TestMethod]
    public async Task Microbatch_invalid_hot_options_fail_open_already_queued_rows_while_running()
    {
        var options = new AuditMicroBatchOptions
        {
            Capacity = 8,
            MaxBatchRows = 1,
            MaxBatchBytes = 1024,
        };
        var optionsMonitor = new SwitchingOptionsMonitor(options);
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            options,
            flushGate,
            optionsMonitor: optionsMonitor);

        var first = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("active-row"), null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1,
            TimeSpan.FromSeconds(2));
        var second = harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("queued-row"), null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse >= 2 *
            AuditWriteEnvelope.ForOperation(CreateOperationModel("active-row")).EstimatedBytes,
            TimeSpan.FromSeconds(2));

        try
        {
            optionsMonitor.FailReads = true;
            flushGate.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            await second.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitUntilAsync(() => harness.Coordinator.QueueBytesInUse == 0,
                TimeSpan.FromSeconds(2));
            Assert.AreEqual(0, harness.Coordinator.QueueBytesInUse);
        }
        finally
        {
            flushGate.TrySetResult();
            optionsMonitor.FailReads = false;
        }
    }

    [TestMethod]
    [DataRow(StatusCodes.Status400BadRequest)]
    [DataRow(StatusCodes.Status500InternalServerError)]
    [DataRow(StatusCodes.Status503ServiceUnavailable)]
    public async Task Operation_uses_mapped_exception_status_before_audit_flush(
        int mappedStatusCode)
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 16,
                MaxBatchRows = 1,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        var buffer = new AuditWriteBuffer();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/orders";
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    FullNetIdentityClaimTypes.Subject,
                    Guid.CreateVersion7().ToString())],
                "test"));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddMetrics();
        services.AddProblemDetails();
        services.AddSingleton(new System.Diagnostics.DiagnosticListener("audit-test"));
        using var provider = services.BuildServiceProvider();
        context.RequestServices = provider;

        var app = new ApplicationBuilder(provider);
        app.Use(next =>
        {
            var coordinator = new AuditWriteCoordinatorMiddleware(next);
            return httpContext => coordinator.InvokeAsync(
                httpContext,
                buffer,
                harness.Coordinator);
        });
        app.UseExceptionHandler(handler => handler.Run(httpContext =>
        {
            httpContext.Response.StatusCode = mappedStatusCode;
            return Task.CompletedTask;
        }));
        app.Run(httpContext =>
        {
            var operation = new OperationLogMiddleware(
                _ => throw new InvalidOperationException("mapped exception"));
            return operation.InvokeAsync(httpContext, new OperationLogWriter(buffer));
        });

        await app.Build()(context);

        var captured = buffer.Snapshot().Operation;
        Assert.IsNotNull(captured);
        Assert.AreEqual(mappedStatusCode, context.Response.StatusCode);
        Assert.AreEqual(mappedStatusCode, captured.StatusCode);
        Assert.IsFalse(captured.Succeeded);
        Assert.AreEqual(1, harness.Executor.CommittedIdCount);
    }

    [TestMethod]
    public void Microbatch_envelope_never_undercharges_multibyte_text()
    {
        var model = CreateOperationModel(new string('中', 100));
        var envelope = AuditWriteEnvelope.ForOperation(model);
        var payloadBytes = Encoding.UTF8.GetByteCount(model.ActionKey)
            + Encoding.UTF8.GetByteCount(model.HttpMethod)
            + Encoding.UTF8.GetByteCount(model.RequestPath)
            + Encoding.UTF8.GetByteCount(model.TraceId!)
            + Encoding.UTF8.GetByteCount(model.ClientIpFingerprint!)
            + Encoding.UTF8.GetByteCount(model.PermissionCode!);

        Assert.IsGreaterThanOrEqualTo(payloadBytes + 128, envelope.EstimatedBytes);
    }

    [TestMethod]
    public void Microbatch_envelope_keeps_utf16_memory_charge_for_ascii_text()
    {
        var model = CreateOperationModel(new string('a', 100));
        var envelope = AuditWriteEnvelope.ForOperation(model);
        var textChars = model.ActionKey.Length
            + model.HttpMethod.Length
            + model.RequestPath.Length
            + model.TraceId!.Length
            + model.ClientIpFingerprint!.Length
            + model.PermissionCode!.Length;

        Assert.IsGreaterThanOrEqualTo(128 + textChars * 2, envelope.EstimatedBytes);
    }

    [TestMethod]
    public async Task Microbatch_does_not_combine_rows_beyond_byte_limit()
    {
        var first = CreateOperationModel("first-row");
        var second = CreateOperationModel("second-row");
        var maxBytes = AuditWriteEnvelope.ForOperation(first).EstimatedBytes
            + AuditWriteEnvelope.ForOperation(second).EstimatedBytes - 1;
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                MaxBatchRows = 8,
                MaxBatchBytes = maxBytes,
                MaxBatchDelay = TimeSpan.FromMilliseconds(500),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        await Task.WhenAll(
            harness.Coordinator.FlushImportantAsync(first, null, CancellationToken.None),
            harness.Coordinator.FlushImportantAsync(second, null, CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(2, harness.Transaction.ExecutionCount);
        Assert.AreEqual(2, harness.Executor.CommittedIdCount);
    }

    [TestMethod]
    public async Task Oversize_single_b1_row_fails_open_before_database_write()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                MaxBatchRows = 8,
                MaxBatchBytes = 1,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        await harness.Coordinator.FlushImportantAsync(
            CreateOperationModel("oversize"), null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(0, harness.Transaction.ExecutionCount);
        Assert.AreEqual(0, harness.Executor.CommittedIdCount);
    }

    [TestMethod]
    public async Task Shutdown_completes_deferred_b1_row_when_active_flush_is_cancelled()
    {
        var first = CreateOperationModel("first-row");
        var second = CreateOperationModel("second-row");
        var maxBytes = AuditWriteEnvelope.ForOperation(first).EstimatedBytes
            + AuditWriteEnvelope.ForOperation(second).EstimatedBytes - 1;
        var flushGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 8,
                MaxBatchRows = 8,
                MaxBatchBytes = maxBytes,
                MaxBatchDelay = TimeSpan.FromSeconds(30),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromMilliseconds(50),
            },
            flushGate);

        var firstFlush = harness.Coordinator.FlushImportantAsync(first, null, CancellationToken.None);
        var secondFlush = harness.Coordinator.FlushImportantAsync(second, null, CancellationToken.None);
        await WaitUntilAsync(() => harness.Transaction.ExecutionCount >= 1, TimeSpan.FromSeconds(2));
        await harness.Coordinator.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(firstFlush, secondFlush).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task Stopped_audit_coordinator_does_not_replace_successful_response()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 16,
                MaxBatchRows = 1,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });
        await harness.Coordinator.StopAsync(CancellationToken.None);

        var buffer = new AuditWriteBuffer();
        buffer.Capture(CreateOperationModel("stopped-coordinator"));
        var middleware = new AuditWriteCoordinatorMiddleware(
            _ => Task.CompletedTask);

        await middleware.InvokeAsync(
            new DefaultHttpContext(),
            buffer,
            harness.Coordinator);
    }

    [TestMethod]
    public async Task Cancelled_request_preserves_exception_and_waits_for_b1_attempt()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 16,
                MaxBatchRows = 1,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = CreateAuthenticatedMutationContext();
        context.RequestAborted = aborted.Token;
        var buffer = new AuditWriteBuffer();
        var operation = new OperationLogMiddleware(
            _ => throw new OperationCanceledException(aborted.Token));
        var coordinator = new AuditWriteCoordinatorMiddleware(
            httpContext => operation.InvokeAsync(
                httpContext,
                new OperationLogWriter(buffer)));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => coordinator.InvokeAsync(context, buffer, harness.Coordinator));

        Assert.IsFalse(buffer.Snapshot().Operation?.Succeeded);
        Assert.AreEqual(1, harness.Executor.CommittedIdCount);
        Assert.IsFalse(harness.Executor.CancellationToken.IsCancellationRequested);
    }

    [TestMethod]
    public async Task Started_response_exception_keeps_sent_status_and_failed_operation()
    {
        await using var harness = await MicroBatchHarness.CreateAsync(
            new AuditMicroBatchOptions
            {
                Capacity = 16,
                MaxBatchRows = 1,
                MaxBatchBytes = 1_000_000,
                MaxBatchDelay = TimeSpan.FromMilliseconds(20),
                EnqueueTimeout = TimeSpan.FromSeconds(1),
                ShutdownFlushTimeout = TimeSpan.FromSeconds(2),
            });

        var buffer = new AuditWriteBuffer();
        var context = CreateAuthenticatedMutationContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddMetrics();
        services.AddProblemDetails();
        services.AddSingleton(new System.Diagnostics.DiagnosticListener("audit-started-test"));
        using var provider = services.BuildServiceProvider();
        context.RequestServices = provider;

        var app = new ApplicationBuilder(provider);
        app.Use(next =>
        {
            var coordinator = new AuditWriteCoordinatorMiddleware(next);
            return httpContext => coordinator.InvokeAsync(
                httpContext,
                buffer,
                harness.Coordinator);
        });
        app.UseExceptionHandler(handler => handler.Run(httpContext =>
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return Task.CompletedTask;
        }));
        app.Run(httpContext =>
        {
            var operation = new OperationLogMiddleware(
                _ => throw new InvalidOperationException("after response start"));
            return operation.InvokeAsync(httpContext, new OperationLogWriter(buffer));
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => app.Build()(context));

        Assert.AreEqual(StatusCodes.Status200OK, buffer.Snapshot().Operation?.StatusCode);
        Assert.IsFalse(buffer.Snapshot().Operation?.Succeeded);
        Assert.AreEqual(1, harness.Executor.CommittedIdCount);
    }

    private static DefaultHttpContext CreateAuthenticatedMutationContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/orders";
        context.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    FullNetIdentityClaimTypes.Subject,
                    Guid.CreateVersion7().ToString())],
                "test"));
        return context;
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = Stream.Null;

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state) { }

        public void OnCompleted(Func<object, Task> callback, object state) { }
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

    private static AuditWriteBatchWriter CreateWriter(
        ICommandTransaction transaction,
        ICommandExecutor executor) =>
        new(
            transaction,
            executor,
            new FixedClock(),
            new SequenceIdGenerator(),
            NullLogger<AuditWriteBatchWriter>.Instance);

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
            TaskCompletionSource? flushGate,
            int failWriterResolutionFromAttempt = int.MaxValue,
            IOptionsMonitor<AuditMicroBatchOptions>? optionsMonitor = null)
        {
            var transaction = new RecordingCommandTransaction(flushGate);
            var executor = new RecordingCommandExecutor();
            var writer = CreateWriter(transaction, executor);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IOptionsMonitor<AuditMicroBatchOptions>>(
                optionsMonitor ?? new StaticOptionsMonitor(options));
            services.AddSingleton<IClock, FixedClock>();
            services.AddSingleton<IIdGenerator, SequenceIdGenerator>();
            services.AddSingleton<ICommandTransaction>(transaction);
            services.AddSingleton<ICommandExecutor>(executor);
            var resolutionAttempt = 0;
            services.AddScoped<AuditWriteBatchWriter>(_ =>
                Interlocked.Increment(ref resolutionAttempt) >= failWriterResolutionFromAttempt
                    ? throw new InvalidOperationException("writer resolution failed")
                    : writer);

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

    private sealed class SwitchingOptionsMonitor(AuditMicroBatchOptions current)
        : IOptionsMonitor<AuditMicroBatchOptions>
    {
        private int _failReads;

        public bool FailReads
        {
            set => Volatile.Write(ref _failReads, value ? 1 : 0);
        }

        public AuditMicroBatchOptions CurrentValue => Volatile.Read(ref _failReads) == 0
            ? current
            : throw new OptionsValidationException(
                AuditMicroBatchOptions.SectionName,
                typeof(AuditMicroBatchOptions),
                ["invalid hot configuration"]);

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

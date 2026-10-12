using System.Diagnostics;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>步骤写入观测关联原业务身份；方法返回不是外层事务提交或外部派发的证明。</summary>
[TestClass, DoNotParallelize]
public sealed class AiAgentStepTraceTests
{
    [TestMethod]
    [DataRow("running")] [DataRow("awaiting_approval")] [DataRow("completed")] [DataRow("failed")]
    public async Task Step_trace_links_the_written_step_and_retains_payload_async(string status)
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture();
        f.Transaction.BeforeCommit = () => Assert.HasCount(0, capture.Spans);
        Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit with { NewStatusKey = status }));
        capture.Verify(f.RunId, "step_write", "completed", 1);
        var span = capture.Spans.Single(); var p = f.StepParameters!;
        Assert.AreEqual(f.Step.StepId.ToString("D"), span.GetTagItem("ai.step.id"));
        Assert.AreEqual(f.Step.OperationId.ToString("D"), span.GetTagItem("ai.operation.id"));
        Assert.AreEqual(span.TraceId.ToHexString(), p["TraceId"]);
        Assert.AreEqual(f.Step.StepId, p["StepId"]); Assert.AreEqual(f.RunId, p["RunId"]);
        Assert.AreEqual(f.Step.InputTokens, p["InputTokens"]); Assert.AreEqual(f.Step.ArgumentDigest, p["ArgumentDigest"]);
        Assert.IsNull(f.Step.TraceId); Assert.IsFalse(f.Transaction.InTransaction);
    }

    [TestMethod]
    [DataRow("0123456789abcdef0123456789abcdef")] [DataRow("legacy-trace-value")]
    public async Task Step_trace_preserves_the_callers_original_trace_async(string trace)
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture();
        Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit with { Step = f.Step with { TraceId = trace } }));
        Assert.AreEqual(trace, f.StepParameters!["TraceId"]);
        Assert.AreEqual(f.Step.StepId.ToString("D"), capture.Spans.Single().GetTagItem("ai.step.id"));
    }

    [TestMethod]
    [DataRow("step")] [DataRow("event")] [DataRow("commit")]
    public async Task Step_trace_preserves_failure_and_does_not_report_returned_success_async(string fault)
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture { Fault = fault };
        f.Transaction.Failure = fault == "commit" ? f.Error : null;
        Assert.AreSame(f.Error, await Assert.ThrowsExactlyAsync<IOException>(() => f.Store.CommitProgressAsync(f.Commit).AsTask()));
        capture.Verify(f.RunId, "step_write", "failed", 1); Assert.IsFalse(f.Transaction.InTransaction);
    }

    [TestMethod]
    [DataRow("sample")] [DataRow("started")] [DataRow("stopped")] [DataRow("attempts")] [DataRow("duration")]
    public async Task Step_trace_listener_fault_cannot_change_the_write_async(string fault)
    {
        using var capture = new AgentTelemetryCapture(throwTrace: fault, throwMeasurement: fault);
        var f = new Fixture(); Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit));
        Assert.IsNotNull(f.StepParameters); Assert.AreEqual(f.Step.StepId, f.StepParameters["StepId"]);
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Step_trace_omits_empty_identity_tags_async(bool emptyStep)
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture();
        var step = emptyStep ? f.Step with { StepId = Guid.Empty } : f.Step with { OperationId = Guid.Empty };
        Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit with { Step = step }));
        var span = capture.Spans.Single(); Assert.IsNull(span.GetTagItem(emptyStep ? "ai.step.id" : "ai.operation.id"));
    }

    [TestMethod]
    public async Task Step_trace_fenced_lease_does_not_create_a_step_observation_async()
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture { Fenced = true };
        Assert.IsFalse(await f.Store.CommitProgressAsync(f.Commit));
        Assert.IsNull(f.StepParameters); Assert.HasCount(0, capture.Spans); Assert.HasCount(0, capture.Measurements);
    }

    [TestMethod]
    public async Task Step_trace_event_only_progress_does_not_invent_a_step_async()
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture();
        Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit with { Step = null }));
        Assert.IsNull(f.StepParameters); Assert.HasCount(0, capture.Spans); Assert.HasCount(0, capture.Measurements);
    }

    [TestMethod]
    public async Task Step_trace_sampling_none_keeps_null_in_the_persistent_column_async()
    {
        using var capture = new AgentTelemetryCapture(sampleNone: true); var f = new Fixture();
        Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit)); Assert.IsNull(f.StepParameters!["TraceId"]);
        Assert.HasCount(0, capture.Spans); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    public async Task Step_trace_new_worker_reuses_run_identity_without_reusing_step_identity_async()
    {
        var f = new Fixture(); string firstTrace;
        using (var first = new AgentTelemetryCapture())
        {
            Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit)); firstTrace = (string)f.StepParameters!["TraceId"]!;
            Assert.AreEqual(f.RunId.ToString("D"), first.Spans.Single().GetTagItem("ai.run.id"));
        }
        using var second = new AgentTelemetryCapture(); var next = f.Step with { StepId = Guid.CreateVersion7() };
        Assert.IsTrue(await f.Store.CommitProgressAsync(f.Commit with { Lease = f.Commit.Lease with { WorkerId = "worker-b", Epoch = 2 }, Step = next }));
        var span = second.Spans.Single(); Assert.AreEqual(f.RunId.ToString("D"), span.GetTagItem("ai.run.id"));
        Assert.AreEqual(next.StepId.ToString("D"), span.GetTagItem("ai.step.id"));
        Assert.AreNotEqual(firstTrace, f.StepParameters!["TraceId"]);
    }

    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Step_trace_cancellation_retains_the_original_exception_async(bool callerCancelled)
    {
        using var capture = new AgentTelemetryCapture(); using var source = new CancellationTokenSource();
        var original = new OperationCanceledException("secret-timeout", source.Token);
        var f = new Fixture { Fault = "step", Error = original, BeforeFailure = () => { if (callerCancelled) source.Cancel(); } };
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Store.CommitProgressAsync(f.Commit, source.Token).AsTask()));
        Assert.AreEqual(callerCancelled ? "cancelled" : "failed", capture.Spans.Single().GetTagItem("ai.outcome"));
        Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod]
    [DataRow("unknown")] [DataRow("")]
    public async Task Step_trace_invalid_status_does_not_enter_transaction_async(string status)
    {
        using var capture = new AgentTelemetryCapture(); var f = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Store.CommitProgressAsync(f.Commit with { NewStatusKey = status }).AsTask());
        Assert.AreEqual(0, f.Transaction.Attempts); Assert.HasCount(0, capture.Spans);
    }

    private sealed class Fixture
    {
        internal readonly Guid RunId = Guid.CreateVersion7();
        internal readonly StepTransaction Transaction = new();
        internal readonly AgentStepUpsert Step = new(Guid.CreateVersion7(), "secret-step-key", 1, Guid.CreateVersion7(), "committed",
            1, null, null, 7, 3, null, null, null, "secret-digest", null);
        internal readonly AiAgentRunStore Store;
        internal AgentRunProgressCommit Commit => new(new(RunId, "worker-a", 1, 1, DateTimeOffset.UtcNow.AddMinutes(1)), "running", Step, null,
            new(Guid.CreateVersion7(), 1, "run.progress", 1, "{}"));
        internal IReadOnlyDictionary<string, object?>? StepParameters;
        internal string? Fault; internal bool Fenced; internal Exception Error = new IOException("secret-error");
        internal Action? BeforeFailure;
        internal Fixture()
        {
            var commands = Substitute.For<ICommandExecutor>();
            commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var statement = call.ArgAt<SqlStatement>(0);
                if (statement == AiAgentRunSql.UpdateRunStatus) return Task.FromResult(Fenced ? 0 : 1);
                if (statement == AiAgentRunSql.InsertStep) StepParameters = call.ArgAt<IReadOnlyDictionary<string, object?>>(1);
                if (Fault == "step" && statement == AiAgentRunSql.InsertStep || Fault == "event" && statement == AiAgentRunSql.InsertEvent)
                { BeforeFailure?.Invoke(); return Task.FromException<int>(Error); }
                return Task.FromResult(1);
            });
            Store = new(Substitute.For<IQueryExecutor>(), commands, Transaction, Substitute.For<IIdGenerator>(), Substitute.For<IClock>(),
                Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        }
    }

    /// <summary>在回调返回后模拟真正提交或提交失败，防止把 SQL 回调返回误作事务已结束。</summary>
    private sealed class StepTransaction : ICommandTransaction
    {
        internal bool InTransaction; internal int Attempts; internal Action? BeforeCommit; internal Exception? Failure;
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            Attempts++; InTransaction = true;
            try { var result = await action(cancellationToken); BeforeCommit?.Invoke(); if (Failure is not null) throw Failure; return result; }
            finally { InTransaction = false; }
        }
    }
}

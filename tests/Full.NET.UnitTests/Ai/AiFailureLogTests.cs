using System.Collections;
using System.Data.Common;
using Full.NET.AI.Abstractions.Budgets;
using Full.NET.Modules.Ai.Diagnostics;
using Full.NET.Modules.Ai.Runtime;
using Microsoft.Extensions.Logging;

namespace Full.NET.UnitTests.Ai;

/// <summary>诊断出口只投影受控分类，不读取异常正文、链、数据或类型名称。</summary>
[TestClass]
public sealed class AiFailureLogTests
{
    [TestMethod]
    public void Failure_log_sink_failure_does_not_poison_next_record_on_same_thread()
    {
        var faulty = new FailureCaptureLogger<AiFailureLogTests> { ThrowOnLog = true };
        var healthy = new FailureCaptureLogger<AiFailureLogTests>();
        AiFailureLog.Write(faulty, LogLevel.Error, AiFailureStage.AgentRun, new IOException());
        AiFailureLog.Write(healthy, LogLevel.Warning, AiFailureStage.KnowledgeIndex, new TimeoutException());
        Assert.HasCount(1, faulty.Entries); Assert.HasCount(1, healthy.Entries);
        Assert.AreEqual("timeout", healthy.Entries[0].State["FailureCategory"]);
        Assert.AreEqual("knowledge_index", healthy.Entries[0].State["Stage"]);
        Assert.AreEqual(5, healthy.Entries[0].State.Count);
    }

    [TestMethod]
    public void Failure_log_nested_sink_keeps_parent_and_child_states_independent()
    {
        var child = new FailureCaptureLogger<AiFailureLogTests>(); var parentId = Guid.CreateVersion7(); var childId = Guid.CreateVersion7();
        var parent = new FailureCaptureLogger<AiFailureLogTests>
        {
            BeforeLog = () => AiFailureLog.Write(child, LogLevel.Warning, AiFailureStage.ChatLease, new TimeoutException(), childId),
        };
        AiFailureLog.Write(parent, LogLevel.Error, AiFailureStage.AgentRun, new IOException(), parentId);
        Assert.HasCount(1, parent.Entries); Assert.HasCount(1, child.Entries);
        Assert.AreEqual(parentId, parent.Entries[0].State["OperationId"]);
        Assert.AreEqual("storage", parent.Entries[0].State["FailureCategory"]);
        Assert.AreEqual("agent_run", parent.Entries[0].State["Stage"]);
        Assert.AreEqual(childId, child.Entries[0].State["OperationId"]);
        Assert.AreEqual("timeout", child.Entries[0].State["FailureCategory"]);
        Assert.AreEqual("chat_lease", child.Entries[0].State["Stage"]);
    }

    [TestMethod]
    [DataRow(0, "other")] [DataRow(1, "cancelled")] [DataRow(2, "timeout")]
    [DataRow(3, "authorization")] [DataRow(4, "budget")] [DataRow(5, "budget")]
    [DataRow(6, "provider")] [DataRow(7, "storage")] [DataRow(8, "invalid")]
    [DataRow(9, "invalid")] [DataRow(10, "other")] [DataRow(11, "other")] [DataRow(12, "timeout")] [DataRow(13, "other")]
    public void Failure_log_projects_only_closed_category_without_exception_payload(int kind, string expected)
    {
        Exception failure = kind switch
        {
            0 => new InvalidOperationException("secret-prompt"), 1 => new OperationCanceledException("secret-prompt"),
            2 => new TimeoutException("secret-prompt"), 3 => new UnauthorizedAccessException("secret-prompt"),
            4 => new AiBudgetException("secret-code"), 5 => new AiRunOutputBudgetException(true, "secret-code"),
            6 => new HttpRequestException("secret-prompt"), 7 => new IOException("secret-prompt"),
            8 => new InvalidDataException("secret-prompt"), 9 => new ArgumentException("secret-prompt"),
            10 => new SecretGetterFailure(), 11 => new AggregateException(new HttpRequestException("secret-prompt")),
            12 => new SecretTimeoutFailure(), _ => new SecretDatabaseFailure(),
        };
        if (kind != 10) failure.Data["secret-key"] = "secret-value";
        var logger = new FailureCaptureLogger<AiFailureLogTests>(); var id = Guid.CreateVersion7();
        AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.KnowledgeIndex, failure, id);
        Assert.HasCount(1, logger.Entries, logger.CaptureFailure?.ToString());
        var entry = logger.Entries[0];
        Assert.IsNull(entry.Exception); Assert.AreEqual(expected, entry.State["FailureCategory"]);
        Assert.AreEqual(id, entry.State["OperationId"]);
        Assert.AreEqual("ai.knowledge.index_unavailable", entry.State["FailureCode"]);
        Assert.AreEqual("AiOperationFailed", entry.EventId.Name);
        Assert.AreEqual(5601, entry.EventId.Id);
        Assert.IsFalse(entry.Text.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(entry.State.Values.Any(value => value is Exception));
        var scope = logger.Scopes.Single();
        Assert.AreEqual("diagnostic", scope["log.class"]);
        Assert.AreEqual("BestEffort", scope["reliability.class"]);
        Assert.AreEqual("Internal", scope["data.classification"]);
        Assert.AreEqual("ai.operations", scope["DiagnosticGroup"]);
    }

    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public void Failure_log_isolates_enabled_scope_sink_and_disposal_failures(int fault)
    {
        var logger = new FailureCaptureLogger<AiFailureLogTests>
        { ThrowOnEnabled = fault == 0, ThrowOnScope = fault == 1, ThrowOnLog = fault == 2, ThrowOnScopeDispose = fault == 3 };
        AiFailureLog.Write(logger, LogLevel.Error, AiFailureStage.AgentRun, new SecretGetterFailure());
    }

    [TestMethod]
    [DataRow(LogLevel.Error)] [DataRow(LogLevel.Warning)]
    public void Failure_log_preserves_original_level(LogLevel level)
    {
        var logger = new FailureCaptureLogger<AiFailureLogTests>();
        AiFailureLog.Write(logger, level, AiFailureStage.AgentBudgetRepair, new IOException());
        Assert.AreEqual(level, logger.Entries.Single().Level);
    }

    [TestMethod]
    public void Failure_log_stages_are_closed_and_unknown_values_do_not_emit_dynamic_names()
    {
        var logger = new FailureCaptureLogger<AiFailureLogTests>();
        foreach (var stage in Enum.GetValues<AiFailureStage>()) AiFailureLog.Write(logger, LogLevel.Warning, stage, null);
        Assert.AreEqual(Enum.GetValues<AiFailureStage>().Length, logger.Entries.Count);
        Assert.AreEqual(logger.Entries.Count, logger.Entries.Select(e => e.State["Stage"]).Distinct().Count());
        Assert.IsFalse(logger.Entries.Any(e => e.State["Stage"] is "other"));
        AiFailureLog.Write(logger, LogLevel.Warning, (AiFailureStage)int.MaxValue, null, Guid.Empty);
        Assert.AreEqual("other", logger.Entries.Last().State["Stage"]);
        Assert.IsNull(logger.Entries.Last().State["OperationId"]);
    }

    [TestMethod]
    public void Failure_log_absent_exception_is_reported_as_lease_lost_only_for_fencing()
    {
        var logger = new FailureCaptureLogger<AiFailureLogTests>();
        AiFailureLog.Write(logger, LogLevel.Warning, AiFailureStage.AgentApprovalCommit, null);
        Assert.AreEqual("lease_lost", logger.Entries.Single().State["FailureCategory"]);
        AiFailureLog.Write(logger, LogLevel.Error, AiFailureStage.AgentRun, null);
        Assert.AreEqual("other", logger.Entries.Last().State["FailureCategory"]);
    }

    [TestMethod]
    public void Failure_log_disabled_logger_never_creates_scope_or_record()
    {
        var logger = new FailureCaptureLogger<AiFailureLogTests> { Enabled = false };
        AiFailureLog.Write(logger, LogLevel.Error, AiFailureStage.AgentRun, new SecretGetterFailure());
        Assert.HasCount(0, logger.Scopes); Assert.HasCount(0, logger.Entries);
    }

    private sealed class SecretTimeoutFailure() : TimeoutException("secret-type-payload");
    private sealed class SecretDatabaseFailure() : DbException("secret-database");
    private sealed class SecretGetterFailure : Exception
    {
        public override string Message => throw new AssertFailedException("不得读取异常消息。");
        public override string? StackTrace => throw new AssertFailedException("不得读取堆栈。");
        public override string ToString() => throw new AssertFailedException("不得格式化原始异常。");
        public override IDictionary Data => throw new AssertFailedException("不得读取异常数据。");
    }
}

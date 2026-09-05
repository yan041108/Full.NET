using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;

namespace Full.NET.UnitTests.Workflow;

/// <summary>验证恢复失败退避、成功关闭与耗尽死信暂停。</summary>
[TestClass]
public sealed class WorkflowRecoveryRetryTests
{
    /// <summary>指数退避必须封顶在配置的最大延迟。</summary>
    [TestMethod]
    public void Exponential_backoff_caps_at_max_delay()
    {
        var options = new WorkflowRecoveryWorkerOptions
        {
            RetryDelaySeconds = 2,
            RetryBackoffMode = "exponential",
            RetryMaxDelaySeconds = 10,
        };
        var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(now.AddSeconds(2), WorkflowRecoveryRetry.ComputeNextAttempt(now, 1, options));
        Assert.AreEqual(now.AddSeconds(10), WorkflowRecoveryRetry.ComputeNextAttempt(now, 8, options));
    }

    /// <summary>已修复不得暂停实例。</summary>
    [TestMethod]
    public void Succeeded_does_not_suspend_the_instance()
    {
        var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var outcome = WorkflowRecoveryRetry.ResolveOutcome(
            WorkflowRecoveryRetry.Succeeded, 8, now, new WorkflowRecoveryWorkerOptions());

        Assert.AreEqual(WorkflowRecoveryStatuses.Succeeded, outcome.Status);
        Assert.IsNull(outcome.NextAttempt);
        Assert.IsFalse(outcome.SuspendInstance);
    }

    /// <summary>达到最大尝试次数必须死信并要求暂停实例。</summary>
    [TestMethod]
    public void Exhausted_attempts_dead_letter_and_require_suspend()
    {
        var options = new WorkflowRecoveryWorkerOptions { MaxAttempts = 3 };
        var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var retryable = WorkflowRecoveryRetry.ResolveOutcome(
            WorkflowRecoveryRetry.Retryable, 2, now, options);
        var deadLetter = WorkflowRecoveryRetry.ResolveOutcome(
            WorkflowRecoveryRetry.Retryable, 3, now, options);

        Assert.AreEqual(WorkflowRecoveryStatuses.Failed, retryable.Status);
        Assert.IsFalse(retryable.SuspendInstance);
        Assert.AreEqual(WorkflowRecoveryStatuses.DeadLettered, deadLetter.Status);
        Assert.IsTrue(deadLetter.SuspendInstance);
        Assert.IsNull(deadLetter.NextAttempt);
    }
}

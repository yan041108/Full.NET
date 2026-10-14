using Full.NET.AI.Abstractions.Models;
using Full.NET.Agents.Runtime;
using Full.NET.Modules.Ai.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

/// <summary>诊断限制随异步执行流传递，父流程释放或子流程默认策略不能重新开放。</summary>
[TestClass, DoNotParallelize]
public sealed class AiDataObservationScopeTests
{
    internal static AiDataProcessingPolicy Deny => new(new Dictionary<string, IReadOnlyList<string>> { ["telemetry"] = [] });

    [TestMethod]
    public void Nested_default_policy_cannot_reenable_parent_diagnostics()
    {
        Assert.IsTrue(AiDataObservationScope.CanObserve);
        using (AiDataObservationScope.Enter(Deny))
        {
            using (AiDataObservationScope.Enter(AiDataProcessingPolicy.Default, "public"))
                Assert.IsFalse(AiDataObservationScope.CanObserve);
            Assert.IsFalse(AiDataObservationScope.CanObserve);
        }
        Assert.IsTrue(AiDataObservationScope.CanObserve);
    }

    [TestMethod]
    public async Task Detached_child_keeps_frozen_policy_after_parent_disposal()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> child;
        using (AiDataObservationScope.Enter(Deny))
            child = Task.Run(async () => { await release.Task; return AiDataObservationScope.CanObserve; });
        Assert.IsTrue(AiDataObservationScope.CanObserve); release.SetResult(); Assert.IsFalse(await child);
    }

    [TestMethod]
    public void Out_of_order_disposal_cannot_enable_active_child()
    {
        var parent = AiDataObservationScope.Enter(Deny);
        var child = AiDataObservationScope.Enter(AiDataProcessingPolicy.Default);
        parent.Dispose(); Assert.IsFalse(AiDataObservationScope.CanObserve);
        child.Dispose(); Assert.IsFalse(AiDataObservationScope.CanObserve);
        parent.Dispose(); Assert.IsTrue(AiDataObservationScope.CanObserve);
    }

    [TestMethod]
    public void Denied_observation_never_calls_listeners_or_logger_and_keeps_business_outcome()
    {
        using var capture = new AgentTelemetryCapture(); var logger = Substitute.For<ILogger>();
        AgentExecutionTelemetry.Observation observation;
        using (AiDataObservationScope.Enter(Deny))
        {
            observation = AgentExecutionTelemetry.Start(Guid.CreateVersion7(), "run");
            observation.Outcome = "completed";
            AiFailureLog.Write(logger, LogLevel.Error, AiFailureStage.AgentRun, new InvalidDataException("secret"));
        }
        // 创建时禁止的观测不能在宿主范围结束后重新发布。
        observation.Dispose(); Assert.AreEqual("completed", observation.Outcome);
        Assert.IsNull(observation.TraceId); Assert.HasCount(0, capture.Spans); Assert.HasCount(0, capture.Measurements);
        Assert.HasCount(0, logger.ReceivedCalls().ToArray());
    }
}

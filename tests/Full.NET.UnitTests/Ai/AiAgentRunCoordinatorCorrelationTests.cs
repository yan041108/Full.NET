using Full.NET.Abstractions.Observability;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiAgentRunCoordinatorTests
{
    [TestMethod]
    [DataRow(SessionBindingKinds.Refresh, false)]
    [DataRow(SessionBindingKinds.Refresh, true)]
    [DataRow(SessionBindingKinds.OidcApplication, false)]
    [DataRow(SessionBindingKinds.OidcApplication, true)]
    public async Task Verified_background_binding_reaches_execution_and_failure_logs_then_restores_parent(string kind, bool fail)
    {
        var user = Guid.CreateVersion7(); var tenant = Guid.CreateVersion7(); var session = Guid.CreateVersion7();
        var parent = new LogCorrelationSnapshot { RequestId = "parent", UserId = Guid.CreateVersion7() };
        using var outer = LogCorrelationContext.BeginScope(() => parent);
        LogCorrelationSnapshot? execution = null;
        var logger = new CorrelationLogger();
        var fixture = await CreateFixtureAsync(sessionId: session, runnerThrows: fail, logger: logger,
            configureRecord: record => { record.ActorUserId = user; record.TenantId = tenant; record.SessionKind = kind; },
            duringModelLookup: () => execution = LogCorrelationContext.Capture());
        await fixture.Coordinator.ProcessPendingAsync();
        Assert.IsNotNull(execution);
        AssertBinding(execution);
        if (fail) { Assert.IsNotNull(logger.Snapshot); AssertBinding(logger.Snapshot); }
        Assert.AreEqual(parent.UserId, LogCorrelationContext.Capture().UserId);
        Assert.AreEqual("parent", LogCorrelationContext.Capture().RequestId);

        void AssertBinding(LogCorrelationSnapshot captured)
        {
            Assert.AreEqual(user, captured.UserId); Assert.AreEqual(tenant, captured.TenantId);
            Assert.AreEqual(kind == SessionBindingKinds.Refresh ? session : (Guid?)null, captured.SessionId);
            Assert.AreEqual(kind == SessionBindingKinds.OidcApplication ? session : (Guid?)null, captured.ApplicationSessionId);
            Assert.IsNull(captured.RequestId); Assert.IsNull(captured.ClientIpFingerprint); Assert.IsNull(captured.CenterSessionId);
        }
    }

    private sealed class CorrelationLogger : ILogger<AiAgentRunCoordinator>
    {
        internal LogCorrelationSnapshot? Snapshot;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Snapshot = LogCorrelationContext.Capture();
    }
}

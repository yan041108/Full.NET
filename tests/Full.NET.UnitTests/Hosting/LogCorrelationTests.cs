using System.Diagnostics;
using Full.NET.Abstractions.Observability;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class LogCorrelationTests
{
    [TestMethod]
    public void Capture_KeepsRequestIdSeparateWithoutActivity()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            using var scope = LogCorrelationContext.BeginScope(() => new() { RequestId = "request-1" });
            var snapshot = LogCorrelationContext.Capture();
            Assert.IsNull(snapshot.TraceId);
            Assert.AreEqual("request-1", snapshot.RequestId);
        }
        finally { Activity.Current = previous; }
    }

    [TestMethod]
    public async Task Capture_IsImmutableAndScopeExitClearsInheritedContext()
    {
        var session = Guid.NewGuid();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<LogCorrelationSnapshot> child;
        LogCorrelationSnapshot captured;
        using (LogCorrelationContext.BeginScope(() => new() { SessionId = session }))
        {
            captured = LogCorrelationContext.Capture();
            child = Task.Run(async () => { await release.Task; return LogCorrelationContext.Capture(); });
        }
        release.SetResult();
        Assert.AreEqual(session, captured.SessionId);
        Assert.IsNull((await child).SessionId);
    }

    [TestMethod]
    public void Capture_UsesActiveSpanAndRestoresNestedScope()
    {
        using var parent = new Activity("parent").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var child = new Activity("child").Start();
        var user = Guid.NewGuid();
        using var scope = LogCorrelationContext.BeginScope(() => new() { UserId = user });
        var snapshot = LogCorrelationContext.Capture();
        Assert.AreEqual(child.TraceId.ToString(), snapshot.TraceId);
        Assert.AreEqual(child.SpanId.ToString(), snapshot.SpanId);
        Assert.AreEqual(parent.SpanId.ToString(), snapshot.ParentSpanId);
        using (LogCorrelationContext.BeginScope(() => new() { UserId = Guid.NewGuid() }))
            Assert.AreNotEqual(user, LogCorrelationContext.Capture().UserId);
        Assert.AreEqual(user, LogCorrelationContext.Capture().UserId);
    }

    [TestMethod]
    public void Json_RoundTripsOnlyBoundedCorrelationFields()
    {
        var snapshot = new LogCorrelationSnapshot { UserId = Guid.NewGuid(), SessionId = Guid.NewGuid(), RequestId = "request-2" };
        var roundTrip = LogCorrelationSnapshot.FromJson(snapshot.ToJson());
        Assert.AreEqual(snapshot, roundTrip);
        Assert.IsNull(LogCorrelationSnapshot.FromJson("invalid"));
    }
}

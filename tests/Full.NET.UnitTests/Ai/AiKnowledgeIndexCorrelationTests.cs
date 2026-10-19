using Full.NET.Abstractions.Observability;
using Full.NET.AI.Abstractions.Budgets;
using NSubstitute;

namespace Full.NET.UnitTests.Ai;

public sealed partial class AiKnowledgeIndexEmbeddingTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Verified_index_session_is_visible_to_provider_and_settlement_only_within_execution(bool tenant)
    {
        using var f = new Fixture(tenant);
        LogCorrelationSnapshot? provider = null; LogCorrelationSnapshot? settlement = null;
        f.Factory.BeforeCreate = () => provider = LogCorrelationContext.Capture();
        f.Budget.SettleAsync(Arg.Any<Guid>(), Arg.Any<AiOperationUsage>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { settlement = LogCorrelationContext.Capture(); return Task.CompletedTask; });
        Assert.IsNotNull(await f.Execute());
        Assert.IsNotNull(provider); Assert.IsNotNull(settlement);
        Assert.AreEqual(f.Session.UserId, provider.UserId); Assert.AreEqual(f.Session.TenantId, provider.TenantId);
        Assert.AreEqual(f.Session.SessionId, provider.SessionId); Assert.AreEqual(provider.UserId, settlement.UserId);
        Assert.IsNull(provider.ApplicationSessionId); Assert.IsNull(provider.ClientIpFingerprint);
        Assert.IsNull(LogCorrelationContext.Capture().UserId);
    }

    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Index_failure_keeps_verified_identity_for_outer_worker_but_denied_binding_does_not(bool denied)
    {
        using var f = new Fixture(false) { Denied = denied };
        using (f.Correlation.BeginCapture())
        {
            f.Factory.BeforeCreate = () => throw new IOException("provider creation failed");
            if (denied) Assert.IsNull(await f.Execute());
            else await Assert.ThrowsExactlyAsync<IOException>(() => f.Execute());
            Assert.AreEqual(denied ? (Guid?)null : f.Session.UserId, LogCorrelationContext.Capture().UserId);
            Assert.AreEqual(denied ? (Guid?)null : f.Session.SessionId, LogCorrelationContext.Capture().SessionId);
        }
        Assert.IsNull(LogCorrelationContext.Capture().UserId);
    }
}

public sealed partial class AiKnowledgeIndexPdfPlanningServiceTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Verified_pdf_index_session_reaches_model_and_restores_ambient(bool tenant)
    {
        using var f = new Fixture(tenant); var execution = f.Paid();
        LogCorrelationSnapshot? captured = null;
        f.Factory.BeforeCreate = () => captured = LogCorrelationContext.Capture();
        Assert.IsNotNull(await execution.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        Assert.IsNotNull(captured); Assert.AreEqual(f.Session.UserId, captured.UserId);
        Assert.AreEqual(f.Session.TenantId, captured.TenantId); Assert.AreEqual(f.Session.SessionId, captured.SessionId);
        Assert.IsNull(LogCorrelationContext.Capture().UserId);
    }
}

public sealed partial class AiKnowledgeIndexDocxPlanningServiceTests
{
    [TestMethod] [DataRow(false)] [DataRow(true)]
    public async Task Verified_docx_index_session_reaches_model_and_restores_ambient(bool tenant)
    {
        using var f = new Fixture(tenant); var execution = f.Paid();
        LogCorrelationSnapshot? captured = null;
        f.Factory.BeforeCreate = () => captured = LogCorrelationContext.Capture();
        Assert.IsNotNull(await execution.ExecuteAsync(f.Binding, f.TaskId, f.Lease, 0, default));
        Assert.IsNotNull(captured); Assert.AreEqual(f.Session.UserId, captured.UserId);
        Assert.AreEqual(f.Session.TenantId, captured.TenantId); Assert.AreEqual(f.Session.SessionId, captured.SessionId);
        Assert.IsNull(LogCorrelationContext.Capture().UserId);
    }
}

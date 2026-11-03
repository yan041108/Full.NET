using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>证明准备必须有界、按真实参与方批量读取，空证明不能变成全局读取许可。</summary>
[TestClass]
public sealed class WorkflowCollaborationListAuthorizationTests
{
    [TestMethod]
    [DataRow("active", true)][DataRow("revoked", false)][DataRow("expired", false)][DataRow("closing", false)]
    [DataRow("foreign_actor", false)][DataRow("source_disabled", false)][DataRow("target_disabled", false)][DataRow("binding", false)]
    public async Task Proofs_require_the_current_grant_fixed_member_and_both_active_tenants(string defect, bool expected)
    {
        var data = WorkflowCollaborationTargetTests.Fixture(); var grant = data.Grant; var admission = Header(data.Admission);
        if (defect == "revoked") grant = grant with { StatusKey = "revoked", Revision = 3, RevokedAtUtc = data.Now };
        if (defect == "expired") grant = grant with { ExpiresAtUtc = data.Now };
        if (defect == "closing") admission = admission with { StatusKey = "cancel_requested" };
        if (defect == "binding") admission = admission with { BindingHash = new string('0', 64) };
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new TenantContext(grant.TargetTenantId!.Value, "target", "目标"));
        var query = Substitute.For<IQueryExecutor>(); var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(data.Now);
        query.QueryAsync<WorkflowCollaborationListCandidate>(WorkflowCollaborationListSql.CandidatesSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns([admission]);
        query.QueryAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationListSql.Grants, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns([grant]);
        var tenants = Substitute.For<IActiveTenantContextResolver>();
        var active = new Dictionary<Guid, TenantContext>();
        foreach (var id in new[] { grant.SourceTenantId!.Value, grant.TargetTenantId.Value }) active.Add(id, new(id, "active", "活动"));
        if (defect == "source_disabled") active.Remove(grant.SourceTenantId.Value);
        if (defect == "target_disabled") active.Remove(grant.TargetTenantId.Value);
        tenants.ResolveActiveByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(active);
        var service = new WorkflowCollaborationListAuthorization(query, Substitute.For<IDataTransactionState>(), tenant, clock, tenants,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        var parameters = WorkflowSqlParameters.Create(("Take", 10));
        var result = await service.PrepareAsync("claim", defect == "foreign_actor" ? Guid.CreateVersion7() : data.Accepted.StarterUserId, parameters, default);
        Assert.IsTrue(result.IsSuccess); using var proof = JsonDocument.Parse(result.Value!);
        Assert.AreEqual(expected ? 1 : 0, proof.RootElement.GetArrayLength());
        Assert.AreEqual(10, parameters["Take"], "授权候选上限不能覆盖原领取池分页大小");
        Assert.AreEqual(WorkflowCollaborationListAuthorization.MaximumCalls + 1, parameters["CollaborationTake"]);
        await tenants.DidNotReceive().ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        if (expected) await tenants.Received(1).ResolveActiveByIdsAsync(Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Count == 2), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Overflow_does_not_truncate_permission_facts_or_read_foreign_directories()
    {
        var data = WorkflowCollaborationTargetTests.Fixture(); var tenant = new CurrentTenantAccessor(); tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>(); var tenants = Substitute.For<IActiveTenantContextResolver>();
        query.QueryAsync<WorkflowCollaborationListCandidate>(WorkflowCollaborationListSql.CandidatesSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Repeat(Header(data.Admission), WorkflowCollaborationListAuthorization.MaximumCalls + 1).ToArray());
        var service = new WorkflowCollaborationListAuthorization(query, Substitute.For<IDataTransactionState>(), tenant, Substitute.For<IClock>(), tenants,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        var result = await service.PrepareAsync("instance", Guid.CreateVersion7(), [], default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(WorkflowErrorCodes.CollaborationQueryTooBroad, result.Error?.Code);
        Assert.AreEqual(0, tenants.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task A_business_transaction_or_missing_actor_cannot_prepare_read_permissions()
    {
        var state = Substitute.For<IDataTransactionState>(); var tenant = new CurrentTenantAccessor(); tenant.SetHost(); var query = Substitute.For<IQueryExecutor>();
        var service = new WorkflowCollaborationListAuthorization(query, state, tenant, Substitute.For<IClock>(), Substitute.For<IActiveTenantContextResolver>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        Assert.IsFalse((await service.PrepareAsync("instance", Guid.Empty, [], default)).IsSuccess);
        state.HasTransaction.Returns(true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.PrepareAsync("instance", Guid.CreateVersion7(), [], default));
        Assert.AreEqual(0, query.ReceivedCalls().Count());
    }
    private static WorkflowCollaborationListCandidate Header(WorkflowCollaborationAdmissionRecord row) => new(row.Id, row.SourceTenantScopeKey,
        row.TargetTenantScopeKey, row.DeliveryId, row.GrantId, row.BindingJson, row.BindingHash, row.ProjectionHash, row.StatusKey, row.TargetInstanceId, row.Revision);
}

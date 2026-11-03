using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Execution;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Workflow;

/// <summary>目标来源不能扩大同意，实时状态及控制面锁优先于旧许可快照。</summary>
[TestClass]
public sealed class WorkflowCollaborationTargetTests
{
    [TestMethod]
    [DataRow(1, 2)][DataRow(5, 3)]
    public async Task Relay_retries_the_whole_candidate_on_deadlock_with_a_fixed_limit(int failures, int expectedReads)
    {
        var query = Substitute.For<IQueryExecutor>(); var calls = 0;
        query.QueryAsync<WorkflowCollaborationRelayCandidate>(WorkflowCollaborationRelaySql.PendingSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns([new(Guid.CreateVersion7(), "host", "target", DateTimeOffset.UtcNow)]);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls <= failures
                ? Task.FromException<WorkflowCollaborationAdmissionRecord?>(new DataCommandException(DataCommandFailureKind.Deadlock, new InvalidOperationException()))
                : Task.FromResult<WorkflowCollaborationAdmissionRecord?>(null));
        var relay = new WorkflowCollaborationRelay(query, Substitute.For<IDataTransactionState>(), Substitute.For<IServiceScopeFactory>(),
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }), Options.Create(new WorkflowRecoveryWorkerOptions()),
            Substitute.For<IClock>(), new WorkflowCollaborationScanCursor());
        Assert.AreEqual(0, await relay.ProcessPendingAsync(default));
        Assert.AreEqual(expectedReads, calls);
        // 后续轮次仍扫描耐久事实，不把失败候选计为完成或使扫描永久跳过。
        await relay.ProcessPendingAsync(default);
        Assert.IsGreaterThan(expectedReads, calls);
    }
    [TestMethod]
    [DataRow("completed", true)][DataRow("active", false)][DataRow("cancelled", false)][DataRow("revoked", false)]
    public async Task Successful_receipt_keeps_authorized_history_without_reopening_business_operations(string condition, bool expected)
    {
        var data = Fixture(); var query = Substitute.For<IQueryExecutor>(); var grant = data.Grant;
        if (condition == "revoked") grant = grant with { StatusKey = "revoked", Revision = 3, RevokedAtUtc = data.Now };
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Origin);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Admission with { StatusKey = "completed" });
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(grant);
        var root = WorkflowExecutionFrameScopeTests.Create().Instance with { Id = data.Origin.Id, TenantId = grant.TargetTenantId, ScopeKey = "tenant",
            TenantScopeKey = grant.TargetTenantScopeKey, DefinitionVersionId = data.Accepted.TargetDefinitionVersionId, StatusKey = condition == "revoked" ? "completed" : condition };
        query.QuerySingleOrDefaultAsync<WorkflowInstanceRecord>(WorkflowSql.FindInstanceById, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(root);
        var scope = new WorkflowManagementScope(grant.TargetTenantId, "tenant", grant.TargetTenantScopeKey);
        Assert.AreEqual(expected, await WorkflowCollaborationRuntimeGate.AllowsReadAsync(root.Id, scope, data.Accepted.StarterUserId, data.Now, query, default));
        Assert.IsFalse(await WorkflowCollaborationRuntimeGate.AllowsAsync(root.Id, scope, data.Accepted.StarterUserId, data.Now, query, default));
    }
    internal static (WorkflowCollaborationAdmissionRecord Admission, WorkflowCollaborationGrantFact Grant, WorkflowCollaborationAcceptancePolicy Accepted,
        WorkflowCollaborationOriginRecord Origin, DateTimeOffset Now) Fixture()
    {
        var (grant, now) = WorkflowCollaborationGrantPolicyTests.Grant();
        var accepted = JsonSerializer.Deserialize(grant.AcceptanceJson!, WorkflowJsonSerializerContext.Default.WorkflowCollaborationAcceptancePolicy)!;
        var call = Guid.CreateVersion7(); var delivery = Guid.CreateVersion7(); var source = Guid.CreateVersion7(); var target = Guid.CreateVersion7();
        var binding = new WorkflowCollaborationCallBinding(call, delivery, source, Guid.CreateVersion7(), grant.SourceFormVersionId, source,
            grant.SourceTenantScopeKey, grant.TargetTenantScopeKey, grant.SourceTenantId, grant.TargetTenantId, "call", "end", null, null, 1,
            new('a', 64), grant.Id, 2, grant.PolicyHash!, accepted.TargetDefinitionVersionId, accepted.TargetSummaryHash, accepted.MaximumActivities, 1, WorkflowCollaborationCallPolicy.Hash("{}"));
        var json = JsonSerializer.Serialize(binding, WorkflowJsonSerializerContext.Default.WorkflowCollaborationCallBinding);
        return (new(call, grant.SourceTenantScopeKey, grant.TargetTenantScopeKey, delivery, grant.Id, json, WorkflowCollaborationCallPolicy.Hash(json),
            "{}", binding.ProjectionHash, "active", target, 3, now, now), grant, accepted, new(target, call, target, grant.TargetTenantScopeKey, 1), now);
    }

    [TestMethod]
    [DataRow("extra")]
    [DataRow("duplicate")]
    [DataRow("projection")]
    [DataRow("scope")]
    public void Shared_admission_rejects_changed_or_expanded_proof(string defect)
    {
        var row = Fixture().Admission;
        if (defect is "extra" or "duplicate")
        {
            var json = row.BindingJson.Insert(1, defect == "extra" ? "\"tenantOverride\":true," : "\"grantRevision\":2,");
            row = row with { BindingJson = json, BindingHash = WorkflowCollaborationCallPolicy.Hash(json) };
        }
        else row = defect == "scope" ? row with { TargetTenantScopeKey = "host" } : row with { ProjectionJson = "{\"secret\":1}" };
        Assert.IsFalse(WorkflowCollaborationAdmissionProof.TryRead(row, out _));
    }

    [TestMethod]
    [DataRow("active")]
    [DataRow("revoked")]
    [DataRow("expired")]
    [DataRow("cancel_requested")]
    [DataRow("foreign_actor")]
    [DataRow("foreign_recipient")]
    [DataRow("source_disabled")]
    [DataRow("target_disabled")]
    public async Task Each_operation_checks_live_consent_both_scopes_and_fixed_members(string condition)
    {
        var data = Fixture(); var row = data.Admission; var grant = data.Grant; var now = data.Now;
        if (condition == "revoked") grant = grant with { StatusKey = "revoked", Revision = 3 };
        if (condition == "expired") now = grant.ExpiresAtUtc;
        if (condition == "cancel_requested") row = row with { StatusKey = condition };
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Origin);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(grant);
        var tenants = Substitute.For<IActiveTenantContextResolver>();
        tenants.ResolveActiveByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(value =>
            new TenantContext(value.ArgAt<Guid>(0), "active", "活动租户"));
        if (condition == "source_disabled") tenants.ResolveActiveByIdAsync(grant.SourceTenantId!.Value, Arg.Any<CancellationToken>()).Returns((TenantContext?)null);
        if (condition == "target_disabled") tenants.ResolveActiveByIdAsync(grant.TargetTenantId!.Value, Arg.Any<CancellationToken>()).Returns((TenantContext?)null);
        var scope = new WorkflowManagementScope(grant.TargetTenantId, "tenant", grant.TargetTenantScopeKey);
        Assert.AreEqual(condition == "active", await WorkflowCollaborationRuntimeGate.AllowsAsync(data.Origin.Id, scope,
            condition == "foreign_actor" ? Guid.CreateVersion7() : data.Accepted.StarterUserId, now, query, default, tenants: tenants,
            selectedUser: condition == "foreign_recipient" ? Guid.CreateVersion7() : null));
    }

    [TestMethod]
    public async Task An_ordinary_root_never_acquires_shared_locks_or_resolves_foreign_tenants()
    {
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var tenants = Substitute.For<IActiveTenantContextResolver>();
        Assert.IsTrue(await WorkflowCollaborationRuntimeGate.AllowsAsync(Guid.CreateVersion7(), new(null, "host", "host"), Guid.CreateVersion7(),
            DateTimeOffset.UtcNow, query, default, commands, tenants));
        Assert.AreEqual(0, commands.ReceivedCalls().Count()); Assert.AreEqual(0, tenants.ReceivedCalls().Count());
    }

    [TestMethod]
    public void Stored_agreement_is_only_a_member_constraint_after_revocation()
    {
        var data = Fixture(); var revoked = data.Grant with { StatusKey = "revoked", Revision = 3 };
        Assert.IsTrue(WorkflowCollaborationGrantPolicy.TryReadAgreement(revoked, out _, out _));
        Assert.IsFalse(WorkflowCollaborationGrantPolicy.TryAuthorize(revoked, revoked.TargetTenantScopeKey, true, 2, data.Now, out _, out _));
        var member = data.Accepted.StarterUserId; var snapshot = new WorkflowAssigneeResolutionSnapshot(1, member, [member], []);
        var json = JsonSerializer.Serialize(snapshot, WorkflowJsonSerializerContext.Default.WorkflowAssigneeResolutionSnapshot);
        var original = new WorkflowPreparedHumanActivities(new Dictionary<string, Result<WorkflowHumanActivityAssignees>>
        { ["approve"] = Result<WorkflowHumanActivityAssignees>.Success(new(null, member, json, WorkflowCollaborationCallPolicy.Hash(json))) });
        Assert.IsTrue(original.RestrictTo([member]).TryGet("approve", out var allowed)); Assert.IsTrue(allowed.IsSuccess);
        Assert.IsTrue(original.RestrictTo([Guid.CreateVersion7()]).TryGet("approve", out var denied)); Assert.IsFalse(denied.IsSuccess);
    }

    [TestMethod]
    [DataRow("active")]
    [DataRow("grant_changed")]
    [DataRow("admission_closed")]
    public async Task Transaction_guard_locks_grant_then_admission_and_rejects_changed_authority(string condition)
    {
        var data = Fixture(); var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationOriginRecord>(WorkflowCollaborationRelaySql.FindOrigin, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Origin);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationAdmissionRecord>(WorkflowCollaborationRelaySql.FindAdmission, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Admission);
        query.QuerySingleOrDefaultAsync<WorkflowCollaborationGrantFact>(WorkflowCollaborationGrantSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Grant);
        var writes = new List<SqlStatement>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var statement = call.ArgAt<SqlStatement>(0); writes.Add(statement);
            return statement == WorkflowCollaborationRelaySql.LockGrant && condition == "grant_changed" ||
                statement == WorkflowCollaborationRelaySql.LockActiveAdmission && condition == "admission_closed" ? 0 : 1;
        });
        Assert.AreEqual(condition == "active", await WorkflowCollaborationRuntimeGate.AllowsAsync(data.Origin.Id,
            new(data.Grant.TargetTenantId, data.Grant.TargetTenantId is null ? "host" : "tenant", data.Grant.TargetTenantScopeKey),
            data.Accepted.StarterUserId, data.Now, query, default, commands));
        CollectionAssert.AreEqual(condition == "grant_changed" ? new[] { WorkflowCollaborationRelaySql.LockGrant } :
            new[] { WorkflowCollaborationRelaySql.LockGrant, WorkflowCollaborationRelaySql.LockActiveAdmission }, writes.ToArray());
    }

    [TestMethod]
    public void Relay_cursor_advances_past_a_full_blocked_page_then_wraps_for_retries()
    {
        var cursor = new WorkflowCollaborationScanCursor();
        var rows = new[] { new WorkflowCollaborationRelayCandidate(Guid.CreateVersion7(), "host", "tenant:a", DateTimeOffset.UtcNow),
            new WorkflowCollaborationRelayCandidate(Guid.CreateVersion7(), "host", "tenant:b", DateTimeOffset.UtcNow.AddSeconds(1)) };
        cursor.Advance(rows, 2);
        Assert.AreEqual(rows[1].Id, cursor.Read().Id); Assert.AreEqual(rows[1].UpdatedAtUtc, cursor.Read().UpdatedAtUtc);
        cursor.Advance([], 2); Assert.IsNull(cursor.Read().Id); Assert.IsNull(cursor.Read().UpdatedAtUtc);
        cursor.Advance(rows[..1], 2); Assert.IsNull(cursor.Read().Id);
    }

    [TestMethod]
    public async Task Terminal_cleanup_closes_source_collaboration_admission_before_other_cleanup()
    {
        var commands = Substitute.For<ICommandExecutor>(); var writes = new List<SqlStatement>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        { writes.Add(call.ArgAt<SqlStatement>(0)); return 1; });
        await WorkflowHumanActivityLifecycle.CancelAsync(commands, Guid.CreateVersion7(), "host", DateTimeOffset.UtcNow, default);
        Assert.AreEqual(WorkflowCollaborationRelaySql.RequestCancellation, writes[0]);
        Assert.AreEqual(1, writes.Count(statement => statement == WorkflowCollaborationRelaySql.RequestCancellation));
    }
}

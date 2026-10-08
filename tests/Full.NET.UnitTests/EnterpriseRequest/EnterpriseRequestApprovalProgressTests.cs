using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>审批进度只暴露当前租户与组织读取范围内的单据绑定。</summary>
[TestClass]
public sealed class EnterpriseRequestApprovalProgressTests
{
    [TestMethod]
    public async Task Draft_without_submission_is_not_submitted()
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = "Draft", Version = 1 }; f.Submission = null;
        var result = await f.Read();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.NotSubmitted, result.Value!.DeliveryState);
        Assert.IsNull(result.Value.WorkflowInstanceId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Submission_exposes_queued_or_started_receipt(bool started)
    {
        var f = new Fixture();
        if (started) f.Submission = f.Submission! with { StartedAtUtc = f.Now.AddSeconds(1) };
        var result = await f.Read();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(started ? EnterpriseRequestApprovalDeliveryState.Started : EnterpriseRequestApprovalDeliveryState.Queued, result.Value!.DeliveryState);
        Assert.AreEqual(f.Submission!.WorkflowInstanceId, result.Value.WorkflowInstanceId);
        Assert.AreEqual(f.Submission.RequestVersion, result.Value.SubmittedVersion);
        Assert.AreEqual(f.Submission.CreatedAtUtc, result.Value.SubmittedAtUtc);
    }

    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Terminal_receipt_is_visible_even_before_start_receipt(string status)
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = status, Version = 3 };
        f.Submission = f.Submission! with { FinalStatus = status, CompletedAtUtc = f.Now.AddSeconds(2), LastMessageId = Guid.NewGuid() };
        var result = await f.Read();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Finalized, result.Value!.DeliveryState);
        Assert.AreEqual(status, result.Value.RequestStatus);
        Assert.IsNull(result.Value.StartedAtUtc);
        Assert.AreEqual(f.Submission.CompletedAtUtc, result.Value.CompletedAtUtc);
    }

    [TestMethod]
    [DataRow("Submitted")]
    [DataRow("Approved")]
    public async Task Legacy_request_without_binding_reports_recovery_required(string status)
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = status }; f.Submission = null;
        var result = await f.Read();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.RecoveryRequired, result.Value!.DeliveryState);
        Assert.IsNull(result.Value.WorkflowInstanceId);
    }

    [TestMethod]
    public async Task Organization_filtered_request_does_not_read_submission()
    {
        var f = new Fixture(); f.Row = null;
        var result = await f.Read();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.NotFound, result.Error!.Code);
        await f.Queries.DidNotReceive().QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Scopes.Received(1).ResolveAsync(f.Actor, false, Arg.Any<CancellationToken>());
        Assert.IsTrue(f.Queries.ReceivedCalls().Any(call => call.GetArguments()[0] is SqlStatement sql && sql.Text.Contains("OrganizationUnitId = @ProgressUnit", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("request")]
    [DataRow("organization")]
    [DataRow("instance")]
    [DataRow("version")]
    [DataRow("final")]
    public async Task Inconsistent_binding_fails_closed(string mismatch)
    {
        var f = new Fixture(); var s = f.Submission!;
        f.Submission = mismatch switch {
            "tenant" => s with { TenantId = Guid.NewGuid() },
            "request" => s with { RequestId = Guid.NewGuid() },
            "organization" => s with { OrganizationUnitId = Guid.NewGuid() },
            "instance" => s with { WorkflowInstanceId = Guid.Empty },
            "version" => s with { RequestVersion = 1 },
            _ => s with { FinalStatus = "Approved", CompletedAtUtc = f.Now, LastMessageId = Guid.NewGuid() }
        };
        var result = await f.Read();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
    }

    [TestMethod]
    [DataRow("host")]
    [DataRow("actor")]
    [DataRow("request")]
    public async Task Invalid_context_does_not_query(string kind)
    {
        var f = new Fixture();
        if (kind == "host") f.Tenant.Id.Returns((Guid?)null);
        var result = await f.Service.GetAsync(kind == "request" ? Guid.Empty : f.Id, kind == "actor" ? Guid.Empty : f.Actor, true);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, f.Queries.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal readonly Guid Id = Guid.NewGuid(), Actor = Guid.NewGuid(), Unit = Guid.NewGuid(), TenantId = Guid.NewGuid();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IUserDataScopeResolver Scopes = Substitute.For<IUserDataScopeResolver>();
        internal EnterpriseRequestRecord? Row;
        internal EnterpriseRequestApprovalSubmission? Submission;
        internal readonly EnterpriseRequestApprovalProgressService Service;
        internal Fixture()
        {
            Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(TenantId);
            Row = new(Id, TenantId, Unit, "REQ", "Request", "Submitted", 1m, Actor, 2, Now, Actor, null, null, false, null, null);
            Submission = new(Guid.NewGuid(), TenantId, Id, 2, Guid.NewGuid(), Guid.NewGuid(), Actor, Unit, "Request", Now, null, null, null, null);
            Scopes.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(false, []));
            var filters = Substitute.For<IDataScopeSqlFilterBuilder>();
            filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "OrganizationUnitId", Actor)
                .Returns(new DataScopeSqlFilter("OrganizationUnitId = @ProgressUnit", new Dictionary<string, object?> { ["ProgressUnit"] = Unit }));
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Row);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Submission);
            Service = new(new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()), Scopes, filters), Queries, Tenant);
        }
        internal Task<Full.NET.Abstractions.Results.Result<EnterpriseRequestApprovalProgressResponse>> Read() => Service.GetAsync(Id, Actor, false);
    }
}

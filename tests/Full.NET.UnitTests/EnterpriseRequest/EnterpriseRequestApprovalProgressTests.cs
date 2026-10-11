using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.ApprovalProgress;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Notifications.Contracts;
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
        Assert.IsNull(result.Value.FinalNotification);
        Assert.AreEqual(0, f.Notifications.ReceivedCalls().Count());
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
        Assert.AreEqual(0, f.Notifications.ReceivedCalls().Count());
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
        Assert.AreEqual(0, f.Notifications.ReceivedCalls().Count());
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

    [TestMethod]
    [DataRow("Approved")]
    [DataRow("Rejected")]
    [DataRow("Cancelled")]
    public async Task Filtered_final_request_never_uses_receipt_to_read_notification(string status)
    {
        var f = new Fixture(); f.Row = null;
        f.Submission = f.Submission! with { FinalStatus = status, CompletedAtUtc = f.Now, LastMessageId = Guid.NewGuid() };
        var result = await f.Read();
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(EnterpriseRequestErrorCodes.NotFound, result.Error!.Code);
        Assert.AreEqual(0, f.Notifications.ReceivedCalls().Count());
        await f.Queries.DidNotReceive().QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Foreign_or_deleted_final_request_never_reads_notification(bool deleted)
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = "Approved", Version = 3,
            IsDeleted = deleted, TenantId = deleted ? f.TenantId : Guid.NewGuid() };
        f.Submission = f.Submission! with { FinalStatus = "Approved", CompletedAtUtc = f.Now, LastMessageId = Guid.NewGuid() };
        var result = await f.Read();
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(EnterpriseRequestErrorCodes.NotFound, result.Error!.Code);
        Assert.AreEqual(0, f.Notifications.ReceivedCalls().Count());
        await f.Queries.DidNotReceive().QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("message")]
    [DataRow("completed")]
    public async Task Inconsistent_final_receipt_never_reads_notification(string kind)
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = "Approved", Version = kind == "version" ? 4 : 3 };
        f.Submission = f.Submission! with { FinalStatus = "Approved", CompletedAtUtc = kind == "completed" ? null : f.Now,
            LastMessageId = kind == "message" ? Guid.Empty : Guid.NewGuid() };
        var result = await f.Read();
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, f.Notifications.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Finalized_approval_keeps_business_and_notification_status_separate()
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = "Approved", Version = 3 };
        f.Submission = f.Submission! with { FinalStatus = "Approved", CompletedAtUtc = f.Now, LastMessageId = Guid.NewGuid() };
        var snapshot = new NotificationIntentDeliverySnapshot(Guid.NewGuid(), f.Now, 3, 1, 0, 2, 0, 0, 0, f.Now.AddMinutes(1));
        f.Notifications.FindByIdempotencyAsync("workflow", $"workflow-{f.Submission.LastMessageId:N}", Arg.Any<CancellationToken>()).Returns(snapshot);
        var result = await f.Read();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Approved", result.Value!.RequestStatus);
        Assert.AreEqual(EnterpriseRequestApprovalDeliveryState.Finalized, result.Value.DeliveryState);
        Assert.AreSame(snapshot, result.Value.FinalNotification);
        Assert.AreEqual(2, result.Value.FinalNotification!.FailedDeliveryCount);
        Assert.AreEqual(1, f.Notifications.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Notification_lookup_failure_does_not_change_completed_business_receipt()
    {
        var f = new Fixture(); f.Row = f.Row! with { Status = "Rejected", Version = 3 };
        f.Submission = f.Submission! with { FinalStatus = "Rejected", CompletedAtUtc = f.Now, LastMessageId = Guid.NewGuid() };
        var originalRow = f.Row; var originalReceipt = f.Submission;
        var expected = new InvalidOperationException("notification owner unavailable");
        f.Notifications.FindByIdempotencyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<NotificationIntentDeliverySnapshot?>(expected));
        Assert.AreSame(expected, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Read()));
        Assert.AreSame(originalRow, f.Row); Assert.AreSame(originalReceipt, f.Submission);
        Assert.AreEqual(1, f.Notifications.ReceivedCalls().Count());
    }
    private sealed class Fixture
    {
        internal readonly Guid Id = Guid.NewGuid(), Actor = Guid.NewGuid(), Unit = Guid.NewGuid(), TenantId = Guid.NewGuid();
        internal readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IUserDataScopeResolver Scopes = Substitute.For<IUserDataScopeResolver>();
        internal readonly INotificationIntentDeliveryDirectory Notifications = Substitute.For<INotificationIntentDeliveryDirectory>();
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
            Service = new(new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()), Scopes, filters), Queries, Tenant, new EnterpriseRequestNotificationProgressReader(Notifications, Tenant));
        }
        internal Task<Full.NET.Abstractions.Results.Result<EnterpriseRequestApprovalProgressResponse>> Read() => Service.GetAsync(Id, Actor, false);
    }
}

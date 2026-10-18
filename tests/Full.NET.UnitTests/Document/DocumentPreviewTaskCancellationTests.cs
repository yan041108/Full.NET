using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Identity.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>Host 预览取消复核当前会话与创建人，不以超级管理员或原创建权限绕过。</summary>
[TestClass]
public sealed class DocumentPreviewTaskCancellationTests
{
    [TestMethod]
    public async Task Pending_cancel_preserves_file_references_and_fences_version()
    {
        var f = new Fixture(); var result = await f.Cancel();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual("cancelled", result.Value!.StatusKey);
        Assert.AreEqual(f.Source, result.Value.SourceFileId); Assert.AreEqual(f.Output, result.Value.OutputFileId);
        var parameters = (Dictionary<string, object?>)f.Command.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.AreEqual(4L, parameters["ExpectedVersion"]); Assert.AreEqual(f.Binding.UserId, parameters["RequestedByUserId"]);
        Assert.AreEqual(1, f.Identity.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("processing")]
    [DataRow("succeeded")]
    [DataRow("failed")]
    public async Task Claimed_and_completed_tasks_are_conflicts_without_write(string status)
    {
        var f = new Fixture(status);
        Assert.AreEqual(ErrorType.Conflict, (await f.Cancel()).Error!.Type);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("actor")]
    [DataRow("tenantContext")]
    [DataRow("bindingTenant")]
    [DataRow("emptyStamp")]
    [DataRow("emptyUser")]
    [DataRow("emptySession")]
    [DataRow("permission")]
    [DataRow("returnedActor")]
    [DataRow("returnedSession")]
    [DataRow("returnedTenant")]
    [DataRow("scopeChanged")]
    public async Task Invalid_host_owner_or_authority_cannot_cancel(string reason)
    {
        var f = new Fixture();
        var binding = reason switch
        {
            "actor" => f.Binding with { UserId = Guid.NewGuid() },
            "bindingTenant" => f.Binding with { TenantId = Guid.NewGuid() },
            "emptyStamp" => f.Binding with { SecurityStamp = "" },
            "emptyUser" => f.Binding with { UserId = Guid.Empty },
            "emptySession" => f.Binding with { SessionId = Guid.Empty },
            _ => f.Binding,
        };
        if (reason == "tenantContext") f.Tenant.SetTenant(new TenantContext(Guid.NewGuid(), "other", "Other"));
        if (reason == "permission") f.Identity.AuthorizeAsync(binding, HostDocumentPreviewTaskPermissions.Cancel, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason.StartsWith("returned", StringComparison.Ordinal)) f.Identity.AuthorizeAsync(binding, HostDocumentPreviewTaskPermissions.Cancel, Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(reason == "returnedActor" ? Guid.NewGuid() : binding.UserId,
                reason == "returnedTenant" ? Guid.NewGuid() : null, reason == "returnedSession" ? Guid.NewGuid() : binding.SessionId));
        if (reason == "scopeChanged") f.Identity.AuthorizeAsync(binding, HostDocumentPreviewTaskPermissions.Cancel, Arg.Any<CancellationToken>())
            .Returns(_ => { f.Tenant.SetTenant(new TenantContext(Guid.NewGuid(), "other", "Other")); return Task.FromResult<AuthorizedSessionActor?>(new(binding.UserId, null, binding.SessionId)); });
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, (await f.Cancel(binding)).Error!.Code);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Repeated_cancel_is_idempotent_but_still_requires_live_permission()
    {
        var f = new Fixture("cancelled"); Assert.IsTrue((await f.Cancel()).IsSuccess);
        f.Identity.AuthorizeAsync(f.Binding, HostDocumentPreviewTaskPermissions.Cancel, Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, (await f.Cancel()).Error!.Code);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("processing", false)]
    [DataRow("pending", false)]
    [DataRow("cancelled", true)]
    public async Task Cas_loss_accepts_only_cancelled_terminal_without_retrying(string final, bool success)
    {
        var f = new Fixture(final: final, affected: 0);
        Assert.AreEqual(success, (await f.Cancel()).IsSuccess); Assert.AreEqual(1, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_task_has_no_write_or_permission_lookup()
    {
        var f = new Fixture();
        f.Query.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DocumentPreviewTaskRecord?>(null));
        Assert.AreEqual(ErrorType.NotFound, (await f.Cancel()).Error!.Type);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count()); Assert.AreEqual(0, f.Identity.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Request_cancellation_after_authorization_does_not_mutate_task()
    {
        var f = new Fixture(); using var cancellation = new CancellationTokenSource();
        f.Identity.AuthorizeAsync(f.Binding, HostDocumentPreviewTaskPermissions.Cancel, Arg.Any<CancellationToken>())
            .Returns(_ => { cancellation.Cancel(); return Task.FromResult<AuthorizedSessionActor?>(new(f.Binding.UserId, null, f.Binding.SessionId)); });
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Cancel(token: cancellation.Token));
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        internal IBackgroundSessionAuthorization Identity { get; } = Substitute.For<IBackgroundSessionAuthorization>();
        internal CurrentTenantAccessor Tenant { get; } = new();
        internal SessionBindingSnapshot Binding { get; } = new(Guid.NewGuid(), null, Guid.NewGuid(), "stamp", "host", "host");
        internal Guid Source { get; } = Guid.NewGuid();
        internal Guid Output { get; } = Guid.NewGuid();
        private readonly IClock clock = Substitute.For<IClock>();
        private readonly Guid taskId = Guid.NewGuid();

        internal Fixture(string status = "pending", string final = "cancelled", int affected = 1)
        {
            Tenant.SetHost(); clock.UtcNow.Returns(DateTimeOffset.UtcNow);
            var attempted = false;
            Query.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<DocumentPreviewTaskRecord?>(new() { Id = taskId, DocumentItemId = Guid.NewGuid(), SourceFileId = Source,
                    OutputFileId = Output, RequestedByUserId = Binding.UserId, StatusKey = attempted ? final : status, Version = attempted ? 5 : 4 }));
            Command.ExecuteAsync(DocumentPreviewTaskSql.Cancel, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => { attempted = true; return Task.FromResult(affected); });
            Identity.AuthorizeAsync(Binding, HostDocumentPreviewTaskPermissions.Cancel, Arg.Any<CancellationToken>())
                .Returns(new AuthorizedSessionActor(Binding.UserId, null, Binding.SessionId));
        }

        internal Task<Result<HostDocumentPreviewTaskResponse>> Cancel(SessionBindingSnapshot? binding = null, CancellationToken token = default) =>
            new HostDocumentPreviewTaskCancellationService(Query, Command, Tenant, Identity, clock).CancelAsync(taskId, binding ?? Binding, token);
    }
}

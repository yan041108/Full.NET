using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Features.ManageAttachments;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>文件操作不能借用申请事务；授权、版本与确切业务引用缺一不可。</summary>
[TestClass]
public sealed class EnterpriseRequestAttachmentTests
{
    [TestMethod]
    [DataRow("host")]
    [DataRow("actor")]
    [DataRow("status")]
    [DataRow("version")]
    [DataRow("organization")]
    public async Task Upload_denial_never_calls_file_store(string kind)
    {
        var f = new Fixture();
        if (kind == "host") f.Tenant.IsHost.Returns(true);
        if (kind == "status") f.Parent = f.Parent with { Status = "Submitted" };
        if (kind == "organization") f.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        var result = await f.Upload(kind == "version" ? 2 : 1, kind == "actor" ? Guid.Empty : f.Actor);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow(0L)]
    [DataRow(10485761L)]
    public async Task Invalid_declared_length_never_reads_or_uploads(long length)
    {
        var f = new Fixture(); using var content = new MemoryStream([1]);
        var result = await f.Service.UploadAsync(f.Id, 1, f.Actor, "valid.txt", content, length);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0L, content.Position); Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Actual_length_mismatch_is_rejected_before_file_store()
    {
        var f = new Fixture(); using var content = new MemoryStream([1, 2]);
        Assert.IsFalse((await f.Service.UploadAsync(f.Id, 1, f.Actor, "valid.txt", content, 1)).IsSuccess);
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Binding_conflict_rolls_back_and_releases_only_new_file_outside_transaction()
    {
        var f = new Fixture(); f.Commands.ExecuteAsync(EnterpriseRequestAttachmentSql.AdvanceParent, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Upload()).IsSuccess);
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        await f.Files.Received(1).ReleaseAsync("enterprise_request", f.Id, f.FileId, Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Commands.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Limit_failure_rolls_back_parent_and_compensates_upload()
    {
        var f = new Fixture(); f.Queries.QuerySingleOrDefaultAsync<int>(EnterpriseRequestAttachmentSql.Count, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(20);
        Assert.IsFalse((await f.Upload()).IsSuccess); Assert.AreEqual(1, f.Coordinator.RollbackCount);
        await f.Files.Received(1).ReleaseAsync("enterprise_request", f.Id, f.FileId, Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Upload_commits_binding_and_does_not_release_it()
    {
        var f = new Fixture(); var result = await f.Upload();
        Assert.IsTrue(result.IsSuccess); Assert.AreEqual(2L, result.Value!.RequestVersion);
        Assert.AreEqual(f.FileId, result.Value.Attachment.FileId); Assert.AreEqual("valid.txt", result.Value.Attachment.OriginalFileName);
        Assert.AreEqual(1, f.Coordinator.CommitCount);
        Assert.IsFalse(f.Files.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ITenantResourceFileStore.ReleaseAsync)));
    }
    [TestMethod]
    public async Task Unknown_transaction_failure_leaves_file_for_owner_reconciliation()
    {
        var f = new Fixture(); f.Commands.ExecuteAsync(EnterpriseRequestAttachmentSql.Bind, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<int>(new InvalidOperationException("probe")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Upload());
        Assert.AreEqual(1, f.Coordinator.RollbackCount);
        Assert.IsFalse(f.Files.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ITenantResourceFileStore.ReleaseAsync)));
    }
    [TestMethod]
    public async Task Expiry_winner_prevents_binding_and_rolls_back_parent_version()
    {
        var f = new Fixture(); f.Commands.ExecuteAsync(EnterpriseRequestAttachmentSql.Bind, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        Assert.IsFalse((await f.Upload()).IsSuccess); Assert.AreEqual(1, f.Coordinator.RollbackCount);
        await f.Files.Received(1).ReleaseAsync("enterprise_request", f.Id, f.FileId, Arg.Any<CancellationToken>());
    }
    [TestMethod]
    public async Task Hidden_parent_prevents_attachment_and_file_read()
    {
        var f = new Fixture(); f.Parent = null!;
        Assert.IsFalse((await f.Service.OpenAsync(f.Id, Guid.NewGuid(), f.Actor, false)).IsSuccess);
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }
    [TestMethod]
    [DataRow("tenant")]
    [DataRow("request")]
    public async Task Wrong_attachment_identity_never_opens_file(string kind)
    {
        var f = new Fixture(); var attachment = new EnterpriseRequestAttachmentRow(Guid.NewGuid(),
            kind == "tenant" ? Guid.NewGuid() : f.TenantId, kind == "request" ? Guid.NewGuid() : f.Id, f.FileId, "probe.txt", 1, DateTimeOffset.UtcNow);
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestAttachmentRow>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(attachment);
        Assert.IsFalse((await f.Service.OpenAsync(f.Id, attachment.Id, f.Actor, false)).IsSuccess);
        Assert.AreEqual(0, f.Files.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Parent_changed_during_file_open_disposes_stream_and_rejects_snapshot()
    {
        var f = new Fixture(); var attachment = new EnterpriseRequestAttachmentRow(Guid.NewGuid(), f.TenantId, f.Id, f.FileId, "probe.txt", 1, DateTimeOffset.UtcNow);
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestAttachmentRow>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(attachment);
        using var content = new MemoryStream([1]);
        f.Files.OpenReadyContentAsync("enterprise_request", f.Id, f.FileId, Arg.Any<CancellationToken>()).Returns(_ => {
            f.Parent = f.Parent with { Version = 2 }; return Result<TenantResourceFileContent>.Success(new(content, "application/octet-stream", "probe.txt")); });
        var result = await f.Service.OpenAsync(f.Id, attachment.Id, f.Actor, false);
        Assert.IsFalse(result.IsSuccess); Assert.IsFalse(content.CanRead);
    }
    [TestMethod]
    public async Task Compensation_failure_preserves_original_conflict_for_reconciliation()
    {
        var f = new Fixture(); f.Commands.ExecuteAsync(EnterpriseRequestAttachmentSql.AdvanceParent, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        f.Files.ReleaseAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("probe")));
        var result = await f.Upload(); Assert.IsFalse(result.IsSuccess); Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
    }
    private sealed class Fixture
    {
        internal readonly Guid Id = Guid.NewGuid(), Actor = Guid.NewGuid(), Unit = Guid.NewGuid(), TenantId = Guid.NewGuid(), FileId = Guid.NewGuid();
        internal readonly IQueryExecutor Queries = Substitute.For<IQueryExecutor>();
        internal readonly ICommandExecutor Commands = Substitute.For<ICommandExecutor>();
        internal readonly ICurrentTenant Tenant = Substitute.For<ICurrentTenant>();
        internal readonly IOrganizationOwnedEntityWriteAuthorizer Authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        internal readonly ITenantResourceFileStore Files = Substitute.For<ITenantResourceFileStore>();
        internal readonly RecordingDbTransactionCoordinator Coordinator = new();
        internal EnterpriseRequestRecord Parent;
        internal readonly EnterpriseRequestAttachmentService Service;
        internal Fixture()
        {
            Tenant.IsAvailable.Returns(true); Tenant.Id.Returns(TenantId);
            Parent = new(Id, TenantId, Unit, "REQ", "Request", "Draft", 1m, Actor, 1, DateTimeOffset.UtcNow, Actor, null, null, false, null, null);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(_ => Parent);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestAttachmentRow>(EnterpriseRequestAttachmentSql.FindUpload, Arg.Any<object?>(), Arg.Any<CancellationToken>())
                .Returns(new EnterpriseRequestAttachmentRow(Guid.NewGuid(), TenantId, Id, FileId, "valid.txt", 1, DateTimeOffset.UtcNow));
            Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Files.UploadAsync("enterprise_request", Id, Actor, Arg.Any<string>(), "application/octet-stream", Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(call => {
                Assert.IsFalse(Coordinator.HasTransaction); return Result<TenantResourceFileReference>.Success(new(FileId, 1, "hash")); });
            Files.ReleaseAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => { Assert.IsFalse(Coordinator.HasTransaction); return Task.CompletedTask; });
            var scopes = Substitute.For<IUserDataScopeResolver>(); scopes.ResolveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(new EffectiveUserDataScope(true, []));
            var filters = Substitute.For<IDataScopeSqlFilterBuilder>(); filters.BuildOrganizationUnitFilter(Arg.Any<EffectiveUserDataScope>(), "OrganizationUnitId", Actor).Returns(new DataScopeSqlFilter("1 = 1", new Dictionary<string, object?>()));
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.NewGuid());
            Service = new(new EnterpriseRequestQueryService(Queries, Options.Create(new DatabaseOptions()), scopes, filters), Queries, Commands,
                new DapperCommandTransaction(Coordinator), Tenant, Authorizer, Files, Substitute.For<IClock>(), NullLogger<EnterpriseRequestAttachmentService>.Instance);
        }
        internal async Task<Full.NET.Abstractions.Results.Result<Full.NET.Modules.EnterpriseRequest.Contracts.EnterpriseRequestAttachmentMutationResponse>> Upload(long version = 1, Guid? actor = null)
        { using var stream = new MemoryStream([1]); return await Service.UploadAsync(Id, version, actor ?? Actor, "valid.txt", stream, 1); }
    }
}

using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Files.Features.WorkflowReadCapabilities;
using Full.NET.Modules.Files.Persistence;
using Full.NET.Modules.Files.Storage;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Files;

/// <summary>能力读取绑定当前接收方、调用与期限；原文件身份只向原签发方开放。</summary>
[TestClass]
public sealed class WorkflowFileReadCapabilityTests
{
    [TestMethod]
    public Task Capability_revoked_while_storage_is_opening_does_not_deliver_bytes()=>VerifyOpenChangeAsync("revoked");

    [TestMethod]
    [DataRow("expired")][DataRow("inactive")][DataRow("scope")][DataRow("source")]
    [DataRow("storage")][DataRow("metadata")][DataRow("revision")][DataRow("missing")]
    public Task Changed_authority_or_metadata_after_storage_open_cannot_deliver_old_bytes(string scenario)=>VerifyOpenChangeAsync(scenario);

    private static async Task VerifyOpenChangeAsync(string scenario)
    {
        var data=Create();var stream=new TrackedStream();var reads=0;
        var changed=scenario switch {
            "revoked"=>data.Row with {StatusKey="revoked",RevokedAtUtc=data.Now,Revision=2},
            "source"=>data.Row with {SourceFileId=Guid.CreateVersion7()},
            "storage"=>data.Row with {StorageKey="changed/key"},
            "metadata"=>data.Row with {ContentHash=new string('b',64)},
            "revision"=>data.Row with {Revision=2},
            "missing"=>null,_=>data.Row
        };
        data.Query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindReceivedReadySqlServer,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(_=>++reads==1?data.Row:changed);
        data.Provider.OpenReadAsync("owned/key",Arg.Any<CancellationToken>()).Returns(_=>{
            if(scenario=="expired")data.Clock.UtcNow.Returns(data.Row.ExpiresAtUtc);
            if(scenario=="inactive")data.Tenants.IsActiveTenantAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(false);
            if(scenario=="scope")data.Tenant.SetHost();
            return Task.FromResult<Stream>(stream);
        });
        var opened=await data.Store.OpenAsync(data.Row.Id,data.Row.CallId);
        Assert.IsFalse(opened.IsSuccess);Assert.AreEqual(FilesErrorCodes.ReadCapabilityNotFound,opened.Error?.Code);
        Assert.AreEqual(2,reads);Assert.IsTrue(stream.Disposed);
    }

    [TestMethod]
    [DataRow("query")][DataRow("tenant")][DataRow("cancel")][DataRow("cancel_after_check")]
    public async Task Revalidation_failure_or_cancellation_releases_the_opened_stream(string scenario)
    {
        var data=Create();var stream=new TrackedStream();var reads=0;var authorityChecks=0;using var cancellation=new CancellationTokenSource();
        data.Query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindReceivedReadySqlServer,Arg.Any<object?>(),Arg.Any<CancellationToken>())
            .Returns(_=>{
                reads++;if(reads==2&&scenario=="query")throw new InvalidOperationException("fixture.query");
                if(reads==2&&scenario=="cancel_after_check")cancellation.Cancel();
                return data.Row;
            });
        data.Tenants.IsActiveTenantAsync(Arg.Any<Guid>(),Arg.Any<CancellationToken>()).Returns(_=>{
            if(++authorityChecks==2&&scenario=="tenant")throw new InvalidOperationException("fixture.tenant");return true;
        });
        data.Provider.OpenReadAsync("owned/key",Arg.Any<CancellationToken>()).Returns(_=>{if(scenario=="cancel")cancellation.Cancel();return Task.FromResult<Stream>(stream);});
        if(scenario.StartsWith("cancel",StringComparison.Ordinal))await Assert.ThrowsExactlyAsync<OperationCanceledException>(()=>data.Store.OpenAsync(data.Row.Id,data.Row.CallId,cancellation.Token));
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(()=>data.Store.OpenAsync(data.Row.Id,data.Row.CallId,cancellation.Token));
        Assert.IsTrue(stream.Disposed);
    }

    [TestMethod]
    public async Task Successful_open_transfers_one_live_stream_after_the_final_check()
    {
        var data=Create();var stream=new TrackedStream();data.Provider.OpenReadAsync("owned/key",Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream>(stream));
        var opened=await data.Store.OpenAsync(data.Row.Id,data.Row.CallId);Assert.IsTrue(opened.IsSuccess);Assert.IsFalse(stream.Disposed);
        await data.Query.Received(2).QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindReceivedReadySqlServer,Arg.Any<object?>(),Arg.Any<CancellationToken>());
        await data.Provider.Received(1).OpenReadAsync("owned/key",Arg.Any<CancellationToken>());
        await opened.Value!.Content.DisposeAsync();Assert.IsTrue(stream.Disposed);
    }
    [TestMethod]
    public async Task Recipient_reads_real_provider_bytes_without_exposing_the_source_file_identity()
    {
        var data = Create();
        var described = await data.Store.DescribeAsync(data.Row.Id, data.Row.CallId);
        Assert.IsNotNull(described); Assert.AreEqual(data.Row.Id, described.Id);
        var opened = await data.Store.OpenAsync(data.Row.Id, data.Row.CallId);
        Assert.IsTrue(opened.IsSuccess);
        await using var content = opened.Value!.Content;
        using var copy = new MemoryStream(); await content.CopyToAsync(copy);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, copy.ToArray());
        Assert.IsNull(await data.Store.ResolveIssuedAsync(data.Row.Id, data.Row.CallId));
    }

    [TestMethod]
    [DataRow("recipient")][DataRow("call")][DataRow("id")][DataRow("expired")][DataRow("revoked")]
    [DataRow("issuerScope")][DataRow("recipientScope")][DataRow("hash")][DataRow("inactive")]
    public async Task Untrusted_or_stale_rows_cannot_authorize_bytes_even_if_a_query_returns_them(string defect)
    {
        var data = Create(); var row = data.Row;
        row = defect switch
        {
            "recipient" => row with { RecipientTenantId = Guid.CreateVersion7(), RecipientScopeKey = "host" },
            "call" => row with { CallId = Guid.CreateVersion7() }, "id" => row with { Id = Guid.CreateVersion7() },
            "expired" => row with { ExpiresAtUtc = data.Now }, "revoked" => row with { StatusKey = "revoked", RevokedAtUtc = data.Now },
            "issuerScope" => row with { IssuerScopeKey = "tenant:foreign" }, "recipientScope" => row with { RecipientScopeKey = "tenant:foreign" },
            "hash" => row with { ContentHash = new string('X', 64) }, _ => row
        };
        data.Query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindReceivedReadySqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        if (defect == "inactive") data.Tenants.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.IsNull(await data.Store.DescribeAsync(data.Row.Id, data.Row.CallId));
        Assert.IsFalse((await data.Store.OpenAsync(data.Row.Id, data.Row.CallId)).IsSuccess);
        await data.Provider.DidNotReceive().OpenReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Issuer_can_resolve_only_its_own_reference_and_cannot_receive_its_own_capability()
    {
        var data = Create(); data.Tenant.SetHost();
        data.Query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindOwnReadySqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Row);
        var own = await data.Store.ResolveIssuedAsync(data.Row.Id, data.Row.CallId);
        Assert.AreEqual(data.Row.SourceFileId, own?.FileId); Assert.AreEqual(data.Row.OwnerReferenceId, own?.OwnerReferenceId);
        Assert.IsNull(await data.Store.DescribeAsync(data.Row.Id, data.Row.CallId));
    }

    [TestMethod]
    public async Task Foreign_business_transactions_are_rejected_before_queries_or_storage()
    {
        var data = Create(); data.State.HasTransaction.Returns(true); data.Query.ClearReceivedCalls();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.Store.DescribeAsync(data.Row.Id, data.Row.CallId));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.Store.OpenAsync(data.Row.Id, data.Row.CallId));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.Store.ResolveIssuedAsync(data.Row.Id, data.Row.CallId));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.Store.ReleaseIssuedAsync(data.Row.CallId));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => data.Store.IssueAsync(Issue(data)));
        Assert.AreEqual(0, data.Query.ReceivedCalls().Count()); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_local_possession_is_not_replaced_by_an_arbitrary_foreign_file_id()
    {
        var data = Create(); data.Tenant.SetHost();
        var result = await data.Store.IssueAsync(Issue(data));
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(FilesErrorCodes.ReadCapabilityNotFound, result.Error?.Code);
        await data.Commands.Received(1).ExecuteAsync(WorkflowFileReadCapabilitySql.InsertHost,
            Arg.Is<Dictionary<string, object?>>(p => p != null && (Guid)p["OwnerReferenceId"]! == data.Row.OwnerReferenceId), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Matching_issued_payload_replays_without_inserting_or_extending_expiry()
    {
        var data = Create(); data.Tenant.SetHost();
        data.Query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindIssued, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Row);
        data.Query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindOwnReadySqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(data.Row);
        Assert.IsTrue((await data.Store.IssueAsync(Issue(data))).IsSuccess);
        var conflict = await data.Store.IssueAsync(Issue(data) with { ExpiresAtUtc = data.Row.ExpiresAtUtc.AddMinutes(1) });
        Assert.AreEqual(FilesErrorCodes.ReadCapabilityConflict, conflict.Error?.Code);
        Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Release_is_bounded_and_never_deletes_original_storage()
    {
        var data = Create(); data.Tenant.SetHost(); var page = Enumerable.Range(0, 25).Select(_ => Guid.CreateVersion7()).ToArray();
        data.Query.QueryAsync<Guid>(WorkflowFileReadCapabilitySql.ListToReleaseSqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(page);
        Assert.IsFalse(await data.Store.ReleaseIssuedAsync(data.Row.CallId));
        await data.Commands.Received(1).ExecuteAsync(WorkflowFileReadCapabilitySql.RevokeIssued,
            Arg.Is<Dictionary<string, object?>>(p => p != null && (string)p["TenantScopeKey"]! == "host" && ((Guid[])p["Ids"]!).Length == 25), Arg.Any<CancellationToken>());
        await data.Provider.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("field")][DataRow("scope")][DataRow("direction")][DataRow("expired")][DataRow("empty")]
    public async Task Invalid_issue_is_rejected_before_persistence(string defect)
    {
        var data = Create(); data.Tenant.SetHost(); var request = Issue(data);
        request = defect switch { "field" => request with { FieldKey = "constructor" }, "scope" => request with { RecipientTenantId = null },
            "direction" => request with { DirectionKey = "delegate" }, "expired" => request with { ExpiresAtUtc = data.Now }, _ => request with { FileId = Guid.Empty } };
        data.Query.ClearReceivedCalls(); Assert.IsFalse((await data.Store.IssueAsync(request)).IsSuccess);
        Assert.AreEqual(0, data.Query.ReceivedCalls().Count()); Assert.AreEqual(0, data.Commands.ReceivedCalls().Count());
    }

    private static WorkflowFileReadCapabilityIssue Issue(Case data) => new(data.Row.CallId, "input", "files", data.Row.SourceFileId,
        data.Row.OwnerReferenceId, data.Row.RecipientTenantId, data.Row.ExpiresAtUtc);
    private static Case Create()
    {
        var now = WorkflowFileReadCapabilityPolicy.Microseconds(DateTimeOffset.UtcNow); var recipient = Guid.CreateVersion7();
        var row = new WorkflowFileReadCapabilityRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), null, "host", recipient,
            WorkflowFileReadCapabilityPolicy.ScopeKey(recipient), "input", "files", Guid.CreateVersion7(), Guid.CreateVersion7(), "file.png", "image/png", 3,
            new string('a', 64), "active", now.AddHours(1), now, null, 1, "local", "owned/key");
        var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new TenantContext(recipient, "recipient", "接收方"));
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var state = Substitute.For<IDataTransactionState>();
        var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(now); var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(Guid.CreateVersion7());
        var tenants = Substitute.For<IIdentityActiveTenantDirectory>(); tenants.IsActiveTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var provider = Substitute.For<IFileStorageProvider>(); provider.ProviderKey.Returns("local");
        provider.OpenReadAsync("owned/key", Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Stream>(new MemoryStream([1, 2, 3])));
        var registry = new FileStorageProviderRegistry([provider], Options.Create(new FileStorageOptions { DefaultProviderKey = "local" }));
        query.QuerySingleOrDefaultAsync<WorkflowFileReadCapabilityRecord>(WorkflowFileReadCapabilitySql.FindReceivedReadySqlServer, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var store = new WorkflowFileReadCapabilityStore(query, commands, tenant, state, clock, ids, tenants, registry,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));
        return new(store, row, now, tenant, query, commands, state, tenants, provider,clock);
    }
    private sealed class TrackedStream():MemoryStream([1,2,3])
    {
        public bool Disposed {get;private set;}
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
    private sealed record Case(WorkflowFileReadCapabilityStore Store, WorkflowFileReadCapabilityRecord Row, DateTimeOffset Now,
        CurrentTenantAccessor Tenant, IQueryExecutor Query, ICommandExecutor Commands, IDataTransactionState State,
        IIdentityActiveTenantDirectory Tenants, IFileStorageProvider Provider,IClock Clock);
}

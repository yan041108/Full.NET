using System.Security.Cryptography;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>签名准备与提交分离，文件归属、实际内容及证据身份都必须匹配。</summary>
[TestClass]
public sealed class WorkflowSignatureCoordinatorTests
{
    [TestMethod]
    public async Task Proxy_signature_preserves_delegator_and_actual_signer_independently()
    {
        var f = Fixture(); var original = Guid.CreateVersion7();
        f.Query.QuerySingleOrDefaultAsync<WorkflowTodoDelegationRecord>(WorkflowTodoDelegationSql.FindForProxy,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowTodoDelegationRecord(Guid.CreateVersion7(), f.Todo.Id, original, f.Actor, DateTimeOffset.UtcNow));
        var prepared = await f.Service.PrepareAsync(f.Todo, f.Instance, f.Actor, "approve", "proxy-sign", f.File, 2, "{}", default);
        Assert.IsTrue(prepared.IsSuccess); Assert.AreEqual(original, prepared.Value!.OriginalAssigneeUserId);
        Assert.AreEqual(f.Actor, prepared.Value.ActorUserId);
        Assert.IsTrue((await f.Service.WriteAsync(prepared.Value, f.Todo, f.Instance, f.Actor, "approve", "proxy-sign", f.File, 2, "{}", default)).IsSuccess);
        var forged = prepared.Value with { OriginalAssigneeUserId = f.Actor };
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureInvalid,
            (await f.Service.WriteAsync(forged, f.Todo, f.Instance, f.Actor, "approve", "proxy-sign", f.File, 2, "{}", default)).Error?.Code);
    }

    [TestMethod]
    public async Task Tenant_signature_uses_current_tenant_actor_resource_and_never_calls_host_ports()
    {
        var fixture = Fixture(tenantScope: true);
        var prepared = await fixture.Service.PrepareAsync(fixture.Todo, fixture.Instance, fixture.Actor,
            "approve", "tenant-signed", fixture.File, 2, "{}", default);
        Assert.IsTrue(prepared.IsSuccess);
        Assert.IsNotNull(prepared.Value);
        await fixture.Service.FinishAsync(prepared.Value, default);
        Assert.AreEqual(0, fixture.Claims.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.HostDescriptors.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.HostContents.ReceivedCalls().Count());
        await fixture.TenantDescriptors.Received(1).GetReadyDescriptorAsync("workflow", fixture.Actor, fixture.File, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Foreign_tenant_instance_is_rejected_before_any_file_lookup()
    {
        var fixture = Fixture(tenantScope: true);
        var prepared = await fixture.Service.PrepareAsync(fixture.Todo, fixture.Instance with { TenantScopeKey = "tenant:" + Guid.CreateVersion7().ToString("N") },
            fixture.Actor, "approve", "foreign-signed", fixture.File, 2, "{}", default);
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureInvalid, prepared.Error?.Code);
        Assert.AreEqual(0, fixture.TenantDescriptors.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.HostDescriptors.ReceivedCalls().Count());
    }
    [TestMethod]
    public async Task Signature_claim_is_prepared_outside_local_write_and_confirmed_by_its_own_evidence()
    {
        var fixture = Fixture();
        var prepared = await fixture.Service.PrepareAsync(fixture.Todo, fixture.Instance, fixture.Actor,
            "approve", "signed", fixture.File, 2, "{}", default);
        Assert.IsTrue(prepared.IsSuccess);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        var calls = fixture.Claims.ReceivedCalls().Count();
        var written = await fixture.Service.WriteAsync(prepared.Value, fixture.Todo, fixture.Instance, fixture.Actor,
            "approve", "signed", fixture.File, 2, "{}", default);
        Assert.IsTrue(written.IsSuccess);
        Assert.AreEqual(calls, fixture.Claims.ReceivedCalls().Count());
        await fixture.Command.Received(1).ExecuteAsync(WorkflowSignatureSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        fixture.Query.QuerySingleOrDefaultAsync<int>(WorkflowSignatureSql.Exists, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        await fixture.Service.FinishAsync(prepared.Value, default);
        await fixture.Claims.Received(1).ConfirmAsync(HostFileReferenceClaimIdempotencyKeys.WorkflowActionSignature(prepared.Value!.Id), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Signature_cannot_be_reused_for_another_submission_or_replay_preparation()
    {
        var fixture = Fixture();
        var prepared = await fixture.Service.PrepareAsync(fixture.Todo, fixture.Instance, fixture.Actor,
            "approve", "signed", fixture.File, 2, "{}", default);
        var changed = await fixture.Service.WriteAsync(prepared.Value, fixture.Todo, fixture.Instance, fixture.Actor,
            "approve", "signed", fixture.File, 3, "{}", default);
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureInvalid, changed.Error?.Code);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        await fixture.Service.FinishAsync(prepared.Value, default);
        await fixture.Claims.Received(1).ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.WorkflowActionSignature(prepared.Value!.Id), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("foreign-owner")]
    [DataRow("fake-header")]
    [DataRow("wrong-hash")]
    [DataRow("wrong-size")]
    public async Task Invalid_file_never_creates_a_claim(string failure)
    {
        var fixture = Fixture(failure);
        var prepared = await fixture.Service.PrepareAsync(fixture.Todo, fixture.Instance, fixture.Actor,
            "approve", "signed", fixture.File, 2, "{}", default);
        Assert.AreEqual(WorkflowErrorCodes.TodoSignatureInvalid, prepared.Error?.Code);
        Assert.AreEqual(0, fixture.Claims.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Vote_signature_pins_the_actual_ballot_form_and_choice_payload()
    {
        var f = Fixture(); var form = Guid.CreateVersion7();
        var todo = f.Todo with { NodeTypeKey = "human.vote" };
        f.Query.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowVoteFormBindingRecord(Guid.CreateVersion7(), form, "{}", "hash", "active"));
        const string payload = """{"choiceKey":"yes","values":{}}""";
        var prepared = await f.Service.PrepareForFormAsync(todo, f.Instance, f.Actor, "vote", "ballot-signed", f.File, form, 2, payload, default);
        Assert.IsTrue(prepared.IsSuccess); Assert.AreEqual(form, prepared.Value!.FormVersionId);
        var wrong = await f.Service.WriteAsync(prepared.Value, todo, f.Instance, f.Actor, "vote", "ballot-signed", f.File, 2, payload, default);
        Assert.IsFalse(wrong.IsSuccess); Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
        var written = await f.Service.WriteForFormAsync(prepared.Value, todo, f.Instance, f.Actor, "vote", "ballot-signed", f.File, form, 2, payload, default);
        Assert.IsTrue(written.IsSuccess);
        await f.Command.Received(1).ExecuteAsync(WorkflowSignatureSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("disabled")]
    [DataRow("other-form")]
    public async Task Unbound_ballot_form_never_reads_files_or_claims(string failure)
    {
        var f = Fixture(); var form = Guid.CreateVersion7(); var todo = f.Todo with { NodeTypeKey = "human.vote" };
        if (failure != "missing") f.Query.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowVoteFormBindingRecord(Guid.CreateVersion7(), failure == "other-form" ? Guid.CreateVersion7() : form, "{}", "hash", failure == "disabled" ? "disabled" : "active"));
        var result = await f.Service.PrepareForFormAsync(todo, f.Instance, f.Actor, "vote", "blocked", f.File, form, 2, "{}", default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.HostDescriptors.ReceivedCalls().Count()); Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Vote_cannot_silently_fall_back_to_the_shared_main_form()
    {
        var f = Fixture(); var todo = f.Todo with { NodeTypeKey = "human.vote" };
        f.Query.QuerySingleOrDefaultAsync<WorkflowVoteFormBindingRecord>(WorkflowVoteSql.FindFormBinding, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowVoteFormBindingRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), "{}", "hash", "active"));
        var result = await f.Service.PrepareAsync(todo, f.Instance, f.Actor, "vote", "wrong-main", f.File, 2, "{}", default);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
    }

    private static SignatureFixture Fixture(string? failure = null, bool tenantScope = false)
    {
        var actor = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var instanceId = Guid.CreateVersion7();
        var bytes = failure == "fake-header" ? new byte[8] : new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        var descriptor = Substitute.For<IHostFileDescriptorReader>(); var content = Substitute.For<IHostFileContentReader>();
        var claims = Substitute.For<IHostFileReferenceClaimService>(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        descriptor.GetReadyDescriptorAsync(file, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(file, "signature.png", "image/png",
            failure == "wrong-size" ? 9 : bytes.Length, failure == "wrong-hash" ? "invalid" : Convert.ToHexStringLower(SHA256.HashData(bytes)),
            failure == "foreign-owner" ? Guid.CreateVersion7() : actor));
        content.OpenReadyContentAsync(file, Arg.Any<CancellationToken>()).Returns(_ => Result<HostFileContent>.Success(new(new MemoryStream(bytes), "image/png", "signature.png")));
        var claimResult = Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(), "pending", new(file, bytes.Length, null)));
        claims.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claimResult);
        claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(claimResult);
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var todo = new WorkflowTodoRuntimeRecord(Guid.CreateVersion7(), instanceId, Guid.CreateVersion7(), actor, "active", DateTimeOffset.UtcNow,
            null, null, 1, "approval", 1, null, null, null, null, null, null);
        var tenantId = tenantScope ? Guid.CreateVersion7() : (Guid?)null;
        var instance = new WorkflowInstanceRecord(instanceId, tenantId, tenantScope ? "tenant" : "host", tenantScope ? $"tenant:{tenantId:N}" : "host", Guid.CreateVersion7(), Guid.CreateVersion7(), "test", "one",
            null, "active", 1, actor, DateTimeOffset.UtcNow, null, null, null, null, null, null);
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(!tenantScope); tenant.IsAvailable.Returns(tenantScope); tenant.Id.Returns(tenantId);
        var tenantDescriptors = Substitute.For<ITenantResourceFileDescriptorReader>(); var tenantFiles = Substitute.For<ITenantResourceFileStore>();
        tenantDescriptors.GetReadyDescriptorAsync("workflow", actor, file, Arg.Any<CancellationToken>()).Returns(
            new TenantResourceFileDescriptor(file, "signature.png", "image/png", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), actor));
        tenantFiles.OpenReadyContentAsync("workflow", actor, file, Arg.Any<CancellationToken>()).Returns(_ =>
            Result<TenantResourceFileContent>.Success(new(new MemoryStream(bytes), "image/png", "signature.png")));
        return new(new(descriptor, content, claims, query, command, ids, Substitute.For<IClock>(), NullLogger<WorkflowSignatureCoordinator>.Instance,
                tenant, tenantDescriptors, tenantFiles),
            query, command, claims, actor, file, todo, instance, descriptor, content, tenantDescriptors);
    }

    private sealed record SignatureFixture(WorkflowSignatureCoordinator Service, IQueryExecutor Query, ICommandExecutor Command,
        IHostFileReferenceClaimService Claims, Guid Actor, Guid File, WorkflowTodoRuntimeRecord Todo, WorkflowInstanceRecord Instance,
        IHostFileDescriptorReader HostDescriptors, IHostFileContentReader HostContents, ITenantResourceFileDescriptorReader TenantDescriptors);
}

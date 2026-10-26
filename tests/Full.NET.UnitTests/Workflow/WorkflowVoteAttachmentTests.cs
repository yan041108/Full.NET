using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>个人票据的文件持有、实际输出与租户资源归属都必须独立授权。</summary>
[TestClass]
public sealed class WorkflowVoteAttachmentTests
{
    [TestMethod]
    public async Task Concurrent_preparations_confirm_only_their_own_committed_reference()
    {
        var f = Create(); var first = (await Prepare(f)).Value!; var second = (await Prepare(f)).Value!;
        Assert.AreNotEqual(first.Attachments[0].Id, second.Attachments[0].Id);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
        var external = f.Claims.ReceivedCalls().Count() + f.Descriptors.ReceivedCalls().Count();
        Assert.IsTrue((await f.Service.WriteAsync(first, f.Ballot, f.Instance, f.Actor, f.Scope, f.Schema, f.Values, "hash", default)).IsSuccess);
        Assert.AreEqual(external, f.Claims.ReceivedCalls().Count() + f.Descriptors.ReceivedCalls().Count());
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowVoteAttachmentSql.ExistsForProbe, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => ((IReadOnlyDictionary<string, object?>)call[1]!)["ReferenceId"]!.Equals(first.Attachments[0].Id) ? 1 : 0);
        await f.Service.FinishAsync(first, default); await f.Service.FinishAsync(second, default);
        await f.Claims.Received(1).ConfirmAsync(HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(first.Attachments[0].Id, f.File), Arg.Any<CancellationToken>());
        await f.Claims.Received(1).ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(second.Attachments[0].Id, f.File), Arg.Any<CancellationToken>());
        await f.Commands.Received(1).ExecuteAsync(WorkflowVoteAttachmentSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        await f.Commands.DidNotReceive().ExecuteAsync(WorkflowFormSubmissionAttachmentSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Foreign_upload_is_rejected_before_any_claim_or_write(bool tenant)
    {
        var f = Create(tenant); var foreign = Guid.CreateVersion7();
        f.Descriptors.GetReadyDescriptorAsync(f.File, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(f.File, "proof.png", "image/png", 100, null, foreign));
        f.TenantDescriptors.GetReadyDescriptorAsync("workflow", f.Actor, f.File, Arg.Any<CancellationToken>()).Returns(new TenantResourceFileDescriptor(f.File, "proof.png", "image/png", 100, "hash", foreign));
        var result = await Prepare(f);
        Assert.AreEqual(WorkflowErrorCodes.FormAttachmentInvalid, result.Error?.Code);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count()); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Tenant_resource_keeps_actual_owner_without_host_fallback()
    {
        var f = Create(true); var plan = (await Prepare(f)).Value!;
        Assert.AreEqual(f.Actor, plan.Attachments[0].OwnerResourceId);
        Assert.IsTrue((await f.Service.WriteAsync(plan, f.Ballot, f.Instance, f.Actor, f.Scope, f.Schema, f.Values, "hash", default)).IsSuccess);
        await f.Service.FinishAsync(plan, default);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count()); Assert.AreEqual(0, f.Descriptors.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("ballot")]
    [DataRow("revision")]
    [DataRow("choiceHash")]
    public async Task Preparation_cannot_be_rebound_to_another_ballot_revision_or_choice(string changed)
    {
        var f = Create(); var plan = (await Prepare(f)).Value!;
        var stale = changed == "ballot" ? plan with { BallotId = Guid.CreateVersion7() }
            : changed == "revision" ? plan with { ExpectedRevision = 2 } : plan with { EvidenceHash = "other-choice" };
        Assert.IsFalse((await f.Service.WriteAsync(stale, f.Ballot, f.Instance, f.Actor, f.Scope, f.Schema, f.Values, "hash", default)).IsSuccess);
        Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Attachment_read_requires_file_in_owned_private_output(bool ownOutput)
    {
        var f = Create(); var contents = Substitute.For<IHostFileContentReader>();
        contents.OpenReadyContentAsync(f.File, Arg.Any<CancellationToken>()).Returns(Result<HostFileContent>.Success(new(new MemoryStream(), "image/png", "proof.png")));
        var schema = JsonSerializer.Serialize(f.Schema, Full.NET.Modules.Workflow.Serialization.WorkflowJsonSerializerContext.Default.WorkflowFormSchema);
        var values = ownOutput ? f.Values.GetRawText() : "{}";
        f.Query.QueryAsync<WorkflowVoteAttachmentViewRecord>(WorkflowVoteAttachmentSql.ReadOwnedSqlServer,
            Arg.Is<object?>(p => ((IReadOnlyDictionary<string, object?>)p!)["ActorUserId"]!.Equals(f.Actor)), Arg.Any<CancellationToken>())
            .Returns([new(null, schema, "{\"choiceKey\":\"yes\",\"values\":" + values + "}")]);
        var result = await new WorkflowFormAttachmentAccessService(f.Query, contents, Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }),
            Substitute.For<ITenantResourceFileStore>()).OpenInstanceAttachmentAsync(f.Instance, f.File, f.Actor, "host");
        Assert.AreEqual(ownOutput, result.IsSuccess);
        Assert.AreEqual(ownOutput ? 1 : 0, contents.ReceivedCalls().Count());
    }

    private static Task<Result<WorkflowVoteAttachmentPlan>> Prepare(Fixture f) => f.Service.PrepareAsync(f.Ballot, f.Instance,
        f.Actor, f.Scope, f.Schema, f.Values, "hash", default);
    private static Fixture Create(bool tenantScope = false)
    {
        var actor = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var instance = Guid.CreateVersion7();
        var tenant = new CurrentTenantAccessor(); if (tenantScope) tenant.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme")); else tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>();
        commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var descriptors = Substitute.For<IHostFileDescriptorReader>(); var tenantDescriptors = Substitute.For<ITenantResourceFileDescriptorReader>();
        descriptors.GetReadyDescriptorAsync(file, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(file, "proof.png", "image/png", 100, null, actor));
        tenantDescriptors.GetReadyDescriptorAsync("workflow", actor, file, Arg.Any<CancellationToken>()).Returns(new TenantResourceFileDescriptor(file, "proof.png", "image/png", 100, "hash", actor));
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        var claim = Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(), "pending", new(file, 100, null)));
        claims.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claim);
        claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(claim);
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("document", "attachment", false, new Dictionary<string, JsonElement> {
            ["maxCount"] = JsonSerializer.SerializeToElement(1), ["maxSizeBytes"] = JsonSerializer.SerializeToElement(1024),
            ["allowedExtensions"] = JsonSerializer.SerializeToElement(new[] { "png" }) })])]);
        var values = JsonSerializer.SerializeToElement(new Dictionary<string, Guid[]> { ["document"] = [file] });
        return new(new(claims, descriptors, tenantDescriptors, query, commands, tenant, Substitute.For<IClock>(), ids,
            NullLogger<WorkflowVoteAttachmentCoordinator>.Instance), query, commands, claims, descriptors, tenantDescriptors,
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), actor, "pending", null, "{}", "{}", 1),
            instance, actor, file, WorkflowManagementScope.Resolve(tenant).TenantScopeKey, schema, values);
    }
    private sealed record Fixture(WorkflowVoteAttachmentCoordinator Service, IQueryExecutor Query, ICommandExecutor Commands,
        IHostFileReferenceClaimService Claims, IHostFileDescriptorReader Descriptors, ITenantResourceFileDescriptorReader TenantDescriptors,
        WorkflowVoteBallotRecord Ballot, Guid Instance, Guid Actor, Guid File, string Scope, WorkflowFormSchema Schema, JsonElement Values);
}

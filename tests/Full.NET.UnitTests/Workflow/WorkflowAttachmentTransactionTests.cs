using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>附件准备、Workflow 本地追加和 Files 确认分离；竞争者不能释放他人的持有。</summary>
[TestClass]
public sealed class WorkflowAttachmentTransactionTests
{
    [TestMethod]
    public async Task Two_branch_preparations_of_same_file_consume_only_the_committed_reference()
    {
        var f = Fixture(); var previous = Values(); var next = Values(f.File);
        var first = (await PrepareAsync(f, previous, next)).Value!;
        var second = (await PrepareAsync(f, previous, next)).Value!;
        Assert.AreNotEqual(first.Attachments[0].Id, second.Attachments[0].Id);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
        var externalCalls = f.Claims.ReceivedCalls().Count() + f.Descriptors.ReceivedCalls().Count();
        Assert.IsTrue((await WriteAsync(f, first, previous, next)).IsSuccess);
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForFieldFile,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        Assert.IsTrue((await WriteAsync(f, second, previous, next)).IsSuccess);
        Assert.AreEqual(externalCalls, f.Claims.ReceivedCalls().Count() + f.Descriptors.ReceivedCalls().Count());
        await f.Command.Received(1).ExecuteAsync(WorkflowFormSubmissionAttachmentSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>());
        f.Query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForProbe, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => ((IReadOnlyDictionary<string, object?>)call[1]!)["ReferenceId"]!.Equals(first.Attachments[0].Id) ? 1 : 0);
        await f.Service.FinishAsync(first, default); await f.Service.FinishAsync(second, default);
        await f.Claims.Received(1).ConfirmAsync(HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(first.Attachments[0].Id, f.File), Arg.Any<CancellationToken>());
        await f.Claims.Received(1).ReleaseAsync(HostFileReferenceClaimIdempotencyKeys.WorkflowFormSubmissionAttachment(second.Attachments[0].Id, f.File), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Readding_hidden_historical_file_does_not_gain_authority_from_old_reference()
    {
        var f = Fixture();
        f.Descriptors.GetReadyDescriptorAsync(f.File, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(f.File, "file.png", "image/png", 100, null, Guid.CreateVersion7()));
        f.Query.QueryAsync<WorkflowAttachmentReferenceRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowAttachmentReferenceRecord(Guid.CreateVersion7(), "document", f.File, null) });
        var result = await PrepareAsync(f, Values(), Values(f.File));
        Assert.AreEqual(WorkflowErrorCodes.FormAttachmentInvalid, result.Error?.Code);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count()); Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Stale_or_missing_preparation_cannot_call_files_inside_local_write()
    {
        var f = Fixture(); var previous = Values(); var next = Values(f.File);
        var plan = (await PrepareAsync(f, previous, next)).Value!;
        var calls = f.Claims.ReceivedCalls().Count();
        var stale = await f.Service.WritePreparedAsync(plan, f.Actor, f.Submission with { Revision = 2 }, f.Submission.InstanceId,
            f.Scope, f.Schema, previous, next, JsonSerializer.Serialize(next), default);
        Assert.AreEqual(WorkflowErrorCodes.FormAttachmentInvalid, stale.Error?.Code);
        var missing = await WriteAsync(f, null, previous, next);
        Assert.AreEqual(WorkflowErrorCodes.FormAttachmentInvalid, missing.Error?.Code);
        Assert.AreEqual(calls, f.Claims.ReceivedCalls().Count()); Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Removing_current_value_retains_history_fact_and_file_protection()
    {
        var f = Fixture(); var previous = Values(f.File); var next = Values();
        var plan = (await PrepareAsync(f, previous, next)).Value!;
        Assert.IsTrue((await WriteAsync(f, plan, previous, next)).IsSuccess);
        await f.Service.FinishAsync(plan, default);
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count()); Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Descriptors.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Tenant_file_ownership_is_prepared_without_host_ports_and_written_as_local_resource_fact()
    {
        var f = Fixture(tenantScope: true); var previous = Values(); var next = Values(f.File);
        var plan = (await PrepareAsync(f, previous, next)).Value!;
        Assert.AreEqual(f.Actor, plan.Attachments[0].OwnerResourceId);
        Assert.IsTrue((await WriteAsync(f, plan, previous, next)).IsSuccess);
        await f.Service.FinishAsync(plan, default);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count()); Assert.AreEqual(0, f.Descriptors.ReceivedCalls().Count());
        await f.TenantDescriptors.Received(1).GetReadyDescriptorAsync("workflow", f.Actor, f.File, Arg.Any<CancellationToken>());
    }

    private static Task<Result<WorkflowAttachmentPlan>> PrepareAsync(AttachmentFixture f, Dictionary<string, JsonElement> previous,
        Dictionary<string, JsonElement> next) => f.Service.PrepareAsync(f.Actor, f.Submission.Id, f.Submission.InstanceId,
            f.Scope, f.Schema, previous, next, new HashSet<Guid>(), f.Submission.Revision, JsonSerializer.Serialize(next), default);

    [TestMethod]
    public async Task Mapped_foreign_uploaded_file_requires_exact_source_field_and_scope_before_claim()
    {
        var f = Fixture(); var parent = Guid.CreateVersion7(); var next = Values(f.File);
        f.Descriptors.GetReadyDescriptorAsync(f.File, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(f.File, "file.png", "image/png", 100, null, Guid.CreateVersion7()));
        var sources = new Dictionary<Guid, WorkflowAttachmentReuseSource> { [f.File] = new(parent, "source") };
        var denied = await f.Service.PrepareInitialAsync(f.Actor, f.Submission.Id, f.Submission.InstanceId, f.Scope, f.Schema, next, default, sources);
        Assert.IsFalse(denied.IsSuccess); Assert.AreEqual(0, f.Claims.ReceivedCalls().Count());
        f.Query.QuerySingleOrDefaultAsync<WorkflowMappedAttachmentFileOwnerRecord>(WorkflowFormSubmissionAttachmentSql.FindMappedFileOwner,
            Arg.Is<object?>(value => value is IReadOnlyDictionary<string, object?> && ((IReadOnlyDictionary<string, object?>)value)["InstanceId"]!.Equals(parent) &&
                ((IReadOnlyDictionary<string, object?>)value)["FieldKey"]!.Equals("source") && ((IReadOnlyDictionary<string, object?>)value)["TenantScopeKey"]!.Equals("host")), Arg.Any<CancellationToken>()).Returns(new WorkflowMappedAttachmentFileOwnerRecord("source", null));
        var accepted = await f.Service.PrepareInitialAsync(f.Actor, f.Submission.Id, f.Submission.InstanceId, f.Scope, f.Schema, next, default, sources);
        Assert.IsTrue(accepted.IsSuccess); Assert.AreEqual(1, f.Claims.ReceivedCalls().Count());
        Assert.AreEqual(0, f.Command.ReceivedCalls().Count());
        f.Query.QuerySingleOrDefaultAsync<WorkflowMappedAttachmentFileOwnerRecord>(WorkflowFormSubmissionAttachmentSql.FindMappedFileOwner,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowMappedAttachmentFileOwnerRecord("Source", null));
        var wrongCase = await f.Service.PrepareInitialAsync(f.Actor, f.Submission.Id, f.Submission.InstanceId, f.Scope, f.Schema, next, default, sources);
        Assert.IsFalse(wrongCase.IsSuccess); Assert.AreEqual(1, f.Claims.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Mapped_tenant_file_keeps_the_original_resource_owner_without_using_host_claims()
    {
        var f = Fixture(true); var owner = Guid.CreateVersion7();
        f.TenantDescriptors.GetReadyDescriptorAsync("workflow", f.Actor, f.File, Arg.Any<CancellationToken>()).Returns((TenantResourceFileDescriptor?)null);
        f.TenantDescriptors.GetReadyDescriptorAsync("workflow", owner, f.File, Arg.Any<CancellationToken>()).Returns(new TenantResourceFileDescriptor(f.File, "file.png", "image/png", 100, "hash", owner));
        f.Query.QuerySingleOrDefaultAsync<WorkflowMappedAttachmentFileOwnerRecord>(WorkflowFormSubmissionAttachmentSql.FindMappedFileOwner, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(new WorkflowMappedAttachmentFileOwnerRecord("source", owner));
        var plan = await f.Service.PrepareInitialAsync(f.Actor, f.Submission.Id, f.Submission.InstanceId, f.Scope, f.Schema, Values(f.File), default,
            new Dictionary<Guid, WorkflowAttachmentReuseSource> { [f.File] = new(Guid.CreateVersion7(), "source") });
        Assert.IsTrue(plan.IsSuccess); Assert.AreEqual(owner, plan.Value!.Attachments.Single().OwnerResourceId);
        Assert.AreEqual(0, f.Claims.ReceivedCalls().Count()); Assert.AreEqual(0, f.Descriptors.ReceivedCalls().Count());
    }

    private static Task<Result<bool>> WriteAsync(AttachmentFixture f, WorkflowAttachmentPlan? plan, Dictionary<string, JsonElement> previous,
        Dictionary<string, JsonElement> next) => f.Service.WritePreparedAsync(plan, f.Actor, f.Submission, f.Submission.InstanceId,
            f.Scope, f.Schema, previous, next, JsonSerializer.Serialize(next), default);

    private static Dictionary<string, JsonElement> Values(Guid? file = null) => new(StringComparer.Ordinal)
        { ["document"] = JsonSerializer.SerializeToElement(file is { } id ? new[] { id } : Array.Empty<Guid>()) };

    private static AttachmentFixture Fixture(bool tenantScope = false)
    {
        var actor = Guid.CreateVersion7(); var file = Guid.CreateVersion7(); var tenantId = Guid.CreateVersion7();
        var scope = tenantScope ? $"tenant:{tenantId:N}" : "host";
        var tenant = new CurrentTenantAccessor(); if (tenantScope) tenant.SetTenant(new(tenantId, "acme", "Acme")); else tenant.SetHost();
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QueryAsync<WorkflowAttachmentReferenceRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowAttachmentReferenceRecord>());
        var descriptors = Substitute.For<IHostFileDescriptorReader>(); var tenantDescriptors = Substitute.For<ITenantResourceFileDescriptorReader>();
        descriptors.GetReadyDescriptorAsync(file, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(file, "file.png", "image/png", 100, null, actor));
        tenantDescriptors.GetReadyDescriptorAsync("workflow", actor, file, Arg.Any<CancellationToken>()).Returns(new TenantResourceFileDescriptor(file, "file.png", "image/png", 100, "hash", actor));
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        var result = Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(), "pending", new(file, 100, null)));
        claims.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(), Arg.Any<CancellationToken>()).Returns(result);
        claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("document", "attachment", false, new Dictionary<string, JsonElement> {
            ["maxCount"] = JsonSerializer.SerializeToElement(1), ["maxSizeBytes"] = JsonSerializer.SerializeToElement(1024),
            ["allowedExtensions"] = JsonSerializer.SerializeToElement(new[] { "png" }) })])]);
        var submission = new WorkflowFormSubmissionRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "{}", "Internal", 1, actor, DateTimeOffset.UtcNow);
        return new(new(claims, descriptors, query, command, Substitute.For<IClock>(), ids,
            NullLogger<WorkflowFormAttachmentCoordinator>.Instance, tenant, tenantDescriptors), actor, file, scope, schema, submission, query, command, claims, descriptors, tenantDescriptors);
    }

    private sealed record AttachmentFixture(WorkflowFormAttachmentCoordinator Service, Guid Actor, Guid File, string Scope,
        WorkflowFormSchema Schema, WorkflowFormSubmissionRecord Submission, IQueryExecutor Query, ICommandExecutor Command,
        IHostFileReferenceClaimService Claims, IHostFileDescriptorReader Descriptors, ITenantResourceFileDescriptorReader TenantDescriptors);
}

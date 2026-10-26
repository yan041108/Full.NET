using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>启动附件的预留、本地事实和确认分开执行，未知提交结果不得误释放已持有文件。</summary>
[TestClass]
public sealed class WorkflowInitialAttachmentPreparationTests
{
    [TestMethod]
    public async Task Preparation_and_confirmation_surround_a_workflow_only_write()
    {
        var actor = Guid.CreateVersion7(); var file = Guid.CreateVersion7();
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        var reader = Substitute.For<IHostFileDescriptorReader>();
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QueryAsync<WorkflowAttachmentReferenceRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<WorkflowAttachmentReferenceRecord>());
        var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
        var tenant = Substitute.For<ICurrentTenant>(); tenant.IsHost.Returns(true);
        reader.GetReadyDescriptorAsync(file, Arg.Any<CancellationToken>()).Returns(new HostFileDescriptor(file, "evidence.pdf", "application/pdf", 100, null, actor));
        var claimResult = Result<HostFileReferenceClaimResult>.Success(new(Guid.CreateVersion7(), "pending", new(file, 100, null)));
        claims.ClaimAsync(Arg.Any<HostFileReferenceClaimRequest>(), Arg.Any<CancellationToken>()).Returns(claimResult);
        claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(claimResult);
        var coordinator = new WorkflowFormAttachmentCoordinator(claims, reader, query, command,
            Substitute.For<IClock>(), ids, NullLogger<WorkflowFormAttachmentCoordinator>.Instance,
            tenant, Substitute.For<ITenantResourceFileDescriptorReader>());
        var schema = new WorkflowFormSchema(1, 1, [new("main", [new("evidence", "attachment", false,
            new Dictionary<string, JsonElement> { ["maxCount"] = JsonSerializer.SerializeToElement(1),
                ["maxSizeBytes"] = JsonSerializer.SerializeToElement(1024), ["allowedExtensions"] = JsonSerializer.SerializeToElement(new[] { "pdf" }) })])]);
        var plan = await coordinator.PrepareInitialAsync(actor, Guid.CreateVersion7(), Guid.CreateVersion7(), "host", schema,
            new Dictionary<string, JsonElement> { ["evidence"] = JsonSerializer.SerializeToElement(new[] { file }) }, default);
        Assert.IsTrue(plan.IsSuccess);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
        var externalCalls = claims.ReceivedCalls().Count() + reader.ReceivedCalls().Count();
        await coordinator.WriteInitialAsync(plan.Value!, default);
        Assert.AreEqual(externalCalls, claims.ReceivedCalls().Count() + reader.ReceivedCalls().Count());
        await command.Received(1).ExecuteAsync(WorkflowFormSubmissionAttachmentSql.Insert,
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
        query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForProbe, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        await coordinator.FinishAsync(plan.Value!, default);
        await claims.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await claims.DidNotReceive().ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(0)]
    public async Task Unknown_commit_uses_authoritative_reference_before_confirm_or_release(int exists)
    {
        var claims = Substitute.For<IHostFileReferenceClaimService>();
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForProbe,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(exists);
        claims.ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<HostFileReferenceClaimResult>.Success(
            new(Guid.CreateVersion7(), "active", new(Guid.CreateVersion7(), 100, null))));
        var coordinator = new WorkflowFormAttachmentCoordinator(claims, Substitute.For<IHostFileDescriptorReader>(),
            query, Substitute.For<ICommandExecutor>(), Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
            NullLogger<WorkflowFormAttachmentCoordinator>.Instance, Substitute.For<ICurrentTenant>(), Substitute.For<ITenantResourceFileDescriptorReader>());
        claims.ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
        await coordinator.FinishAsync(new(Guid.CreateVersion7(), Guid.CreateVersion7(), "host", Guid.CreateVersion7(), 1, "hash",
            [new(Guid.CreateVersion7(), "evidence", Guid.CreateVersion7(), null)]), default);
        await claims.Received(exists).ConfirmAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await claims.Received(1 - exists).ReleaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}

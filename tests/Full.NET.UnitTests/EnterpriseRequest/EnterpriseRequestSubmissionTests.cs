using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

/// <summary>提交前独立校验所有者与组织权限；启动失败保留可重放的稳定意图。</summary>
[TestClass]
public sealed class EnterpriseRequestSubmissionTests
{
    [TestMethod]
    public async Task Non_owner_and_out_of_scope_applicant_cannot_change_status()
    {
        var tenant = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenant).SetTenant(new(Guid.NewGuid(), "t", "T"));
        var row = new EnterpriseRequestRecord(Guid.NewGuid(), tenant.Id!.Value, Guid.NewGuid(), "REQ", "Title", "draft", 1,
            Guid.NewGuid(), 1, DateTimeOffset.UtcNow, Guid.NewGuid(), null, null, false, null, null);
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(EnterpriseRequestSql.FindByIdStatement, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(row);
        var command = Substitute.For<ICommandExecutor>(); var outbox = Substitute.For<IOutboxWriter>();
        var authorizer = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        authorizer.EnsureCanWriteAsync(tenant.Id.Value, row.OrganizationUnitId, row.ApplicantUserId, Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(false));
        var service = new SubmitEnterpriseRequestForApprovalService(query, command, Substitute.For<IClock>(), tenant,
            Substitute.For<IWorkflowPublishedDefinitionDirectory>(), authorizer, Substitute.For<ICommandTransaction>(), outbox);
        Assert.AreEqual(ErrorType.Forbidden, (await service.SubmitAsync(row.Id, Guid.NewGuid())).Error!.Type);
        Assert.IsFalse(authorizer.ReceivedCalls().Any());
        Assert.AreEqual(ErrorType.Forbidden, (await service.SubmitAsync(row.Id, row.ApplicantUserId)).Error!.Type);
        Assert.IsFalse(command.ReceivedCalls().Any());
        Assert.IsFalse(outbox.ReceivedCalls().Any());
    }

    [TestMethod]
    public async Task Startup_retry_preserves_definition_and_idempotency_key_and_rejects_other_tenant()
    {
        var tenant = new CurrentTenantAccessor(); ((ICurrentTenantContextWriter)tenant).SetTenant(new(Guid.NewGuid(), "t", "T"));
        var intent = new EnterpriseRequestSubmitted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "Title");
        var serializer = Substitute.For<IIntegrationEventSerializer>();
        serializer.Deserialize<EnterpriseRequestSubmitted>(Arg.Any<ReadOnlyMemory<byte>>()).Returns(intent);
        var starter = Substitute.For<IWorkflowInstanceStarter>();
        starter.StartAsync(intent.ActorUserId, Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<WorkflowInstanceLifecycleResult>.Failure(new Error("workflow.retry", "retry", ErrorType.Conflict)),
                Result<WorkflowInstanceLifecycleResult>.Success(new(Guid.NewGuid(), "active", 1)));
        Assert.IsTrue(Full.NET.Messaging.Abstractions.MessagingNames.MessageTypePattern.IsMatch(EnterpriseRequestSubmittedHandler.MessageType));
        var handler = new EnterpriseRequestSubmittedHandler(serializer, starter, tenant);
        var context = new IntegrationEventContext(Guid.NewGuid(), handler.EventType, 1, tenant.Id, null, DateTimeOffset.UtcNow);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(context, default, default));
        await handler.HandleAsync(context, default, default);
        await starter.Received(2).StartAsync(intent.ActorUserId, Arg.Is<StartWorkflowInstanceCommand>(command =>
            command != null && command.DefinitionVersionId == intent.DefinitionVersionId && command.IdempotencyKey == $"submit:{intent.RequestId:D}:2"), Arg.Any<CancellationToken>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => handler.HandleAsync(context with { TenantId = Guid.NewGuid() }, default, default));
    }
}

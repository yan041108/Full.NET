using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Domain;
using Full.NET.Modules.Workflow.Features.ManageDefinitions;
using Full.NET.Modules.Workflow.Features.ManageForms;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowTenantEntitlementTransactionBoundaryTests
{
    [TestMethod]
    [DataRow("definition-draft", true)]
    [DataRow("form-draft", true)]
    [DataRow("form-publish", true)]
    [DataRow("definition-draft", false)]
    [DataRow("form-draft", false)]
    [DataRow("form-publish", false)]
    public async Task Tenant_entitlement_is_checked_before_local_transaction(string operation, bool granted)
    {
        var tenantId = Guid.CreateVersion7();
        var tenant = Substitute.For<ICurrentTenant>();
        tenant.IsHost.Returns(false);
        tenant.IsAvailable.Returns(true);
        tenant.Id.Returns(tenantId);
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var transaction = new TrackingTransaction();
        var entitlements = Substitute.For<ITenantFeatureEntitlementPort>();
        entitlements.IsFeatureGrantedAsync(tenantId, TenantEntitlementCatalogCodes.Workflow,
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Assert.IsFalse(transaction.IsActive, "跨模块权益读取必须发生在 Workflow 本地事务之前。");
                return granted
                    ? Result<bool>.Success(true)
                    : Result<bool>.Failure(new Error(TenancyErrorCodes.EntitlementFeatureNotGranted,
                        "Feature not granted.", ErrorType.Validation));
            });

        var id = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        Error? error;
        if (operation == "definition-draft")
        {
            var service = new WorkflowDefinitionManagementService(query, command, transaction, tenant,
                Substitute.For<IClock>(), Substitute.For<IIdGenerator>(),
                Substitute.For<IHostUserBatchSelectionDirectory>(), Substitute.For<ITenantUserSelectionDirectory>(),
                WorkflowTodoManagementTestDependencies.CreateAssigneePublishValidator(), entitlements);
            var result = await service.UpdateDraftAsync(id, actorId,
                new UpdateWorkflowDefinitionDraftRequest(1, new WorkflowDefinitionDraft(1, [])));
            Assert.IsFalse(result.IsSuccess);
            error = result.Error;
        }
        else
        {
            var service = new WorkflowFormManagementService(query, command, transaction, tenant,
                Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), entitlements);
            if (operation == "form-draft")
            {
                var result = await service.UpdateDraftAsync(id,
                    new UpdateWorkflowFormDraftRequest(1, new WorkflowFormSchema(1, 1, [])));
                Assert.IsFalse(result.IsSuccess);
                error = result.Error;
            }
            else
            {
                var result = await service.PublishAsync(id, actorId, new PublishWorkflowFormRequest(1));
                Assert.IsFalse(result.IsSuccess);
                error = result.Error;
            }
        }

        Assert.IsNotNull(error);
        if (!granted)
        {
            Assert.AreEqual(TenancyErrorCodes.EntitlementFeatureNotGranted, error.Code);
            Assert.AreEqual(0, query.ReceivedCalls().Count());
        }

        Assert.AreEqual(granted ? 1 : 0, transaction.EntryCount);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
        await entitlements.Received(1).IsFeatureGrantedAsync(tenantId, TenantEntitlementCatalogCodes.Workflow,
            Arg.Any<CancellationToken>());
    }

    private sealed class TrackingTransaction : ICommandTransaction
    {
        public bool IsActive { get; private set; }
        public int EntryCount { get; private set; }

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            EntryCount++;
            IsActive = true;
            try
            {
                return await action(cancellationToken);
            }
            finally
            {
                IsActive = false;
            }
        }
    }
}

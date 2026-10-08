using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Dapper;
using Full.NET.UnitTests.Data;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Hosting.Api;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.SubmitForApproval;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class SubmitEnterpriseRequestForApprovalSecurityTests
{
    [TestMethod]
    public async Task Endpoint_requires_independent_submit_permission_and_registered_action()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<SubmitEnterpriseRequestForApprovalService>();
        builder.Services.AddSingleton(Substitute.For<IApiResultMapper>());
        await using var app = builder.Build();
        SubmitForApprovalEndpoint.Map(app);
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText
                == "/api/v1/enterprise_request/enterprise-requests/{id:guid}/submit-for-approval");
        var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.HasCount(1, authorization);
        Assert.AreEqual(FullNetPermissionPolicies.For(EnterpriseRequestWorkflowPermissions.Submit), authorization[0].Policy);
        var catalog = new Full.NET.Modules.EnterpriseRequest.EnterpriseRequestAuthorizationContributor();
        Assert.AreEqual(1, catalog.Permissions.Count(value => value.Code == EnterpriseRequestWorkflowPermissions.Submit));
        Assert.AreEqual(1, catalog.Actions.Count(value => value.PermissionCode == EnterpriseRequestWorkflowPermissions.Submit));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Organization_denial_has_no_status_or_workflow_side_effect(bool successWithFalse)
    {
        using var fixture = new Fixture();
        fixture.Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(successWithFalse ? Result<bool>.Success(false)
                : Result<bool>.Failure(new Error(OrganizationErrorCodes.WriteAccessDenied, "Denied", ErrorType.Forbidden)));
        var result = await fixture.Service.SubmitAsync(fixture.Row.Id, fixture.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        await fixture.Authorizer.Received(1).EnsureCanWriteAsync(fixture.TenantId, fixture.Row.OrganizationUnitId, fixture.Actor, Arg.Any<CancellationToken>());
        fixture.AssertNoSideEffects();
        Assert.AreEqual(0, fixture.Definitions.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("identity")]
    [DataRow("deleted")]
    public async Task Foreign_or_unavailable_record_is_not_found_before_authorization(string mismatch)
    {
        using var fixture = new Fixture();
        var requestedId = fixture.Row.Id;
        fixture.Row = mismatch switch
        {
            "tenant" => fixture.Row with { TenantId = Guid.NewGuid() },
            "identity" => fixture.Row with { Id = Guid.NewGuid() },
            _ => fixture.Row with { IsDeleted = true }
        };
        var result = await fixture.Service.SubmitAsync(requestedId, fixture.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.NotFound, result.Error!.Type);
        Assert.AreEqual(0, fixture.Authorizer.ReceivedCalls().Count());
        fixture.AssertNoSideEffects();
    }

    [TestMethod]
    [DataRow("unavailable")]
    [DataRow("host")]
    [DataRow("missing")]
    [DataRow("empty")]
    public async Task Invalid_trusted_tenant_rejects_before_database(string kind)
    {
        using var fixture = new Fixture();
        if (kind == "unavailable") fixture.Tenant.IsAvailable.Returns(false);
        if (kind == "host") fixture.Tenant.IsHost.Returns(true);
        if (kind == "missing") fixture.Tenant.Id.Returns((Guid?)null);
        if (kind == "empty") fixture.Tenant.Id.Returns(Guid.Empty);
        Assert.IsFalse((await fixture.Service.SubmitAsync(fixture.Row.Id, fixture.Actor)).IsSuccess);
        Assert.AreEqual(0, fixture.Queries.ReceivedCalls().Count());
        fixture.AssertNoSideEffects();
    }

    [TestMethod]
    public async Task Empty_actor_is_rejected_before_database()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.SubmitAsync(fixture.Row.Id, Guid.Empty);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Forbidden, result.Error!.Type);
        Assert.AreEqual(0, fixture.Queries.ReceivedCalls().Count());
        fixture.AssertNoSideEffects();
    }

    [TestMethod]
    public async Task Allowed_submission_authorizes_original_unit_and_records_pinned_intent_once()
    {
        using var fixture = new Fixture();
        var original = fixture.Row;
        var result = await fixture.Service.SubmitAsync(original.Id, fixture.Actor);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, result.Value!.Status);
        await fixture.Authorizer.Received(1).EnsureCanWriteAsync(fixture.TenantId, original.OrganizationUnitId, fixture.Actor, Arg.Any<CancellationToken>());
        await fixture.Commands.Received(1).ExecuteAsync(EnterpriseRequestApprovalSql.Insert,
            Arg.Is<object?>(value => value is Dictionary<string, object?> &&
                ((Dictionary<string, object?>)value)["WorkflowDefinitionVersionId"]!.Equals(fixture.DefinitionId) &&
                ((Dictionary<string, object?>)value)["RequestVersion"]!.Equals(original.Version + 1)), Arg.Any<CancellationToken>());
        Assert.AreEqual(0, fixture.Starter.ReceivedCalls().Count());
        Assert.AreEqual(2, fixture.Commands.ReceivedCalls().Count());
        Assert.AreEqual(1, fixture.Coordinator.CommitCount);
    }

    [TestMethod]
    public async Task Compare_and_swap_conflict_does_not_start_workflow()
    {
        using var fixture = new Fixture();
        fixture.Commands.ExecuteAsync(EnterpriseRequestWorkflowSql.ApplySubmittedStatus, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await fixture.Service.SubmitAsync(fixture.Row.Id, fixture.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestErrorCodes.VersionConflict, result.Error!.Code);
        Assert.AreEqual(0, fixture.Starter.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Allowed_submission_queues_outbox_without_inline_workflow_start()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.SubmitAsync(fixture.Row.Id, fixture.Actor);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(0, fixture.Starter.ReceivedCalls().Count(), "提交请求不能等待跨模块流程写入。");
        Assert.AreEqual(1, fixture.Outbox.ReceivedCalls().Count(), "状态和启动意图必须由事务 Outbox 可靠交付。");
    }

    [TestMethod]
    public async Task Workflow_unavailability_does_not_prevent_durable_submission()
    {
        using var fixture = new Fixture();
        fixture.Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<WorkflowInstanceLifecycleResult>.Failure(new Error("workflow.unavailable", "Unavailable", ErrorType.Conflict)));
        var result = await fixture.Service.SubmitAsync(fixture.Row.Id, fixture.Actor);
        Assert.IsTrue(result.IsSuccess, "流程不可用时仍应可靠保存待启动意图，由 Worker 重试。");
        Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, result.Value!.Status);
        Assert.AreEqual(0, fixture.Starter.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Outbox_failure_rolls_back_submission_transaction()
    {
        using var f = new Fixture();
        f.Outbox.AddAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<EnterpriseRequestApprovalSubmittedIntegrationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("outbox.failed")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.Row.Id, f.Actor));
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.AreEqual(0, f.Starter.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Submission_insert_failure_rolls_back_and_does_not_queue_outbox()
    {
        using var f = new Fixture();
        f.Commands.ExecuteAsync(EnterpriseRequestApprovalSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(0);
        var result = await f.Service.SubmitAsync(f.Row.Id, f.Actor);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, f.Coordinator.RollbackCount); Assert.AreEqual(0, f.Coordinator.CommitCount);
        Assert.AreEqual(0, f.Outbox.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Original_actor_can_replay_queued_submission_without_new_intent()
    {
        using var f = new Fixture();
        f.Row = f.Row with { Status = "Submitted", Version = 2 };
        var submission = new EnterpriseRequestApprovalSubmission(Guid.CreateVersion7(), f.TenantId, f.Row.Id, 2,
            f.DefinitionId, Guid.CreateVersion7(), f.Actor, f.Row.OrganizationUnitId, f.Row.Title, DateTimeOffset.UtcNow, null, null, null, null);
        f.Queries.QuerySingleOrDefaultAsync<EnterpriseRequestApprovalSubmission>(EnterpriseRequestApprovalSql.FindByRequest,
            Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(submission);
        Assert.IsTrue((await f.Service.SubmitAsync(f.Row.Id, f.Actor)).IsSuccess);
        f.AssertNoSideEffects();
        Assert.AreEqual(0, f.Definitions.ReceivedCalls().Count());
        await f.Authorizer.Received(1).EnsureCanWriteAsync(f.TenantId, f.Row.OrganizationUnitId, f.Actor, Arg.Any<CancellationToken>());
    }

    private sealed class Fixture : IDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid Actor { get; } = Guid.NewGuid();
        public Guid DefinitionId { get; } = Guid.NewGuid();
        public IQueryExecutor Queries { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Commands { get; } = Substitute.For<ICommandExecutor>();
        public ICurrentTenant Tenant { get; } = Substitute.For<ICurrentTenant>();
        public IOrganizationOwnedEntityWriteAuthorizer Authorizer { get; } = Substitute.For<IOrganizationOwnedEntityWriteAuthorizer>();
        public IWorkflowPublishedDefinitionDirectory Definitions { get; } = Substitute.For<IWorkflowPublishedDefinitionDirectory>();
        public IWorkflowInstanceStarter Starter { get; } = Substitute.For<IWorkflowInstanceStarter>();
        public IOutboxWriter Outbox { get; } = Substitute.For<IOutboxWriter>();
        public RecordingDbTransactionCoordinator Coordinator { get; } = new();
        public EnterpriseRequestRecord Row { get; set; }
        public SubmitEnterpriseRequestForApprovalService Service { get; }
        private readonly ServiceProvider provider;

        public Fixture()
        {
            Tenant.IsAvailable.Returns(true);
            Tenant.Id.Returns(TenantId);
            Row = new EnterpriseRequestRecord(Guid.NewGuid(), TenantId, Guid.NewGuid(), "REQ-1", "Title",
                EnterpriseRequestStatusKeys.Draft, 1m, Actor, 1, DateTimeOffset.UtcNow, Actor, null, null, false, null, null);
            Queries.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(EnterpriseRequestSql.FindByIdStatement, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Row);
            Authorizer.EnsureCanWriteAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<bool>.Success(true));
            Definitions.FindLatestPublishedAsync(EnterpriseRequestWorkflowConstants.DefinitionKey, Arg.Any<CancellationToken>())
                .Returns(new WorkflowPublishedDefinitionVersion(DefinitionId, Guid.NewGuid(), EnterpriseRequestWorkflowConstants.DefinitionKey));
            Commands.ExecuteAsync(EnterpriseRequestWorkflowSql.ApplySubmittedStatus, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => { Row = Row with { Status = EnterpriseRequestStatusKeys.Submitted, Version = Row.Version + 1 }; return 1; });
            Commands.ExecuteAsync(EnterpriseRequestApprovalSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
            Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result<WorkflowInstanceLifecycleResult>.Success(new WorkflowInstanceLifecycleResult(Guid.NewGuid(), "running", 1)));
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(_ => Guid.CreateVersion7());
            provider = new ServiceCollection().AddSingleton(Queries).AddSingleton(Commands).AddSingleton(Tenant)
                .AddSingleton(ids).AddSingleton<ICommandTransaction>(new DapperCommandTransaction(Coordinator))
                .AddSingleton(Authorizer).AddSingleton(Definitions).AddSingleton(Starter).AddSingleton(Outbox).AddSingleton(Substitute.For<IClock>())
                .AddTransient<SubmitEnterpriseRequestForApprovalService>().BuildServiceProvider();
            Service = provider.GetRequiredService<SubmitEnterpriseRequestForApprovalService>();
        }

        public void AssertNoSideEffects()
        {
            Assert.AreEqual(0, Commands.ReceivedCalls().Count());
            Assert.AreEqual(0, Starter.ReceivedCalls().Count());
            Assert.AreEqual(0, Outbox.ReceivedCalls().Count());
        }

        public void Dispose() => provider.Dispose();
    }
}

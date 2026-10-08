using Full.NET.Abstractions.Results;
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
    public async Task Allowed_submission_authorizes_original_unit_and_starts_pinned_definition_once()
    {
        using var fixture = new Fixture();
        var original = fixture.Row;
        var result = await fixture.Service.SubmitAsync(original.Id, fixture.Actor);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(EnterpriseRequestStatusKeys.Submitted, result.Value!.Status);
        await fixture.Authorizer.Received(1).EnsureCanWriteAsync(fixture.TenantId, original.OrganizationUnitId, fixture.Actor, Arg.Any<CancellationToken>());
        await fixture.Starter.Received(1).StartAsync(fixture.Actor,
            Arg.Is<StartWorkflowInstanceCommand>(value => value != null && value.DefinitionVersionId == fixture.DefinitionId
                && value.BusinessId == original.Id.ToString("D")
                && value.IdempotencyKey == $"submit:{original.Id:D}:{original.Version}"), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, fixture.Commands.ReceivedCalls().Count());
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
            Starter.StartAsync(Arg.Any<Guid>(), Arg.Any<StartWorkflowInstanceCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result<WorkflowInstanceLifecycleResult>.Success(new WorkflowInstanceLifecycleResult(Guid.NewGuid(), "running", 1)));
            provider = new ServiceCollection().AddSingleton(Queries).AddSingleton(Commands).AddSingleton(Tenant)
                .AddSingleton(Authorizer).AddSingleton(Definitions).AddSingleton(Starter).AddSingleton(Substitute.For<IClock>())
                .AddTransient<SubmitEnterpriseRequestForApprovalService>().BuildServiceProvider();
            Service = provider.GetRequiredService<SubmitEnterpriseRequestForApprovalService>();
        }

        public void AssertNoSideEffects()
        {
            Assert.AreEqual(0, Commands.ReceivedCalls().Count());
            Assert.AreEqual(0, Starter.ReceivedCalls().Count());
        }

        public void Dispose() => provider.Dispose();
    }
}

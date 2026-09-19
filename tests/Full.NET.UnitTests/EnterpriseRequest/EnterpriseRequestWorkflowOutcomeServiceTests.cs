using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Features.WorkflowOutcomes;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.EnterpriseRequest.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.EnterpriseRequest;

[TestClass]
public sealed class EnterpriseRequestWorkflowOutcomeServiceTests
{
    [TestMethod]
    public async Task Ignores_non_enterprise_business_type()
    {
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var service = new EnterpriseRequestWorkflowOutcomeService(
            queryExecutor,
            commandExecutor,
            Substitute.For<IClock>());

        await service.HandleTerminalWorkflowAsync(
            "other.business",
            Guid.NewGuid().ToString("D"),
            EnterpriseRequestStatusKeys.Approved,
            "key",
            CancellationToken.None);

        await queryExecutor.DidNotReceive().QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
            Arg.Any<SqlStatement>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Approved_workflow_updates_submitted_request()
    {
        var requestId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var row = new EnterpriseRequestRecord(
            requestId,
            tenantId,
            Guid.NewGuid(),
            "REQ-1",
            "title",
            EnterpriseRequestStatusKeys.Submitted,
            1m,
            Guid.NewGuid(),
            2,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            false,
            null,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        var clock = Substitute.For<IClock>();
        var now = DateTimeOffset.UtcNow;
        clock.UtcNow.Returns(now);
        queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(row);
        commandExecutor.ExecuteAsync(
                EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var service = new EnterpriseRequestWorkflowOutcomeService(
            queryExecutor,
            commandExecutor,
            clock);

        await service.HandleTerminalWorkflowAsync(
            EnterpriseRequestWorkflowConstants.BusinessType,
            requestId.ToString("D"),
            EnterpriseRequestStatusKeys.Approved,
            "completed",
            CancellationToken.None);

        await commandExecutor.Received(1).ExecuteAsync(
            EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Is<object?>(parameters => parameters != null),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Does_not_update_when_request_is_not_submitted()
    {
        var requestId = Guid.NewGuid();
        var row = new EnterpriseRequestRecord(
            requestId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "REQ-1",
            "title",
            EnterpriseRequestStatusKeys.Draft,
            1m,
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            false,
            null,
            null);
        var queryExecutor = Substitute.For<IQueryExecutor>();
        var commandExecutor = Substitute.For<ICommandExecutor>();
        queryExecutor.QuerySingleOrDefaultAsync<EnterpriseRequestRecord>(
                EnterpriseRequestSql.FindByIdStatement,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(row);

        var service = new EnterpriseRequestWorkflowOutcomeService(
            queryExecutor,
            commandExecutor,
            Substitute.For<IClock>());

        await service.HandleTerminalWorkflowAsync(
            EnterpriseRequestWorkflowConstants.BusinessType,
            requestId.ToString("D"),
            EnterpriseRequestStatusKeys.Approved,
            "completed",
            CancellationToken.None);

        await commandExecutor.DidNotReceive().ExecuteAsync(
            EnterpriseRequestWorkflowSql.ApplyTerminalStatus,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }
}
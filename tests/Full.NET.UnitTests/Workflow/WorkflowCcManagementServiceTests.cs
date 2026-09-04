using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Features.ManageMyCc;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

[TestClass]
public sealed class WorkflowCcManagementServiceTests
{
    [TestMethod]
    public async Task Mark_read_returns_not_found_without_updating_another_users_record()
    {
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        tenant.IsHost.Returns(true);
        query.QuerySingleOrDefaultAsync<WorkflowCcRecord>(
                WorkflowSql.FindOwnCcById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns((WorkflowCcRecord?)null);
        var service = new WorkflowCcManagementService(
            query,
            command,
            tenant,
            clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await service.MarkReadAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("workflow.cc.not_found", result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Mark_read_is_idempotent_when_own_record_is_already_read()
    {
        var ccId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var readAt = DateTimeOffset.UtcNow;
        var query = Substitute.For<IQueryExecutor>();
        var command = Substitute.For<ICommandExecutor>();
        var tenant = Substitute.For<ICurrentTenant>();
        var clock = Substitute.For<IClock>();
        tenant.IsHost.Returns(true);
        query.QuerySingleOrDefaultAsync<WorkflowCcRecord>(
                WorkflowSql.FindOwnCcById,
                Arg.Any<object?>(),
                Arg.Any<CancellationToken>())
            .Returns(CcRecord(ccId, actorId, readAt));
        var service = new WorkflowCcManagementService(
            query,
            command,
            tenant,
            clock,
            Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer }));

        var result = await service.MarkReadAsync(ccId, actorId);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(readAt, result.Value!.ReadAtUtc);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }

    private static WorkflowCcRecord CcRecord(
        Guid id,
        Guid actorId,
        DateTimeOffset? readAtUtc) =>
        new(
            id,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "copy",
            actorId,
            "leave.request",
            "request-1",
            DateTimeOffset.UtcNow,
            readAtUtc);
}

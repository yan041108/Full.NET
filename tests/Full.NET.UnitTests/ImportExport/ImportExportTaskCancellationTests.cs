using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>取消只结束未在写入的任务；创建人、会话、版本和原检查点必须保持边界。</summary>
[TestClass]
public sealed class ImportExportTaskCancellationTests
{
    [TestMethod]
    [DataRow("preview_succeeded")]
    [DataRow("queued")]
    [DataRow("execution_partial")]
    [DataRow("execution_failed")]
    public async Task Cancellation_preserves_checkpoint_and_receipt(string status)
    {
        var fixture = new Fixture(status);
        var result = await fixture.CancelAsync();
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("cancelled", result.Value!.StatusKey);
        Assert.AreEqual(1, result.Value.NextLineNumber);
        Assert.AreEqual(1, result.Value.ProcessedRowCount);
        Assert.AreEqual(1, result.Value.SucceededRowCount);
        Assert.IsTrue(result.Value.HasErrorReceipt);
        Assert.AreEqual(1, fixture.Command.ReceivedCalls().Count());
        var parameters = (Dictionary<string, object?>)fixture.Command.ReceivedCalls().Single().GetArguments()[1]!;
        Assert.AreEqual(4L, parameters["ExpectedVersion"]);
        Assert.AreEqual(status, parameters["ExpectedStatusKey"]);
        Assert.AreEqual(fixture.Binding.UserId, parameters["RequestedByUserId"]);
    }

    [TestMethod]
    [DataRow("uploaded")]
    [DataRow("preview_failed")]
    [DataRow("executing")]
    [DataRow("execution_succeeded")]
    public async Task Inflight_or_completed_task_is_conflict_without_mutation(string status)
    {
        var fixture = new Fixture(status);
        var result = await fixture.CancelAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("actor")]
    [DataRow("permission")]
    [DataRow("tenant")]
    public async Task Untrusted_actor_or_revoked_cancel_permission_is_denied(string reason)
    {
        var fixture = new Fixture("queued");
        var binding = reason == "actor" ? fixture.Binding with { UserId = Guid.NewGuid() }
            : reason == "tenant" ? fixture.Binding with { TenantId = Guid.NewGuid() } : fixture.Binding;
        if (reason == "permission") fixture.Identity.AuthorizeAsync(fixture.Binding, "import_export.import_tasks.cancel", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        var result = await fixture.CancelAsync(binding);
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Repeated_cancel_returns_same_terminal_without_write()
    {
        var fixture = new Fixture("cancelled");
        Assert.IsTrue((await fixture.CancelAsync()).IsSuccess);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Concurrent_claim_does_not_overwrite_new_worker_state()
    {
        var fixture = new Fixture("queued", "executing", 0);
        var result = await fixture.CancelAsync();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(ErrorType.Conflict, result.Error!.Type);
        Assert.AreEqual(1, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Concurrent_cancel_is_idempotent_after_compare_and_swap_loss()
    {
        var fixture = new Fixture("queued", "cancelled", 0);
        Assert.IsTrue((await fixture.CancelAsync()).IsSuccess);
        Assert.AreEqual(1, fixture.Command.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Missing_task_is_not_found_without_write()
    {
        var fixture = new Fixture("queued");
        fixture.Query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ImportExportTaskRecord?>(null));
        var result = await fixture.CancelAsync();
        Assert.AreEqual(ErrorType.NotFound, result.Error!.Type);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        internal IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        internal ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        internal IBackgroundSessionAuthorization Identity { get; } = Substitute.For<IBackgroundSessionAuthorization>();
        internal SessionBindingSnapshot Binding { get; }
        private readonly CurrentTenantAccessor tenant = new();
        private readonly IClock clock = Substitute.For<IClock>();
        private readonly Guid taskId = Guid.NewGuid();

        internal Fixture(string status, string final = "cancelled", int affected = 1)
        {
            var tenantId = Guid.NewGuid(); var userId = Guid.NewGuid();
            tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
            Binding = new SessionBindingSnapshot(userId, tenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
            clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-14T00:00:00Z"));
            var written = false;
            Query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult<ImportExportTaskRecord?>(Record(written ? final : status)));
            Command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => { written = true; return Task.FromResult(affected); });
            Identity.AuthorizeAsync(Binding, "import_export.import_tasks.cancel", Arg.Any<CancellationToken>())
                .Returns(new AuthorizedSessionActor(userId, tenantId, Binding.SessionId));
        }

        private ImportExportTaskRecord Record(string status) => new()
        {
            Id = taskId, TenantId = Binding.TenantId!.Value, RequestedByUserId = Binding.UserId,
            SchemaKey = "positions", WorksheetKey = "positions", SchemaDisplayName = "positions",
            SourceFileName = "positions.xlsx", StatusKey = status, Version = 4,
            ValidRowCount = 3, TotalRows = 3, ProcessedRowCount = 1, SucceededRowCount = 1,
            NextLineNumber = 1, ExecutionFailedRowCount = 1, ErrorReceiptFileId = Guid.NewGuid(),
            ExecutionRowsJson = "[]",
        };

        internal async Task<Result<ImportExportTaskDetailResponse>> CancelAsync(SessionBindingSnapshot? binding = null)
        {
            var service = new ImportExportTaskCancellationService(Query, Command, tenant, Identity, clock);
            return await service.CancelAsync(taskId, binding ?? Binding);
        }
    }
}

using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>排队前实际核对来源；缺失、撤权、取消不改变检查点，已打开流始终收回。</summary>
[TestClass]
public sealed class ImportSourceAvailabilityTests
{
    [TestMethod]
    [DataRow("queue")]
    [DataRow("resume")]
    [DataRow("retry")]
    public async Task Missing_source_rejects_without_changing_the_task(string operation)
    {
        var f = Create(operation);
        f.Files.OpenReadyContentAsync("import_export", f.Task.Id, f.Task.SourceFileId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantResourceFileContent>.Failure(new Error(FilesErrorCodes.FileNotFound, "Missing", ErrorType.NotFound)));
        var result = await f.Run();
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ImportExportErrorCodes.SourceUnavailable, result.Error!.Code);
        Assert.AreEqual(ErrorType.Conflict, result.Error.Type); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("queue")]
    [DataRow("resume")]
    [DataRow("retry")]
    public async Task Available_source_is_not_read_and_is_disposed_before_queue(string operation)
    {
        var f = Create(operation); var stream = new ObservedStream();
        f.Files.OpenReadyContentAsync("import_export", f.Task.Id, f.Task.SourceFileId, Arg.Any<CancellationToken>())
            .Returns(Result<TenantResourceFileContent>.Success(new(stream, "application/octet-stream", "source.xlsx")));
        f.Commands.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ => {
            Assert.IsTrue(stream.Disposed); Assert.AreEqual(0, stream.Reads); return 1;
        });
        Assert.IsTrue((await f.Run()).IsSuccess); Assert.IsTrue(stream.Disposed);
        await f.Sessions.Received(2).AuthorizeAllAsync(f.Binding, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        Assert.AreEqual(1, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Revocation_while_opening_the_source_disposes_it_and_prevents_queue()
    {
        var f = Create("queue"); var stream = new ObservedStream();
        f.Files.OpenReadyContentAsync("import_export", f.Task.Id, f.Task.SourceFileId, Arg.Any<CancellationToken>()).Returns(_ => {
            f.Sessions.AuthorizeAllAsync(f.Binding, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
            return Result<TenantResourceFileContent>.Success(new(stream, "application/octet-stream", "source.xlsx"));
        });
        var result = await f.Run(); Assert.IsFalse(result.IsSuccess); Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, result.Error!.Code);
        Assert.IsTrue(stream.Disposed); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Cancellation_after_opening_disposes_the_source_and_never_queues()
    {
        using var cancellation = new CancellationTokenSource(); var f = Create("queue", cancellation.Token); var stream = new ObservedStream();
        f.Files.OpenReadyContentAsync("import_export", f.Task.Id, f.Task.SourceFileId, Arg.Any<CancellationToken>()).Returns(_ => {
            cancellation.Cancel(); return Result<TenantResourceFileContent>.Success(new(stream, "application/octet-stream", "source.xlsx"));
        });
        await Assert.ThrowsAsync<OperationCanceledException>(f.Run); Assert.IsTrue(stream.Disposed); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    [TestMethod]
    public async Task Storage_failure_is_not_misreported_as_permanent_source_loss()
    {
        var f = Create("retry"); var error = new Error("files.storage.unavailable", "Storage unavailable", ErrorType.Unexpected);
        f.Files.OpenReadyContentAsync("import_export", f.Task.Id, f.Task.SourceFileId, Arg.Any<CancellationToken>()).Returns(Result<TenantResourceFileContent>.Failure(error));
        var result = await f.Run(); Assert.AreEqual(error, result.Error); Assert.AreEqual(0, f.Commands.ReceivedCalls().Count());
    }

    private static Fixture Create(string operation, CancellationToken token = default)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.CreateVersion7(); tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var binding = new SessionBindingSnapshot(Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), "stamp", "host", $"tenant:{tenantId:N}");
        var task = new ImportExportTaskRecord { Id = Guid.CreateVersion7(), TenantId = tenantId, RequestedByUserId = binding.UserId,
            SchemaKey = "positions", SchemaDisplayName = "Positions", WorksheetKey = "positions", SourceFileId = Guid.CreateVersion7(), SourceFileName = "source.xlsx",
            StatusKey = operation == "queue" ? ImportExportTaskStatusKeys.PreviewSucceeded : operation == "resume" ? ImportExportTaskStatusKeys.ExecutionPartial : ImportExportTaskStatusKeys.ExecutionFailed,
            Version = 1, ValidRowCount = 2, SucceededRowCount = operation == "resume" ? 1 : 0,
            ExecutionRowsJson = ImportExportTaskMapper.SerializeExecutionState(new ImportExportExecutionStateDocument(new Dictionary<string, bool>(), [])) };
        var query = Substitute.For<IQueryExecutor>(); var commands = Substitute.For<ICommandExecutor>(); var files = Substitute.For<ITenantResourceFileStore>();
        query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(task);
        var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("positions");
        handler.GetDefinition().Returns(new StaticImportSchemaDefinition("positions", "Positions", "tenant", "positions.import", [new("positions", "Positions", ["value"])]));
        handler.ExecutionCapabilityPermissions.Returns(Array.Empty<string>());
        var sessions = Substitute.For<IBackgroundSessionAuthorization>();
        sessions.AuthorizeAllAsync(binding, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(new AuthorizedSessionActor(binding.UserId, tenantId, binding.SessionId));
        var options = Substitute.For<IOptionsMonitor<ImportExportOptions>>(); options.CurrentValue.Returns(new ImportExportOptions());
        var service = new ImportExportTaskExecutionService(query, commands, files, tenant, new ImportTaskAuthority(new StaticImportSchemaRegistry([handler]), sessions, tenant), Substitute.For<IServiceScopeFactory>(), options);
        var context = new StaticImportPreviewContext(binding.UserId, new Dictionary<string, bool>()) { SessionBinding = binding };
        return new(task, files, commands, sessions, binding, () => operation == "queue" ? service.QueueExecuteAsync(task.Id, context, token)
            : operation == "resume" ? service.ResumeAsync(task.Id, context, token) : service.RetryAsync(task.Id, context, token));
    }
    private sealed record Fixture(ImportExportTaskRecord Task, ITenantResourceFileStore Files, ICommandExecutor Commands,
        IBackgroundSessionAuthorization Sessions, SessionBindingSnapshot Binding, Func<Task<Result<ImportExportTaskDetailResponse>>> Run);
    private sealed class ObservedStream : MemoryStream
    {
        internal bool Disposed; internal int Reads;
        public override int Read(byte[] buffer, int offset, int count) { Reads++; return base.Read(buffer, offset, count); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { Reads++; return base.ReadAsync(buffer, cancellationToken); }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}

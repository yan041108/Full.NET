using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>预览成功载荷仍须经过框架边界检查，不能把冲突计数直接保存为可执行任务。</summary>
[TestClass]
public sealed class ImportExportTaskPreviewBoundaryTests
{
    [TestMethod]
    [DataRow("result-null")]
    [DataRow("null")]
    [DataRow("rows-null")]
    [DataRow("count")]
    [DataRow("duplicate")]
    [DataRow("valid-count")]
    [DataRow("limit")]
    [DataRow("valid")]
    [DataRow("valid-sparse")]
    public async Task Preview_boundary_controls_persisted_status_and_counts(string kind)
    {
        StaticImportPreviewResult? preview = kind switch
        {
            "null" => null,
            "rows-null" => new(1, 1, 0, null!),
            "count" => new(2, 2, 0, [new(1, true, null, null)]),
            "duplicate" => new(2, 2, 0, [new(1, true, null, null), new(1, true, null, null)]),
            "valid-count" => new(1, 1, 0, [new(1, false, "validation.failed", "invalid")]),
            "limit" => new(3, 3, 0, [new(1, true, null, null), new(2, true, null, null), new(3, true, null, null)]),
            "valid-sparse" => new(1, 1, 0, [new(4096, true, null, null)]),
            _ => new(1, 1, 0, [new(1, true, null, null)]),
        };
        var fixture = new Fixture(preview, returnNullResult: kind == "result-null");
        await using var input = new MemoryStream([1]);
        var result = await fixture.Service.CreateAsync("positions", "data", "positions.xlsx", input, 1,
            fixture.ActorId, new(fixture.ActorId, new Dictionary<string, bool>()), default);
        Assert.IsFalse(fixture.Probe.CanRead);
        Assert.IsTrue(input.CanRead);
        if (kind == "limit")
        {
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(ImportExportErrorCodes.RowLimitExceeded, result.Error!.Code);
            Assert.IsNull(fixture.Inserted);
            return;
        }
        Assert.IsTrue(result.IsSuccess);
        var valid = kind is "valid" or "valid-sparse";
        Assert.AreEqual(valid ? ImportExportTaskStatusKeys.PreviewSucceeded : ImportExportTaskStatusKeys.PreviewFailed, result.Value!.StatusKey);
        Assert.AreEqual(valid ? 1 : 0, result.Value.TotalRows);
        Assert.AreEqual(valid ? null : ImportExportErrorCodes.PreviewFailed, result.Value.ErrorCode);
        if (!valid) Assert.IsNull(fixture.Inserted!["PreviewRowsJson"]);
    }

    [TestMethod]
    public async Task Ignored_preview_cancellation_cannot_insert_task_after_source_is_released()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(new(0, 0, 0, []), cancellation.Cancel);
        await using var input = new MemoryStream([1]);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.CreateAsync("positions", "data", "positions.xlsx", input, 1,
            fixture.ActorId, new(fixture.ActorId, new Dictionary<string, bool>()), cancellation.Token));
        Assert.IsNull(fixture.Inserted);
        Assert.IsFalse(fixture.Probe.CanRead);
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    [DataRow("entry")]
    [DataRow("upload")]
    [DataRow("upload-failed")]
    [DataRow("open")]
    [DataRow("open-failed")]
    [DataRow("preview")]
    [DataRow("preview-failed")]
    [DataRow("write")]
    [DataRow("readback")]
    [DataRow("readback-missing")]
    public async Task Ignored_handoff_cancellation_stops_preview_or_followup_and_retains_caller_stream(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture(new(0, 0, 0, []), boundaryStage: stage, cancellation: cancellation);
        using var input = new MemoryStream([1]);
        using var probe = fixture.Probe;
        if (stage == "entry") cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.CreateAsync("positions", "data", "positions.xlsx", input, 1,
            fixture.ActorId, new(fixture.ActorId, new Dictionary<string, bool>()), cancellation.Token));
        Assert.IsTrue(input.CanRead);
        Assert.AreEqual(!fixture.ProbeHandedOff, probe.CanRead);
        Assert.AreEqual(stage is "preview" or "preview-failed" or "write" or "readback" or "readback-missing" ? 1 : 0, fixture.PreviewCalls);
        Assert.AreEqual(stage is "write" or "readback" or "readback-missing" ? 1 : 0, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(stage is "readback" or "readback-missing" ? 1 : 0, fixture.Query.ReceivedCalls().Count());
        Assert.AreEqual(stage is "entry" or "upload" or "upload-failed" ? 0 : 1, fixture.OpenCalls);
    }

    [TestMethod]
    [DataRow("upload")]
    [DataRow("open")]
    [DataRow("preview")]
    [DataRow("write")]
    [DataRow("readback")]
    public async Task Preview_creation_refuses_changed_tenant_before_followup_or_result(string stage)
    {
        var fixture = new Fixture(new(0, 0, 0, []), boundaryStage: "scope-" + stage);
        using var input = new MemoryStream([1]);
        using var probe = fixture.Probe;
        var result = await fixture.Service.CreateAsync("positions", "data", "positions.xlsx", input, 1,
            fixture.ActorId, new(fixture.ActorId, new Dictionary<string, bool>()));
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.IsTrue(input.CanRead);
        Assert.AreEqual(!fixture.ProbeHandedOff, probe.CanRead);
        Assert.AreEqual(stage == "upload" ? 0 : 1, fixture.OpenCalls);
        Assert.AreEqual(stage is "preview" or "write" or "readback" ? 1 : 0, fixture.PreviewCalls);
        Assert.AreEqual(stage is "write" or "readback" ? 1 : 0, fixture.Command.ReceivedCalls().Count());
        Assert.AreEqual(stage == "readback" ? 1 : 0, fixture.Query.ReceivedCalls().Count());
    }

    [TestMethod]
    [DataRow("actor")]
    [DataRow("context")]
    [DataRow("stream")]
    [DataRow("empty")]
    public async Task Invalid_preview_actor_or_input_is_rejected_before_upload(string kind)
    {
        var fixture = new Fixture(new(0, 0, 0, []));
        using var input = new MemoryStream([1]);
        using var probe = fixture.Probe;
        var actor = kind == "empty" ? Guid.Empty : fixture.ActorId;
        StaticImportPreviewContext? context = kind == "context" ? null
            : new(kind == "actor" ? Guid.CreateVersion7() : actor, new Dictionary<string, bool>());
        var result = await fixture.Service.CreateAsync("positions", "data", "positions.xlsx", kind == "stream" ? null! : input, 1,
            actor, context!);
        Assert.AreEqual(kind == "stream" ? ImportExportErrorCodes.FileInvalid : CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(0, fixture.Files.ReceivedCalls().Count());
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
        Assert.IsTrue(input.CanRead);
    }

    [TestMethod]
    public async Task Unknown_preview_exception_releases_probe_without_inserting_or_disposing_caller_input()
    {
        var fixture = new Fixture(new(0, 0, 0, []), boundaryStage: "preview-throws");
        using var input = new MemoryStream([1]);
        using var probe = fixture.Probe;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.CreateAsync("positions", "data", "positions.xlsx", input, 1,
            fixture.ActorId, new(fixture.ActorId, new Dictionary<string, bool>())));
        Assert.IsFalse(probe.CanRead);
        Assert.IsTrue(input.CanRead);
        Assert.AreEqual(0, fixture.Command.ReceivedCalls().Count());
    }

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.CreateVersion7();
        public MemoryStream Probe { get; } = new([1]);
        public IReadOnlyDictionary<string, object?>? Inserted { get; private set; }
        public ImportExportTaskManagementService Service { get; }
        public ITenantResourceFileStore Files { get; } = Substitute.For<ITenantResourceFileStore>();
        public IQueryExecutor Query { get; } = Substitute.For<IQueryExecutor>();
        public ICommandExecutor Command { get; } = Substitute.For<ICommandExecutor>();
        public bool ProbeHandedOff { get; private set; }
        public int OpenCalls { get; private set; }
        public int PreviewCalls { get; private set; }

        public Fixture(StaticImportPreviewResult? preview, Action? afterPreview = null, bool returnNullResult = false,
            string? boundaryStage = null, CancellationTokenSource? cancellation = null)
        {
            var taskId = Guid.CreateVersion7(); var tenantId = Guid.CreateVersion7(); var fileId = Guid.CreateVersion7();
            var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(tenantId, "acme", "Acme"));
            var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("positions");
            handler.GetDefinition().Returns(new StaticImportSchemaDefinition("positions", "Positions", "tenant", "positions.import", [new("data", "Data", ["value"])]));
            handler.ExecutionCapabilityPermissions.Returns([]);
            handler.PreviewAsync(Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<StaticImportPreviewContext>(), Arg.Any<CancellationToken>())
                .Returns(_ => {
                    PreviewCalls++; afterPreview?.Invoke();
                    if (boundaryStage is "preview" or "preview-failed") cancellation?.Cancel();
                    if (boundaryStage == "scope-preview") tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                    if (boundaryStage == "preview-throws") throw new InvalidOperationException("fixture preview failure");
                    return Task.FromResult(returnNullResult ? null! : boundaryStage == "preview-failed"
                        ? Result<StaticImportPreviewResult>.Failure(new(ImportExportErrorCodes.PreviewFailed, "fixture", ErrorType.Validation))
                        : Result<StaticImportPreviewResult>.Success(preview!)); });
            var files = Files;
            files.UploadAsync("import_export", taskId, ActorId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), 1, Arg.Any<CancellationToken>())
                .Returns(_ => {
                    if (boundaryStage is "upload" or "upload-failed") cancellation?.Cancel();
                    if (boundaryStage == "scope-upload") tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                    return boundaryStage == "upload-failed" ? Result<TenantResourceFileReference>.Failure(new(ImportExportErrorCodes.FileInvalid, "fixture", ErrorType.Validation))
                        : Result<TenantResourceFileReference>.Success(new(fileId, 1, "hash")); });
            files.OpenReadyContentAsync("import_export", taskId, fileId, Arg.Any<CancellationToken>())
                .Returns(_ => {
                    OpenCalls++;
                    if (boundaryStage is "open" or "open-failed") cancellation?.Cancel();
                    if (boundaryStage == "scope-open") tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                    if (boundaryStage == "open-failed") return Result<TenantResourceFileContent>.Failure(new(ImportExportErrorCodes.FileInvalid, "fixture", ErrorType.Validation));
                    ProbeHandedOff = true;
                    return Result<TenantResourceFileContent>.Success(new(Probe, "application/test", "positions.xlsx")); });
            var command = Command;
            command.ExecuteAsync(ImportExportTaskSql.Insert, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(call => {
                    Inserted = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1);
                    if (boundaryStage == "write") cancellation?.Cancel();
                    if (boundaryStage == "scope-write") tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                    return 1; });
            var query = Query;
            query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => {
                    if (boundaryStage is "readback" or "readback-missing") cancellation?.Cancel();
                    if (boundaryStage == "readback-missing") return null;
                    if (boundaryStage == "scope-readback") tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
                    return new ImportExportTaskRecord { Id = taskId, TenantId = tenantId, SourceFileId = fileId, RequestedByUserId = ActorId,
                    StatusKey = (string)Inserted!["StatusKey"]!, ErrorCode = (string?)Inserted["ErrorCode"],
                    TotalRows = (int)Inserted["TotalRows"]!, ValidRowCount = (int)Inserted["ValidRowCount"]!, InvalidRowCount = (int)Inserted["InvalidRowCount"]!,
                    PreviewRowsJson = (string?)Inserted["PreviewRowsJson"] }; });
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(taskId);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
            var options = Substitute.For<IOptionsMonitor<ImportExportOptions>>(); options.CurrentValue.Returns(new ImportExportOptions { MaxPreviewRows = 2 });
            Service = new(new([handler]), files, query, command, tenant, clock, ids, options);
        }
    }
}

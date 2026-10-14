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

    private sealed class Fixture
    {
        public Guid ActorId { get; } = Guid.CreateVersion7();
        public MemoryStream Probe { get; } = new([1]);
        public IReadOnlyDictionary<string, object?>? Inserted { get; private set; }
        public ImportExportTaskManagementService Service { get; }

        public Fixture(StaticImportPreviewResult? preview, Action? afterPreview = null, bool returnNullResult = false)
        {
            var taskId = Guid.CreateVersion7(); var tenantId = Guid.CreateVersion7(); var fileId = Guid.CreateVersion7();
            var tenant = new CurrentTenantAccessor(); tenant.SetTenant(new(tenantId, "acme", "Acme"));
            var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("positions");
            handler.GetDefinition().Returns(new StaticImportSchemaDefinition("positions", "Positions", "tenant", "positions.import", [new("data", "Data", ["value"])]));
            handler.PreviewAsync(Arg.Any<Stream>(), Arg.Any<long>(), Arg.Any<StaticImportPreviewContext>(), Arg.Any<CancellationToken>())
                .Returns(_ => { afterPreview?.Invoke(); return Task.FromResult(returnNullResult ? null! : Result<StaticImportPreviewResult>.Success(preview!)); });
            var files = Substitute.For<ITenantResourceFileStore>();
            files.UploadAsync("import_export", taskId, ActorId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Stream>(), 1, Arg.Any<CancellationToken>())
                .Returns(Result<TenantResourceFileReference>.Success(new(fileId, 1, "hash")));
            files.OpenReadyContentAsync("import_export", taskId, fileId, Arg.Any<CancellationToken>())
                .Returns(Result<TenantResourceFileContent>.Success(new(Probe, "application/test", "positions.xlsx")));
            var command = Substitute.For<ICommandExecutor>();
            command.ExecuteAsync(ImportExportTaskSql.Insert, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(call => { Inserted = (IReadOnlyDictionary<string, object?>)call.ArgAt<object>(1); return 1; });
            var query = Substitute.For<IQueryExecutor>();
            query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => new ImportExportTaskRecord { Id = taskId, TenantId = tenantId, SourceFileId = fileId, RequestedByUserId = ActorId,
                    StatusKey = (string)Inserted!["StatusKey"]!, ErrorCode = (string?)Inserted["ErrorCode"],
                    TotalRows = (int)Inserted["TotalRows"]!, ValidRowCount = (int)Inserted["ValidRowCount"]!, InvalidRowCount = (int)Inserted["InvalidRowCount"]!,
                    PreviewRowsJson = (string?)Inserted["PreviewRowsJson"] });
            var ids = Substitute.For<IIdGenerator>(); ids.NewId().Returns(taskId);
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
            var options = Substitute.For<IOptionsMonitor<ImportExportOptions>>(); options.CurrentValue.Returns(new ImportExportOptions { MaxPreviewRows = 2 });
            Service = new(new([handler]), files, query, command, tenant, clock, ids, options);
        }
    }
}

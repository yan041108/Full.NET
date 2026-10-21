using System.IO.Compression;
using System.Security.Claims;
using System.Xml.Linq;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.ImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Reporting.Configuration;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实双库验证导入计划恢复与独立作用域续租，慢文件及生成器只作为外部工作门闩。</summary>
internal static class PerformanceImportExecutionAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, DatabaseProvider provider, Action<string> evidence)
    {
        await using var observer = factory.Services.CreateAsyncScope();
        var sp = observer.ServiceProvider;
        sp.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
        var tenantId = await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<Guid>(
            Global("tenant", "SELECT Id FROM fn_tenancy_tenant WHERE Identifier='acme'"));
        Assert.AreNotEqual(Guid.Empty, tenantId);
        await BindAsync(sp, tenantId);
        await VerifyPlanAsync(factory, tenantId, evidence);
        // 两种执行器使用相同真实时钟、领取与续租 SQL，分别覆盖各自状态谓词。
        foreach (var reporting in new[] { false, true })
            await VerifyLeaseAsync(factory, provider, tenantId, reporting, evidence);
    }

    private static async Task VerifyPlanAsync(FullNetApiFactory factory, Guid tenantId, Action<string> evidence)
    {
        Guid taskId;
        Guid sourceId;
        string prepared;
        var actor = Guid.CreateVersion7();
        var prefix = "perf-" + Guid.CreateVersion7().ToString("N");
        IReadOnlyDictionary<string, bool> capabilities = new Dictionary<string, bool>
        {
            [OrganizationPositionManagementPermissions.AssignUnit] = true,
            [OrganizationUnitManagementPermissions.Read] = true,
        };
        await using (var first = factory.Services.CreateAsyncScope())
        {
            var sp = first.ServiceProvider;
            await BindAsync(sp, tenantId);
            var handler = Handler(sp);
            var workbook = Workbook(handler, prefix);
            taskId = Guid.CreateVersion7();
            await using var content = new MemoryStream(workbook);
            var uploaded = await sp.GetRequiredService<ITenantResourceFileStore>().UploadAsync(
                "import_export", taskId, actor, "positions.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                content, content.Length);
            Assert.IsTrue(uploaded.IsSuccess, uploaded.Error?.Message);
            sourceId = uploaded.Value!.FileId;
            await InsertImportAsync(sp, taskId, tenantId, actor, sourceId, 3, capabilities);
            Assert.AreEqual(1, await ImportRunner(sp).ProcessPendingAsync(default));
            await BindAsync(sp, tenantId);
            var stored = await ImportAsync(sp, taskId);
            Assert.AreEqual(1, stored.NextLineNumber);
            Assert.AreEqual("queued", stored.StatusKey);
            prepared = ImportExportTaskMapper.DeserializeExecutionState(stored.ExecutionRowsJson).PreparedPlanJson!;
            Assert.IsFalse(string.IsNullOrWhiteSpace(prepared), "首批必须提交可恢复计划。");
            // 模拟业务行已提交但任务检查点丢失；保留已持久化计划，恢复必须重放同一行回执。
            await sp.GetRequiredService<ICommandExecutor>().ExecuteAsync(Tenant("rewind", """
                UPDATE fn_import_export_task SET NextLineNumber=0, ProcessedRowCount=0, SucceededRowCount=0,
                    ExecutionRowsJson=@State WHERE TenantId=@TenantId AND Id=@Id
                """), P(("Id", taskId), ("State", ImportExportTaskMapper.SerializeExecutionState(
                    new ImportExportExecutionStateDocument(capabilities, []) { PreparedPlanJson = prepared }))));
        }

        for (var batch = 0; batch < 3; batch++)
        {
            // 每批重建作用域和 Runner；源文件仍经真实所有权检查，但任何再次读取正文都会使测试失败。
            await using var resumed = factory.Services.CreateAsyncScope();
            var sp = resumed.ServiceProvider;
            await BindAsync(sp, tenantId);
            var files = new ObservedFiles(sp.GetRequiredService<ITenantResourceFileStore>()) { RejectReads = true };
            Assert.AreEqual(1, await ImportRunner(sp, files).ProcessPendingAsync(default));
            Assert.AreEqual(0, files.ReadAttempts);
            await BindAsync(sp, tenantId);
            var stored = await ImportAsync(sp, taskId);
            Assert.AreEqual(batch + 1, stored.NextLineNumber);
            Assert.AreEqual(prepared, ImportExportTaskMapper.DeserializeExecutionState(stored.ExecutionRowsJson).PreparedPlanJson);
            Assert.AreEqual(batch == 2 ? "execution_succeeded" : "queued", stored.StatusKey);
        }

        await using var final = factory.Services.CreateAsyncScope();
        var finalSp = final.ServiceProvider;
        await BindAsync(finalSp, tenantId);
        var query = finalSp.GetRequiredService<IQueryExecutor>();
        Assert.AreEqual(3L, await query.QuerySingleOrDefaultAsync<long>(Tenant("receipts", """
            SELECT COUNT(*) FROM fn_organization_position_import WHERE TenantId=@TenantId AND TaskId=@Id
            """), P(("Id", taskId))));
        Assert.AreEqual(3L, await query.QuerySingleOrDefaultAsync<long>(Tenant("positions", """
            SELECT COUNT(*) FROM fn_organization_position WHERE TenantId=@TenantId AND Code LIKE @Prefix
            """), P(("Prefix", prefix + "%"))));
        var context = new StaticImportPreviewContext(actor, capabilities)
            { TaskId = taskId, SourceFileId = sourceId, PreparedPlanJson = prepared };
        foreach (var invalid in new[]
        {
            context with { TaskId = Guid.CreateVersion7() },
            context with { SourceFileId = Guid.CreateVersion7() },
            // 显式撤销 AssignUnit，旧计划不能保留原来的关联写权限。
            context with { CapabilityFlags = new Dictionary<string, bool>() },
        })
        {
            await using var unreadable = new RejectReadStream(new MemoryStream([1]), () => Assert.Fail("绑定失败不得重读源文件。"));
            var rejected = await Handler(finalSp).ExecuteBatchAsync(unreadable, 1, 0, 1, invalid);
            Assert.IsFalse(rejected.IsSuccess);
            Assert.AreEqual(OrganizationErrorCodes.PositionImportWorkbookInvalid, rejected.Error!.Code);
        }
        Assert.AreEqual(3L, await query.QuerySingleOrDefaultAsync<long>(Tenant("receipts_after_rejection", """
            SELECT COUNT(*) FROM fn_organization_position_import WHERE TenantId=@TenantId AND TaskId=@Id
            """), P(("Id", taskId))));
        evidence("import_plan rows=3 batch=1 recreated_scopes=3 replayed_receipt=1 subsequent_body_reads=0 binding_rejections=3 errors=0");
    }

    private static async Task VerifyLeaseAsync(FullNetApiFactory factory, DatabaseProvider provider, Guid tenantId,
        bool reporting, Action<string> evidence)
    {
        await using var worker = factory.Services.CreateAsyncScope();
        await using var observer = factory.Services.CreateAsyncScope();
        await using var contender = factory.Services.CreateAsyncScope();
        var sp = worker.ServiceProvider;
        var watch = observer.ServiceProvider;
        await BindAsync(sp, tenantId);
        await BindAsync(watch, tenantId);
        await BindAsync(contender.ServiceProvider, tenantId);
        var taskId = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        var gate = new WorkGate();
        var files = new ObservedFiles(sp.GetRequiredService<ITenantResourceFileStore>()) { Gate = reporting ? null : gate };
        var table = reporting ? "fn_reporting_export_task" : "fn_import_export_task";
        Task<int> running;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        if (reporting)
        {
            await sp.GetRequiredService<ICommandExecutor>().ExecuteAsync(ReportingExportTaskSql.InsertFor(provider),
                new ReportingExportTaskRecord { Id = taskId, TenantId = tenantId, DefinitionId = Guid.CreateVersion7(),
                    VersionNumber = 1, DefinitionKey = "lease-proof", DefinitionName = "lease-proof", FormatKey = "excel",
                    StatusKey = "queued", RequestedByUserId = actor, CreatedAtUtc = sp.GetRequiredService<IClock>().UtcNow,
                    ActorPermissionCodesJson = "[]", Version = 1 });
            running = ActivatorUtilities.CreateInstance<ReportingExportTaskRunner>(sp, new SlowWorkbook(gate),
                new FixedMonitor<ReportingExportOptions>(new() { BatchSize = 1, LeaseSeconds = 30, ExecutionTimeoutSeconds = 90 }))
                .ProcessPendingAsync(deadline.Token);
        }
        else
        {
            var bytes = Workbook(Handler(sp), "lease-" + taskId.ToString("N"));
            await using var stream = new MemoryStream(bytes);
            var source = await sp.GetRequiredService<ITenantResourceFileStore>().UploadAsync("import_export", taskId, actor,
                "lease.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", stream, stream.Length);
            Assert.IsTrue(source.IsSuccess, source.Error?.Message);
            await InsertImportAsync(sp, taskId, tenantId, actor, source.Value!.FileId, 3);
            running = ImportRunner(sp, files, 30).ProcessPendingAsync(deadline.Token);
        }
        try
        {
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var initial = await ReadLeaseAsync(watch, table, taskId);
            Assert.IsNotNull(initial.LeaseId);
            Assert.IsNotNull(initial.LeaseExpiresAtUtc);
            // 真实等待初始租约到期，不能用预置过期行替代生产控制面在执行中的续租。
            while (watch.GetRequiredService<IClock>().UtcNow <= initial.LeaseExpiresAtUtc!.Value.AddMilliseconds(500))
                await Task.Delay(100, deadline.Token);
            var renewed = await ReadLeaseAsync(watch, table, taskId);
            Assert.AreEqual(initial.LeaseId, renewed.LeaseId);
            Assert.IsTrue(renewed.LeaseExpiresAtUtc > initial.LeaseExpiresAtUtc);
            Assert.IsTrue(renewed.LeaseExpiresAtUtc > watch.GetRequiredService<IClock>().UtcNow);
            var competing = reporting
                ? await contender.ServiceProvider.GetRequiredService<ReportingExportTaskRunner>().ProcessPendingAsync(deadline.Token)
                : await contender.ServiceProvider.GetRequiredService<ImportExportTaskRunner>().ProcessPendingAsync(deadline.Token);
            Assert.AreEqual(0, competing, "初始期限后仍由续租持有者执行。");
            var replacement = Guid.CreateVersion7();
            await watch.GetRequiredService<ICommandExecutor>().ExecuteAsync(Tenant("replace_lease", $"""
                UPDATE {table} SET LeaseId=@LeaseId, LeaseExpiresAtUtc=@Expiry WHERE TenantId=@TenantId AND Id=@Id
                """), P(("Id", taskId), ("LeaseId", replacement), ("Expiry", watch.GetRequiredService<IClock>().UtcNow.AddMinutes(2))));
            await gate.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            gate.Release.TrySetResult();
            Assert.AreEqual(1, await running.WaitAsync(TimeSpan.FromSeconds(15)));
            var commands = watch.GetRequiredService<ICommandExecutor>();
            var now = watch.GetRequiredService<IClock>().UtcNow;
            Assert.AreEqual(0, await commands.ExecuteAsync(reporting
                ? ReportingExportTaskSql.RenewExecutionLease : ImportExportTaskSql.RenewExecutionLease,
                P(("Id", taskId), ("LeaseId", initial.LeaseId), ("NowUtc", now), ("LeaseExpiresAtUtc", now.AddMinutes(3)))),
                "旧租约不能通过真实 SQL 再次续期。");
            var completion = reporting
                ? provider == DatabaseProvider.SqlServer ? ReportingExportTaskSql.CompleteFailedSqlServer : ReportingExportTaskSql.CompleteFailed
                : ImportExportTaskSql.MarkExecutionFailed;
            Assert.AreEqual(0, await commands.ExecuteAsync(completion, P(("Id", taskId), ("LeaseId", initial.LeaseId),
                ("NowUtc", now), ("StatusKey", "failed"), ("RowCount", 0), ("ErrorCode", "test.stale"),
                ("ErrorMessage", "stale owner"), ("CompletedAtUtc", now), ("ExecutionCompletedAtUtc", now))),
                "旧租约不能通过真实 SQL 提交终态。");
            var unchanged = await ReadLeaseAsync(watch, table, taskId);
            Assert.AreEqual(replacement, unchanged.LeaseId);
            Assert.AreEqual(reporting ? "processing" : "executing", unchanged.StatusKey);
            if (!reporting) Assert.AreEqual(0, (await ImportAsync(watch, taskId)).ProcessedRowCount);
            else
            {
                var stored = await watch.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
                    ReportingExportTaskSql.FindByIdFor(provider), P(("Id", taskId)));
                Assert.IsNotNull(stored);
                Assert.IsNull(stored.OutputFileId);
                Assert.IsNull(stored.CompletedAtUtc);
            }
            evidence($"lease module={(reporting ? "reporting" : "import_export")} initial_seconds=30 renewed=true second_scope_claims=0 lost_owner_cancelled=true stale_completion=false");
        }
        finally
        {
            deadline.Cancel();
            gate.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(15));
            // 移除本场景遗留队列项，避免后续模块场景再次领取故意留下的新租约。
            await watch.GetRequiredService<ICommandExecutor>().ExecuteAsync(Tenant("delete_lease_fixture",
                $"DELETE FROM {table} WHERE TenantId=@TenantId AND Id=@Id"), P(("Id", taskId)));
        }
    }

    private static ImportExportTaskRunner ImportRunner(IServiceProvider sp, ITenantResourceFileStore? files = null, int leaseSeconds = 120) =>
        ActivatorUtilities.CreateInstance<ImportExportTaskRunner>(sp, files ?? sp.GetRequiredService<ITenantResourceFileStore>(),
            new FixedMonitor<ImportExportOptions>(new() { BatchSize = 1, LeaseSeconds = leaseSeconds, ExecutionTimeoutSeconds = 90 }));

    private static async Task BindAsync(IServiceProvider sp, Guid tenantId)
    {
        var tenant = await sp.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(tenantId);
        Assert.IsNotNull(tenant);
        sp.GetRequiredService<ICurrentTenantContextWriter>().SetTenant(tenant);
    }

    private static IStaticImportSchemaHandler Handler(IServiceProvider sp) => sp.GetServices<IStaticImportSchemaHandler>()
        .Single(handler => handler.SchemaKey == StaticImportSchemaKeys.OrganizationTenantPositions);

    private static async Task<ImportExportTaskRecord> ImportAsync(IServiceProvider sp, Guid id)
    {
        var row = await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, P(("Id", id)));
        Assert.IsNotNull(row);
        return row;
    }

    private static Task<int> InsertImportAsync(IServiceProvider sp, Guid id, Guid tenantId, Guid actor, Guid source, int count,
        IReadOnlyDictionary<string, bool>? capabilities = null) =>
        sp.GetRequiredService<ICommandExecutor>().ExecuteAsync(ImportExportTaskSql.Insert, new ImportExportTaskRecord
        { Id = id, TenantId = tenantId, SchemaKey = StaticImportSchemaKeys.OrganizationTenantPositions,
            SchemaDisplayName = "positions", WorksheetKey = "positions", SourceFileId = source, SourceFileName = "positions.xlsx",
            StatusKey = "queued", TotalRows = count, ValidRowCount = count, PreviewRowsJson = "[]", RequestedByUserId = actor,
            CreatedAtUtc = sp.GetRequiredService<IClock>().UtcNow, Version = 1,
            ExecutionRowsJson = ImportExportTaskMapper.SerializeExecutionState(new ImportExportExecutionStateDocument(capabilities ?? new Dictionary<string, bool>(), [])) });

    private static async Task<LeaseRow> ReadLeaseAsync(IServiceProvider sp, string table, Guid id)
    {
        var row = await sp.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<LeaseRow>(Tenant("lease", $"""
            SELECT StatusKey, LeaseId, LeaseExpiresAtUtc FROM {table} WHERE TenantId=@TenantId AND Id=@Id
            """), P(("Id", id)));
        Assert.IsNotNull(row);
        return row;
    }

    private static byte[] Workbook(IStaticImportSchemaHandler handler, string prefix)
    {
        using var output = new MemoryStream();
        output.Write(handler.CreateTemplate("positions"));
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument xml;
            using (var input = entry.Open()) xml = XDocument.Load(input);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            for (var index = 1; index <= 3; index++)
                xml.Descendants(ns + "sheetData").Single().Add(new XElement(ns + "row", new XAttribute("r", index + 1),
                    new[] { prefix + "-" + index, "岗位 " + index, "0", "", "" }.Select((value, column) =>
                        new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + column)}{index + 1}"),
                            new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value))))));
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry("xl/worksheets/sheet1.xml").Open());
            writer.Write(xml.ToString());
        }
        return output.ToArray();
    }

    private static SqlStatement Tenant(string name, string sql) => new("test.performance.import." + name, sql,
        SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
    private static SqlStatement Global(string name, string sql) => new("test.performance.import." + name, sql, SqlDataScope.Global);
    private static Dictionary<string, object?> P(params (string Key, object? Value)[] values) => values.ToDictionary(value => value.Key, value => value.Value);
    private sealed class LeaseRow
    {
        public string StatusKey { get; set; } = "";
        public Guid? LeaseId { get; set; }
        public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    }
    private sealed class FixedMonitor<T>(T value) : IOptionsMonitor<T> where T : class
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
    private sealed class WorkGate
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async Task WaitAsync(CancellationToken token)
        {
            using var registration = token.Register(() => Cancelled.TrySetResult());
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(95));
        }
    }
    private sealed class SlowWorkbook(WorkGate gate) : IReportingExportWorkbookSource
    {
        public async Task<Result<ReportingExportGeneratedFile>> GenerateAsync(ReportingExportTaskRecord task, ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return Result<ReportingExportGeneratedFile>.Failure(new Error("test.lease_lost", "Stopped work", ErrorType.Unexpected));
        }
    }
    private sealed class ObservedFiles(ITenantResourceFileStore inner) : ITenantResourceFileStore
    {
        internal WorkGate? Gate { get; init; }
        internal bool RejectReads { get; init; }
        internal int ReadAttempts { get; private set; }
        public async Task<Result<TenantResourceFileContent>> OpenReadyContentAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default)
        {
            if (Gate is not null) await Gate.WaitAsync(cancellationToken);
            var result = await inner.OpenReadyContentAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
            if (!result.IsSuccess || !RejectReads) return result;
            var file = result.Value!;
            return Result<TenantResourceFileContent>.Success(file with
                { Content = new RejectReadStream(file.Content, () => ReadAttempts++) });
        }
        public Task<Result<TenantResourceFileReference>> UploadAsync(string ownerModuleKey, Guid resourceId, Guid actorUserId, string originalFileName,
            string contentType, Stream content, long contentLength, CancellationToken cancellationToken = default) =>
            inner.UploadAsync(ownerModuleKey, resourceId, actorUserId, originalFileName, contentType, content, contentLength, cancellationToken);
        public Task<IReadOnlyList<TenantResourceFileReadyItem>> ListReadyAsync(string ownerModuleKey, Guid resourceId, CancellationToken cancellationToken = default) =>
            inner.ListReadyAsync(ownerModuleKey, resourceId, cancellationToken);
        public Task ReleaseAsync(string ownerModuleKey, Guid resourceId, Guid fileId, CancellationToken cancellationToken = default) =>
            inner.ReleaseAsync(ownerModuleKey, resourceId, fileId, cancellationToken);
    }
    private sealed class RejectReadStream(Stream inner, Action onRead) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) { onRead(); throw new InvalidOperationException("恢复计划不得再次读取工作簿正文。"); }
        public override int Read(Span<byte> buffer) { onRead(); throw new InvalidOperationException("恢复计划不得再次读取工作簿正文。"); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { onRead(); throw new InvalidOperationException("恢复计划不得再次读取工作簿正文。"); }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    }
}

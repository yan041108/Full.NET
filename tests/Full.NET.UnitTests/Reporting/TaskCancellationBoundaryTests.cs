using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Contracts;
using Full.NET.Modules.Document.Features.ManageHostDocumentPreviewTasks;
using Full.NET.Modules.Document.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>三种任务停止入口共享取消和原作用域约束，但保留各自的状态与 CAS SQL。</summary>
[TestClass]
public sealed class TaskCancellationBoundaryTests
{
    [TestMethod]
    [DataRow("reporting-sqlserver", "entry")]
    [DataRow("reporting-mysql", "entry")]
    [DataRow("import", "entry")]
    [DataRow("document", "entry")]
    [DataRow("reporting-sqlserver", "load")]
    [DataRow("reporting-mysql", "load")]
    [DataRow("import", "load")]
    [DataRow("document", "load")]
    [DataRow("reporting-sqlserver", "load-missing")]
    [DataRow("reporting-mysql", "load-missing")]
    [DataRow("import", "load-missing")]
    [DataRow("document", "load-missing")]
    [DataRow("reporting-sqlserver", "identity")]
    [DataRow("reporting-mysql", "identity")]
    [DataRow("import", "identity")]
    [DataRow("document", "identity")]
    [DataRow("reporting-sqlserver", "identity-missing")]
    [DataRow("reporting-mysql", "identity-missing")]
    [DataRow("import", "identity-missing")]
    [DataRow("document", "identity-missing")]
    [DataRow("reporting-sqlserver", "write-zero")]
    [DataRow("reporting-mysql", "write-zero")]
    [DataRow("import", "write-zero")]
    [DataRow("document", "write-zero")]
    [DataRow("reporting-sqlserver", "write-one")]
    [DataRow("reporting-mysql", "write-one")]
    [DataRow("import", "write-one")]
    [DataRow("document", "write-one")]
    [DataRow("reporting-sqlserver", "reload")]
    [DataRow("reporting-mysql", "reload")]
    [DataRow("import", "reload")]
    [DataRow("document", "reload")]
    [DataRow("reporting-sqlserver", "reload-missing")]
    [DataRow("reporting-mysql", "reload-missing")]
    [DataRow("import", "reload-missing")]
    [DataRow("document", "reload-missing")]
    public async Task Cancellation_is_not_replaced_by_missing_authority_or_cas_status(string module, string stage)
    {
        using var fixture = new Fixture(module, stage);
        if (stage == "entry") fixture.Cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Cancel());
        var wrote = stage.StartsWith("write", StringComparison.Ordinal) || stage.StartsWith("reload", StringComparison.Ordinal);
        Assert.AreEqual(wrote ? 1 : 0, fixture.Writes);
        Assert.AreEqual(stage == "entry" ? 0 : stage.StartsWith("reload", StringComparison.Ordinal) ? 2 : 1, fixture.Reads);
        // 写端口返回后取消不代表回滚；只证明不再执行后续读取或返回普通状态结果。
    }

    [TestMethod]
    [DataRow("reporting-sqlserver")]
    [DataRow("reporting-mysql")]
    [DataRow("import")]
    [DataRow("document")]
    public async Task Cancellation_still_propagates_when_task_is_already_cancelled_and_authority_returns_null(string module)
    {
        using var fixture = new Fixture(module, "identity-missing", terminal: true);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Cancel());
        Assert.AreEqual(0, fixture.Writes);
    }

    [TestMethod]
    [DataRow("reporting-sqlserver", "load")]
    [DataRow("reporting-mysql", "load")]
    [DataRow("import", "load")]
    [DataRow("document", "load")]
    [DataRow("reporting-sqlserver", "identity-original")]
    [DataRow("reporting-mysql", "identity-original")]
    [DataRow("import", "identity-original")]
    [DataRow("document", "identity-original")]
    [DataRow("reporting-sqlserver", "identity-changed")]
    [DataRow("reporting-mysql", "identity-changed")]
    [DataRow("import", "identity-changed")]
    [DataRow("document", "identity-changed")]
    [DataRow("reporting-sqlserver", "write")]
    [DataRow("reporting-mysql", "write")]
    [DataRow("import", "write")]
    [DataRow("document", "write")]
    [DataRow("reporting-sqlserver", "reload")]
    [DataRow("reporting-mysql", "reload")]
    [DataRow("import", "reload")]
    [DataRow("document", "reload")]
    [DataRow("reporting-sqlserver", "reload-missing")]
    [DataRow("reporting-mysql", "reload-missing")]
    [DataRow("import", "reload-missing")]
    [DataRow("document", "reload-missing")]
    public async Task Original_scope_is_required_before_next_read_write_and_terminal_result(string module, string stage)
    {
        using var fixture = new Fixture(module, "scope-" + stage);
        var result = await fixture.Cancel();
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.ErrorCode);
        Assert.AreEqual(stage is "write" or "reload" or "reload-missing" ? 1 : 0, fixture.Writes);
        Assert.AreEqual(stage.StartsWith("reload", StringComparison.Ordinal) ? 2 : 1, fixture.Reads);
        Assert.AreEqual(stage == "load" ? 0 : 1, fixture.IdentityCalls);
        Assert.AreNotEqual(fixture.Binding.TenantId, fixture.Tenant.Id);
    }

    [TestMethod]
    public async Task Import_empty_security_stamp_is_denied_without_identity_or_write()
    {
        using var fixture = new Fixture("import", "allowed");
        var result = await fixture.Cancel(fixture.Binding with { SecurityStamp = "" });
        Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.ErrorCode);
        Assert.AreEqual(0, fixture.Writes);
        Assert.AreEqual(0, fixture.IdentityCalls);
    }

    [TestMethod]
    [DataRow("reporting-sqlserver", "allowed")]
    [DataRow("reporting-mysql", "allowed")]
    [DataRow("import", "allowed")]
    [DataRow("document", "allowed")]
    [DataRow("reporting-sqlserver", "cas-lost")]
    [DataRow("reporting-mysql", "cas-lost")]
    [DataRow("import", "cas-lost")]
    [DataRow("document", "cas-lost")]
    [DataRow("reporting-sqlserver", "claimed")]
    [DataRow("reporting-mysql", "claimed")]
    [DataRow("import", "claimed")]
    [DataRow("document", "claimed")]
    [DataRow("reporting-sqlserver", "already-cancelled")]
    [DataRow("reporting-mysql", "already-cancelled")]
    [DataRow("import", "already-cancelled")]
    [DataRow("document", "already-cancelled")]
    public async Task Normal_cancel_replay_and_claim_conflict_retain_original_semantics(string module, string kind)
    {
        using var fixture = new Fixture(module, kind);
        var result = await fixture.Cancel();
        Assert.AreEqual(kind != "claimed", result.Succeeded);
        if (kind == "claimed") Assert.AreEqual(ErrorType.Conflict, result.Type);
        else Assert.AreEqual("cancelled", result.Status);
        Assert.AreEqual(kind == "already-cancelled" ? 0 : 1, fixture.Writes);
        Assert.AreEqual(1, fixture.IdentityCalls);
    }

    private sealed record Outcome(bool Succeeded, string? Status, string? ErrorCode, ErrorType? Type);

    private sealed class Fixture : IDisposable
    {
        public CurrentTenantAccessor Tenant { get; } = new();
        public SessionBindingSnapshot Binding { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public int IdentityCalls { get; private set; }
        private readonly string module;
        private readonly string stage;
        private readonly bool terminal;
        private readonly Guid id = Guid.CreateVersion7();
        private readonly Guid source = Guid.CreateVersion7();
        private readonly Guid output = Guid.CreateVersion7();
        private readonly ReportingExportTaskCancellationService reporting;
        private readonly ImportExportTaskCancellationService import;
        private readonly HostDocumentPreviewTaskCancellationService document;

        public Fixture(string module, string stage, bool terminal = false)
        {
            this.module = module; this.stage = stage; this.terminal = terminal;
            if (module == "document") Tenant.SetHost(); else Tenant.SetTenant(new(Guid.CreateVersion7(), "acme", "Acme"));
            Binding = new(Guid.CreateVersion7(), Tenant.Id, Guid.CreateVersion7(), "stamp", "host", module == "document" ? "host" : $"tenant:{Tenant.Id:N}");
            var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
            var identity = Substitute.For<IBackgroundSessionAuthorization>();
            var clock = Substitute.For<IClock>(); clock.UtcNow.Returns(DateTimeOffset.Parse("2026-10-15T00:00:00Z"));
            var provider = module == "reporting-mysql" ? DatabaseProvider.MySql : DatabaseProvider.SqlServer;
            query.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(ReportingExportTaskSql.FindByIdFor(provider), Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Load((tenant, status) => new ReportingExportTaskRecord { Id = id, TenantId = tenant!.Value,
                    RequestedByUserId = Binding.UserId, StatusKey = status, Version = Writes > 0 ? 5 : 4, OutputFileId = output }));
            query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Load((tenant, status) => new ImportExportTaskRecord { Id = id, TenantId = tenant!.Value,
                    RequestedByUserId = Binding.UserId, StatusKey = status, Version = Writes > 0 ? 5 : 4,
                    SourceFileId = source, ErrorReceiptFileId = output, ExecutionRowsJson = "[]" }));
            query.QuerySingleOrDefaultAsync<DocumentPreviewTaskRecord>(DocumentPreviewTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(_ => Load((_, status) => new DocumentPreviewTaskRecord { Id = id, RequestedByUserId = Binding.UserId,
                    StatusKey = status, Version = Writes > 0 ? 5 : 4, SourceFileId = source, OutputFileId = output }));
            var permission = module == "import" ? ImportExportPermissions.ImportTasksCancel : module == "document"
                ? HostDocumentPreviewTaskPermissions.Cancel : ReportingExportTaskPermissions.Cancel;
            identity.AuthorizeAsync(Arg.Any<SessionBindingSnapshot>(), permission, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                IdentityCalls++;
                if (stage is "identity" or "identity-missing") Cancellation.Cancel();
                if (stage == "identity-missing") return null;
                if (stage is "scope-identity-original" or "scope-identity-changed") ChangeScope();
                return new AuthorizedSessionActor(Binding.UserId, stage == "scope-identity-changed" ? Tenant.Id : Binding.TenantId, Binding.SessionId);
            });
            command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                Writes++;
                if (stage is "write-zero" or "write-one") Cancellation.Cancel();
                if (stage == "scope-write") ChangeScope();
                return stage is "write-zero" or "cas-lost" or "claimed" ? 0 : 1;
            });
            reporting = new(query, command, Tenant, identity, clock, Options.Create(new DatabaseOptions { Provider = provider }));
            import = new(query, command, Tenant, identity, clock);
            document = new(query, command, Tenant, identity, clock);
        }

        private T? Load<T>(Func<Guid?, string, T> build) where T : class
        {
            Reads++;
            if (Reads == 1 && (stage is "load" or "load-missing") || Reads == 2 && (stage is "reload" or "reload-missing")) Cancellation.Cancel();
            if (stage == "scope-load" || (stage is "scope-reload" or "scope-reload-missing") && Reads == 2) ChangeScope();
            if (stage == "load-missing" || (stage is "reload-missing" or "scope-reload-missing") && Reads == 2) return null;
            var status = terminal || stage == "already-cancelled" || Writes > 0 ? "cancelled" : module == "document" ? "pending" : "queued";
            if (Writes > 0 && stage == "claimed") status = module == "import" ? "executing" : "processing";
            return build(stage == "scope-reload" && Reads == 2 ? Tenant.Id : Binding.TenantId, status);
        }

        private void ChangeScope() => Tenant.SetTenant(new(Guid.CreateVersion7(), "other", "Other"));
        public async Task<Outcome> Cancel(SessionBindingSnapshot? binding = null)
        {
            if (module == "import") return Map(await import.CancelAsync(id, binding ?? Binding, Cancellation.Token), value => value.StatusKey);
            if (module == "document") return Map(await document.CancelAsync(id, binding ?? Binding, Cancellation.Token), value => value.StatusKey);
            return Map(await reporting.CancelAsync(id, binding ?? Binding, Cancellation.Token), value => value.StatusKey);
        }
        private static Outcome Map<T>(Result<T> result, Func<T, string> status) =>
            new(result.IsSuccess, result.IsSuccess ? status(result.Value!) : null, result.Error?.Code, result.Error?.Type);
        public void Dispose() => Cancellation.Dispose();
    }
}

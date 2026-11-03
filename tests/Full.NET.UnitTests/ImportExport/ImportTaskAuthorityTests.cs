using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Configuration;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.ImportExport.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.ImportExport;

/// <summary>原上传者、当前来源权限与显式会话绑定不能被通用任务权限或恢复参数替代。</summary>
[TestClass]
public sealed class ImportTaskAuthorityTests
{
    [TestMethod]
    public async Task Source_revocation_missing_binding_and_foreign_takeover_fail_closed()
    {
        var fixture = new Fixture();
        Assert.IsNull(await fixture.Authority.ValidateCreationAsync(fixture.Definition, fixture.Context, default));
        await fixture.Sessions.Received(1).AuthorizeAllAsync(fixture.Binding,
            Arg.Is<IReadOnlyCollection<string>>(permissions => permissions != null && permissions.Contains("source.import") && permissions.Contains(ImportExportPermissions.ImportTasksCreate)), default);
        Assert.IsTrue((await fixture.Authority.ValidateQueueAsync(fixture.Task, fixture.Context, default)).IsSuccess);
        Assert.IsFalse((await fixture.Authority.ValidateQueueAsync(fixture.Task, fixture.Context with { RequestedByUserId = Guid.CreateVersion7() }, default)).IsSuccess);
        Assert.IsFalse((await fixture.Authority.ValidateStoredAsync(fixture.Task, default)).IsSuccess);
        fixture.Task = fixture.Record(binding: JsonSerializer.Serialize(fixture.Binding, ImportExportJsonSerializerContext.Default.SessionBindingSnapshot));
        Assert.IsTrue((await fixture.Authority.ValidateStoredAsync(fixture.Task, default)).IsSuccess);
        fixture.Sessions.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns((AuthorizedSessionActor?)null);
        Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, (await fixture.Authority.ValidateStoredAsync(fixture.Task, default)).Error!.Code);
        Assert.IsNotNull(await fixture.Authority.ValidateCreationAsync(fixture.Definition, fixture.Context, default));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Explicit_recovery_preserves_frozen_capabilities_and_plan_and_rebinds_only_current_owner(bool retry)
    {
        var fixture = new Fixture(); var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        fixture.Task = fixture.Record(ImportExportTaskStatusKeys.ExecutionPartial, execution: ImportExportTaskMapper.SerializeExecutionState(new(new Dictionary<string, bool> { ["assign"] = false },
            [new(2, true, Guid.CreateVersion7(), null, null)]) { PreparedPlanJson = "{\"frozen\":true}" }), next: 1);
        query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(Arg.Any<SqlStatement>(), Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(fixture.Task);
        Dictionary<string, object?>? written = null;
        command.ExecuteAsync(ImportExportTaskSql.QueueExecution, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call => { written = (Dictionary<string, object?>)call[1]!; return 1; });
        var service = new ImportExportTaskExecutionService(query, command, Substitute.For<ITenantResourceFileStore>(), fixture.Tenant,
            fixture.Authority, Substitute.For<IServiceScopeFactory>(), new Monitor(new ImportExportOptions { RunSynchronously = false }));
        var context = fixture.Context with { CapabilityFlags = new Dictionary<string, bool> { ["assign"] = true }, SessionBinding = fixture.Binding with { SessionId = Guid.CreateVersion7() } };
        Assert.IsTrue((retry ? await service.RetryAsync(fixture.Task.Id, context) : await service.ResumeAsync(fixture.Task.Id, context)).IsSuccess);
        Assert.IsNotNull(written);
        var saved = ImportExportTaskMapper.DeserializeExecutionState((string)written["ExecutionRowsJson"]!);
        Assert.AreEqual("{\"frozen\":true}", saved.PreparedPlanJson); Assert.IsFalse(saved.CapabilityFlags!["assign"]);
        Assert.AreEqual(retry ? 0 : 1, saved.Rows.Count); Assert.AreEqual(retry ? 0 : 1, written["NextLineNumber"]);
        var rebound = JsonSerializer.Deserialize((string)written["ActorSessionBindingJson"]!, ImportExportJsonSerializerContext.Default.SessionBindingSnapshot);
        Assert.AreEqual(context.SessionBinding!.SessionId, rebound!.SessionId);
    }

    private sealed class Monitor(ImportExportOptions value) : IOptionsMonitor<ImportExportOptions>
    {
        public ImportExportOptions CurrentValue => value;
        public ImportExportOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ImportExportOptions, string?> listener) => null;
    }
    private sealed class Fixture
    {
        internal CurrentTenantAccessor Tenant { get; } = new();
        internal IBackgroundSessionAuthorization Sessions { get; } = Substitute.For<IBackgroundSessionAuthorization>();
        internal StaticImportSchemaDefinition Definition { get; } = new("source", "Source", "tenant", "source.import", [new("rows", "Rows", ["code"])]);
        internal ImportExportTaskRecord Task { get; set; }
        internal SessionBindingSnapshot Binding { get; }
        internal StaticImportPreviewContext Context { get; }
        internal ImportTaskAuthority Authority { get; }
        internal Fixture()
        {
            Tenant.SetTenant(new(Guid.CreateVersion7(), "test", "Test"));
            Binding = new(Guid.CreateVersion7(), Tenant.Id, Guid.CreateVersion7(), "stamp", "host", $"tenant:{Tenant.Id:N}");
            Context = new(Binding.UserId, new Dictionary<string, bool>()) { SessionBinding = Binding };
            Task = Record();
            var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("source"); handler.GetDefinition().Returns(Definition);
            Sessions.AuthorizeAllAsync(Arg.Any<SessionBindingSnapshot>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(call => { var binding = call.Arg<SessionBindingSnapshot>()!; return new AuthorizedSessionActor(binding.UserId, binding.TenantId, binding.SessionId); });
            Authority = new(new StaticImportSchemaRegistry([handler]), Sessions, Tenant);
        }
        internal ImportExportTaskRecord Record(string status = "", string? binding = null, string? execution = null, int next = 0) => new()
        { Id = Guid.CreateVersion7(), TenantId = Tenant.Id!.Value, RequestedByUserId = Binding.UserId, SchemaKey = "source", WorksheetKey = "rows",
            StatusKey = status, ActorSessionBindingJson = binding, ExecutionRowsJson = execution, NextLineNumber = next };
    }
}

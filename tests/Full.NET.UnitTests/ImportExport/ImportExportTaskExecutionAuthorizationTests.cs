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

/// <summary>恢复必须保持任务创建人与原预览能力，不能接受后才形成回执冲突或空批循环。</summary>
[TestClass]
public sealed class ImportExportTaskExecutionAuthorizationTests
{
    [TestMethod]
    [DataRow("queue", false)]
    [DataRow("retry", false)]
    [DataRow("resume", false)]
    [DataRow("queue", true)]
    [DataRow("retry", true)]
    [DataRow("resume", true)]
    public async Task Revoked_original_capability_or_changed_actor_is_rejected_before_queue(string operation, bool changedActor)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var owner = Guid.NewGuid();
        var binding = new SessionBindingSnapshot(changedActor ? Guid.NewGuid() : owner, tenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
        var task = new ImportExportTaskRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = owner, SchemaKey = "positions",
            SchemaDisplayName = "positions", WorksheetKey = "positions", SourceFileName = "source.xlsx",
            StatusKey = operation == "queue" ? ImportExportTaskStatusKeys.PreviewSucceeded
                : operation == "retry" ? ImportExportTaskStatusKeys.ExecutionFailed : ImportExportTaskStatusKeys.ExecutionPartial,
            ValidRowCount = 2,
            ExecutionRowsJson = ImportExportTaskMapper.SerializeExecutionState(new ImportExportExecutionStateDocument(
                new Dictionary<string, bool> { ["assign"] = true }, [])),
        };
        var query = Substitute.For<IQueryExecutor>(); var command = Substitute.For<ICommandExecutor>();
        query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById, Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(task);
        command.ExecuteAsync(Arg.Any<SqlStatement>(), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(1);
        var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("positions");
        handler.GetDefinition().Returns(new StaticImportSchemaDefinition("positions", "positions", "tenant", "positions.import", []));
        handler.ExecutionCapabilityPermissions.Returns(["assign"]);
        var identity = Substitute.For<IBackgroundSessionAuthorization>();
        identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(binding.UserId, tenantId, binding.SessionId));
        if (!changedActor)
            identity.AuthorizeAsync(binding, "assign", Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        var options = Substitute.For<IOptionsMonitor<ImportExportOptions>>(); options.CurrentValue.Returns(new ImportExportOptions());
        var service = new ImportExportTaskExecutionService(query, command, Substitute.For<ITenantResourceFileStore>(),
            tenant, new StaticImportSchemaRegistry([handler]), new ImportExportExecutionAuthorization(identity),
            Substitute.For<IServiceScopeFactory>(), options);
        var context = new StaticImportPreviewContext(binding.UserId, new Dictionary<string, bool> { ["assign"] = false });
        var result = operation == "queue" ? await service.QueueExecuteAsync(task.Id, context, binding)
            : operation == "retry" ? await service.RetryAsync(task.Id, context, binding) : await service.ResumeAsync(task.Id, context, binding);
        Assert.IsFalse(result.IsSuccess); Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(0, command.ReceivedCalls().Count());
    }
}

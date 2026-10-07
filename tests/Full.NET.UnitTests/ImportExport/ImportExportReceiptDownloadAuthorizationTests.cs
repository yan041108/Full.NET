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

/// <summary>错误回执可能含原业务行信息，下载仍需复核创建人和 Schema 能力。</summary>
[TestClass]
public sealed class ImportExportReceiptDownloadAuthorizationTests
{
    [TestMethod]
    [DataRow("actor")]
    [DataRow("schema")]
    [DataRow("capability")]
    [DataRow("session")]
    [DataRow("tenant")]
    [DataRow("snapshot")]
    [DataRow("allowed")]
    [DataRow("newPermission")]
    public async Task Receipt_requires_current_owner_session_and_original_capabilities(string reason)
    {
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var owner = Guid.NewGuid();
        var binding = new SessionBindingSnapshot(reason == "actor" ? Guid.NewGuid() : owner, reason == "tenant" ? Guid.NewGuid() : tenantId,
            Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
        var task = new ImportExportTaskRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = owner, SchemaKey = "positions",
            ErrorReceiptFileId = Guid.NewGuid(), ExecutionFailedRowCount = 1,
            ExecutionRowsJson = reason == "snapshot" ? "[]" : ImportExportTaskMapper.SerializeExecutionState(new(
                new Dictionary<string, bool> { ["assign"] = reason != "newPermission" }, [])),
        };
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
            Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(task);
        var files = Substitute.For<ITenantResourceFileStore>();
        files.OpenReadyContentAsync("import_export", task.Id, task.ErrorReceiptFileId.Value, Arg.Any<CancellationToken>())
            .Returns(Result<TenantResourceFileContent>.Success(new(new MemoryStream([1]), "application/test", "errors.xlsx")));
        var handler = Substitute.For<IStaticImportSchemaHandler>(); handler.SchemaKey.Returns("positions");
        handler.GetDefinition().Returns(new StaticImportSchemaDefinition("positions", "positions", "tenant", "positions.import", []));
        handler.ExecutionCapabilityPermissions.Returns(["assign"]);
        var identity = Substitute.For<IBackgroundSessionAuthorization>();
        identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(binding.UserId, tenantId, binding.SessionId));
        if (reason is "schema" or "capability" or "newPermission")
            identity.AuthorizeAsync(binding, reason == "schema" ? "positions.import" : "assign", Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason == "session")
            identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        var service = new ImportExportTaskExecutionService(query, Substitute.For<ICommandExecutor>(), files, tenant,
            new StaticImportSchemaRegistry([handler]), new ImportExportExecutionAuthorization(identity),
            Substitute.For<IServiceScopeFactory>(), Substitute.For<IOptionsMonitor<ImportExportOptions>>());
        var result = await service.OpenErrorReceiptAsync(task.Id, binding);
        var allowed = reason is "allowed" or "newPermission";
        Assert.AreEqual(allowed, result.IsSuccess);
        if (allowed) result.Value!.Content.Dispose();
        else Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(allowed ? 1 : 0, files.ReceivedCalls().Count());
    }
}

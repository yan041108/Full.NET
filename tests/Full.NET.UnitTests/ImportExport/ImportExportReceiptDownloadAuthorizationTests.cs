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
    [DataRow("lateSession")]
    [DataRow("latePermission")]
    [DataRow("lateFailure")]
    [DataRow("lateCancel")]
    [DataRow("lateRequestCancel")]
    public async Task Receipt_requires_current_owner_session_and_original_capabilities(string reason)
    {
        using var cancellation = new CancellationTokenSource();
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var owner = Guid.NewGuid();
        var binding = new SessionBindingSnapshot(reason == "actor" ? Guid.NewGuid() : owner, reason == "tenant" ? Guid.NewGuid() : tenantId,
            Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
        var task = new ImportExportTaskRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = owner, SchemaKey = "positions",
            SourceFileName = "positions.xlsx",
            ErrorReceiptFileId = Guid.NewGuid(), ExecutionFailedRowCount = 1,
            ExecutionRowsJson = reason == "snapshot" ? "[]" : ImportExportTaskMapper.SerializeExecutionState(new(
                new Dictionary<string, bool> { ["assign"] = reason != "newPermission" }, [])),
        };
        var query = Substitute.For<IQueryExecutor>();
        query.QuerySingleOrDefaultAsync<ImportExportTaskRecord>(ImportExportTaskSql.FindById,
            Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(task);
        var files = Substitute.For<ITenantResourceFileStore>();
        var stream = new MemoryStream([1]);
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
        files.OpenReadyContentAsync("import_export", task.Id, task.ErrorReceiptFileId.Value, Arg.Any<CancellationToken>())
            .Returns(_ => {
                if (reason == "lateSession") identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
                if (reason == "latePermission") identity.AuthorizeAsync(binding, "positions.import", Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
                if (reason is "lateFailure" or "lateCancel") identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<AuthorizedSessionActor?>(reason == "lateFailure"
                        ? new InvalidOperationException("authorization-failure") : new OperationCanceledException()));
                if (reason == "lateRequestCancel") cancellation.Cancel();
                return Task.FromResult(Result<TenantResourceFileContent>.Success(new(stream, "application/test", "positions-errors-internal-identity.xlsx")));
            });
        var service = new ImportExportTaskExecutionService(query, Substitute.For<ICommandExecutor>(), files, tenant,
            new StaticImportSchemaRegistry([handler]), new ImportExportExecutionAuthorization(identity),
            Substitute.For<IServiceScopeFactory>(), Substitute.For<IOptionsMonitor<ImportExportOptions>>());
        if (reason is "lateFailure" or "lateCancel" or "lateRequestCancel")
        {
            if (reason == "lateFailure") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.OpenErrorReceiptAsync(task.Id, binding, cancellation.Token));
            else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.OpenErrorReceiptAsync(task.Id, binding, cancellation.Token));
            Assert.IsFalse(stream.CanRead);
            Assert.AreEqual(1, files.ReceivedCalls().Count());
            return;
        }
        var result = await service.OpenErrorReceiptAsync(task.Id, binding, cancellation.Token);
        var allowed = reason is "allowed" or "newPermission";
        Assert.AreEqual(allowed, result.IsSuccess);
        if (allowed)
        {
            Assert.AreEqual("positions-errors.xlsx", result.Value!.OriginalFileName);
            result.Value.Content.Dispose();
        }
        else Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        if (reason is "lateSession" or "latePermission") Assert.IsFalse(stream.CanRead);
        Assert.AreEqual(allowed || reason is "lateSession" or "latePermission" ? 1 : 0, files.ReceivedCalls().Count());
    }
}

using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Features.PublishedDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Full.NET.UnitTests.Reporting;

/// <summary>持久导出不能绕过当前主体、会话和文件中原受保护列的权限。</summary>
[TestClass]
public sealed class ReportingExportDownloadAuthorizationTests
{
    [TestMethod]
    [DataRow("lateSession")]
    [DataRow("latePermission")]
    [DataRow("lateFailure")]
    [DataRow("lateCancel")]
    [DataRow("lateRequestCancel")]
    [DataRow("lateVersion")]
    [DataRow("lateDisabled")]
    [DataRow("actor")]
    [DataRow("tenant")]
    [DataRow("session")]
    [DataRow("column")]
    [DataRow("snapshot")]
    [DataRow("missingSnapshot")]
    [DataRow("disabled")]
    [DataRow("missingVersion")]
    [DataRow("ungranted")]
    [DataRow("layout")]
    [DataRow("download")]
    [DataRow("run")]
    [DataRow("returnedActor")]
    [DataRow("allowed")]
    [DataRow("unrelated")]
    [DataRow("newPermission")]
    public async Task Download_requires_current_owner_session_and_original_columns(string reason)
    {
        using var cancellation = new CancellationTokenSource();
        var tenant = new CurrentTenantAccessor(); var tenantId = Guid.NewGuid();
        tenant.SetTenant(new TenantContext(tenantId, "acme", "Acme"));
        var owner = Guid.NewGuid();
        var binding = new SessionBindingSnapshot(reason == "actor" ? Guid.NewGuid() : owner,
            reason == "tenant" ? Guid.NewGuid() : tenantId, Guid.NewGuid(), "stamp", "host", $"tenant:{tenantId:N}");
        var task = new ReportingExportTaskRecord
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = owner,
            DefinitionId = Guid.NewGuid(), VersionNumber = 1, OutputFileId = Guid.NewGuid(),
            StatusKey = ReportingExportTaskStatusKeys.Succeeded,
            ActorPermissionCodesJson = reason == "snapshot" ? "{" : reason == "missingSnapshot" ? null : reason == "newPermission" ? "[]" : "[\"protected.column\",\"unrelated.permission\"]",
        };
        var query = Substitute.For<IQueryExecutor>();
        var database = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        query.QuerySingleOrDefaultAsync<ReportingExportTaskRecord>(
            ReportingExportTaskSql.FindByIdFor(DatabaseProvider.SqlServer), Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(task);
        query.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(
            ReportingTenantGrantSql.ResolveVersion(DatabaseProvider.SqlServer), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new ReportingDefinitionVersionRecord
            {
                DefinitionId = task.DefinitionId, VersionNumber = 1,
                LayoutConfigJson = reason == "layout" ? "{" : "{\"columns\":[{\"key\":\"secret\",\"requiredPermission\":\"protected.column\"}]}",
            });
        query.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(ReportingTenantGrantSql.FindDefinition, Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new ReportingDefinitionRecord { Id = task.DefinitionId, IsEnabled = reason != "disabled" });
        if (reason is "missingVersion" or "ungranted")
            query.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(ReportingTenantGrantSql.ResolveVersion(DatabaseProvider.SqlServer),
                Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ReportingDefinitionVersionRecord?>(null));
        var files = Substitute.For<ITenantResourceFileStore>();
        var stream = new MemoryStream([1]);
        var identity = Substitute.For<IBackgroundSessionAuthorization>();
        identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizedSessionActor(binding.UserId, binding.TenantId, binding.SessionId));
        if (reason == "session")
            identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason is "column" or "newPermission")
            identity.AuthorizeAsync(binding, "protected.column", Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason is "unrelated" or "download" or "run")
            identity.AuthorizeAsync(binding, reason == "unrelated" ? "unrelated.permission"
                    : reason == "run" ? ReportingExecutionPermissions.Run : ReportingExportTaskPermissions.Download,
                Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
        if (reason == "returnedActor")
            identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new AuthorizedSessionActor(Guid.NewGuid(), binding.TenantId, binding.SessionId));
        var definitions = new ReportingPublishedDefinitionResolver(query, new ReportingDefinitionQueryService(query, database), tenant, database);
        files.OpenReadyContentAsync("reporting", task.Id, task.OutputFileId.Value, Arg.Any<CancellationToken>())
            .Returns(_ => {
                if (reason == "lateSession") identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
                if (reason == "latePermission") identity.AuthorizeAsync(binding, "protected.column", Arg.Any<CancellationToken>()).Returns(Task.FromResult<AuthorizedSessionActor?>(null));
                if (reason is "lateFailure" or "lateCancel") identity.AuthorizeAsync(binding, Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromException<AuthorizedSessionActor?>(reason == "lateFailure"
                        ? new InvalidOperationException("authorization-failure") : new OperationCanceledException()));
                if (reason == "lateRequestCancel") cancellation.Cancel();
                if (reason == "lateVersion") query.QuerySingleOrDefaultAsync<ReportingDefinitionVersionRecord>(
                    ReportingTenantGrantSql.ResolveVersion(DatabaseProvider.SqlServer), Arg.Any<object>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<ReportingDefinitionVersionRecord?>(null));
                if (reason == "lateDisabled") query.QuerySingleOrDefaultAsync<ReportingDefinitionRecord>(
                    ReportingTenantGrantSql.FindDefinition, Arg.Any<object>(), Arg.Any<CancellationToken>())
                    .Returns(new ReportingDefinitionRecord { Id = task.DefinitionId, IsEnabled = false });
                return Task.FromResult(Result<TenantResourceFileContent>.Success(new(stream, "application/test", "report.xlsx")));
            });
        var authorization = new ReportingExportAuthorization(tenant, identity, definitions);
        var service = new ReportingExportTaskManagementService(definitions,
            files, query, Substitute.For<ICommandExecutor>(), null!, tenant,
            Substitute.For<IClock>(), Substitute.For<IIdGenerator>(), database, authorization, Substitute.For<IIdentityPermissionEvaluator>());
        if (reason is "lateFailure" or "lateCancel" or "lateRequestCancel")
        {
            if (reason == "lateFailure") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.OpenDownloadAsync(task.Id, binding, cancellation.Token));
            else await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.OpenDownloadAsync(task.Id, binding, cancellation.Token));
            Assert.IsFalse(stream.CanRead);
            Assert.AreEqual(1, files.ReceivedCalls().Count());
            Assert.AreEqual(tenantId, tenant.Id);
            Assert.IsFalse(tenant.IsHost);
            return;
        }
        var result = await service.OpenDownloadAsync(task.Id, binding, cancellation.Token);
        var allowed = reason is "allowed" or "unrelated" or "newPermission";
        Assert.AreEqual(allowed, result.IsSuccess);
        if (allowed) result.Value!.Content.Dispose();
        else Assert.AreEqual(CommonErrorCodes.PermissionDenied, result.Error!.Code);
        Assert.AreEqual(allowed || reason is "lateSession" or "latePermission" or "lateVersion" or "lateDisabled" ? 1 : 0, files.ReceivedCalls().Count());
        if (reason is "lateSession" or "latePermission" or "lateVersion" or "lateDisabled") Assert.IsFalse(stream.CanRead);
        Assert.AreEqual(tenantId, tenant.Id); Assert.IsFalse(tenant.IsHost);
    }
}

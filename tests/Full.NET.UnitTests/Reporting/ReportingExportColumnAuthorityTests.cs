using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Security;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Features.ManageDefinitions;
using Full.NET.Modules.Reporting.Features.ManageExportTasks;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Reporting;

/// <summary>字段检查独立于会话失效；真实权限授权器不得以仍有效的会话替代精确列权限。</summary>
[TestClass]
public sealed class ReportingExportColumnAuthorityTests
{
    [TestMethod]
    public async Task Frozen_visible_column_is_required_even_when_original_session_remains_valid()
    {
        var user = Guid.CreateVersion7(); var tenant = Guid.CreateVersion7(); var definition = Guid.CreateVersion7();
        var current = new CurrentTenantAccessor(); current.SetTenant(new TenantContext(tenant, "acme", "Acme"));
        var binding = new SessionBindingSnapshot(user, tenant, Guid.CreateVersion7(), "stamp", "host", $"tenant:{tenant:N}");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(FullNetIdentityClaimTypes.Permission, ReportingExecutionPermissions.ColumnSchemaName),
            new Claim(FullNetIdentityClaimTypes.Permission, "unrelated.secret.read")], "original"));
        var layout = """{"columns":[{"key":"SchemaName","displayName":"Schema","requiredPermission":"reporting.executions.columns.schema_name"}]}""";
        var version = new ReportingDefinitionVersionResponse(Guid.CreateVersion7(), definition, 1, Guid.CreateVersion7(),
            "reporting.schema_inventory", [], layout, null, user, DateTimeOffset.UtcNow);
        var permissions = ReportingExportTaskAuthority.FreezePermissions(version, code => principal.HasClaim(FullNetIdentityClaimTypes.Permission, code));
        CollectionAssert.DoesNotContain(permissions, "unrelated.secret.read"); Assert.AreEqual(3, permissions.Length);
        var task = new ReportingExportTaskRecord { Id = Guid.CreateVersion7(), TenantId = tenant, RequestedByUserId = user,
            DefinitionId = definition, VersionNumber = 1,
            ActorSessionBindingJson = JsonSerializer.Serialize(binding, ReportingJsonSerializerContext.Default.SessionBindingSnapshot),
            ActorPermissionCodesJson = ReportingExportTaskMapper.SerializePermissionCodes(permissions) };
        var validSession = new ValidSession(); var queries = new RejectQueries();
        var options = Options.Create(new DatabaseOptions { Provider = DatabaseProvider.SqlServer });
        var catalog = new ReportingExecutionCatalog(queries, new ReportingDefinitionQueryService(queries, options), current, options);
        var sessions = new BackgroundSessionAuthorization(validSession, new CurrentPermissions());
        var denied = await new ReportingExportTaskAuthority(sessions, catalog, current).ValidateAsync(task, default);
        Assert.AreEqual(ReportingErrorCodes.ExportAuthorityDenied, denied?.Code); Assert.AreEqual(1, validSession.Calls);
    }

    private sealed class ValidSession : IBackgroundSessionBindingValidator
    {
        internal int Calls;
        public Task<bool> IsValidAsync(SessionBindingSnapshot binding, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(true); }
    }
    private sealed class CurrentPermissions : IPermissionSnapshotReader
    {
        public Task<PermissionSnapshot> ReadAsync(Guid userId, string scopeKey, Guid? tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PermissionSnapshot([ReportingExportTaskPermissions.Create, ReportingDefinitionGrantPermissions.PublishedRead], false));
    }
    private sealed class RejectQueries : IQueryExecutor
    {
        public Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("字段撤权后不得继续读取发布或外部数据源。");
        public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("字段撤权后不得继续读取发布或外部数据源。");
    }
}

using System.Security.Claims;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.ExecuteDefinitions;
using Full.NET.Modules.Reporting.Persistence;
using Full.NET.Modules.Reporting.Serialization;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>每个副作用阶段复核原会话、固定发布版本及创建时可见列的精确权限。</summary>
internal interface IReportingExportTaskAuthority
{
    Task<Error?> ValidateAsync(ReportingExportTaskRecord task, CancellationToken token);
}

/// <summary>权限快照仅限制输出列，不能代替当前会话与授权的权威校验。</summary>
internal sealed class ReportingExportTaskAuthority(IBackgroundSessionAuthorization sessions,
    ReportingExecutionCatalog catalog, ICurrentTenant current) : IReportingExportTaskAuthority
{
    public async Task<Error?> ValidateAsync(ReportingExportTaskRecord task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current.IsAvailable || current.Id != task.TenantId || string.IsNullOrEmpty(task.ActorSessionBindingJson)) return Denied();
        SessionBindingSnapshot? binding;
        string[] permissions;
        try
        {
            binding = JsonSerializer.Deserialize(task.ActorSessionBindingJson, ReportingJsonSerializerContext.Default.SessionBindingSnapshot);
            permissions = ReportingExportTaskMapper.DeserializePermissionCodes(task.ActorPermissionCodesJson).ToArray();
        }
        catch (JsonException) { return Denied(); }
        if (binding is null || binding.TenantId != task.TenantId || binding.UserId != task.RequestedByUserId
            || !permissions.Contains(ReportingExportTaskPermissions.Create, StringComparer.Ordinal)
            || !permissions.Contains(ReportingDefinitionGrantPermissions.PublishedRead, StringComparer.Ordinal)) return Denied();
        var actor = await sessions.AuthorizeAllAsync(binding, permissions, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (current.Id != task.TenantId || actor is null || actor.UserId != task.RequestedByUserId || actor.TenantId != task.TenantId || actor.SessionId != binding.SessionId) return Denied();
        var definition = await catalog.GetByIdAsync(task.DefinitionId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!definition.IsSuccess || !definition.Value!.IsEnabled) return Denied();
        var version = await catalog.GetVersionAsync(task.DefinitionId, task.VersionNumber, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!version.IsSuccess) return Denied();
        try { ReportingLayoutConfigParser.ParseColumns(version.Value!.LayoutConfigJson); }
        catch (InvalidOperationException) { token.ThrowIfCancellationRequested(); return Denied(); }
        catch (JsonException) { token.ThrowIfCancellationRequested(); return Denied(); }
        var source = await catalog.GetDataSourceAsync(task.DefinitionId, task.VersionNumber, version.Value!.DataSourceId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return current.IsAvailable && current.Id == task.TenantId && source is { IsEnabled: true } ? null : Denied();
    }

    internal static string[] FreezePermissions(ReportingDefinitionVersionResponse version, Func<string, bool> hasPermission)
    {
        var result = new HashSet<string>(StringComparer.Ordinal)
        {
            ReportingExportTaskPermissions.Create, ReportingDefinitionGrantPermissions.PublishedRead,
        };
        var columns = ReportingQueryPortCatalog.ResolveResultColumnKeys(version.QueryPortKey);
        foreach (var column in ReportingLayoutConfigParser.ParseColumns(version.LayoutConfigJson))
            if (column.RequiredPermission is { } permission && columns.Contains(column.ColumnKey, StringComparer.Ordinal)
                && hasPermission(permission)) result.Add(permission);
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    internal static Error Denied() => new(ReportingErrorCodes.ExportAuthorityDenied,
        "The original export session, publication grant or required permissions are no longer valid.", ErrorType.Forbidden);
}

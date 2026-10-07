using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Reporting.Contracts;
using Full.NET.Modules.Reporting.Domain;
using Full.NET.Modules.Reporting.Features.PublishedDefinitions;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Features.ManageExportTasks;

/// <summary>原始列快照只限定文件内容；会话、版本授权与精确权限始终从当前权威源重验。</summary>
internal sealed class ReportingExportAuthorization(
    ICurrentTenant tenant, IBackgroundSessionAuthorization authorization, ReportingPublishedDefinitionResolver definitions)
{
    /// <summary>恢复只能使用创建时冻结的交互会话，无委托的旧队列必须重新创建。</summary>
    public Task<bool> CanRunAsync(ReportingExportTaskRecord task, CancellationToken token)
    {
        var snapshot = ReportingExportTaskMapper.DeserializeAuthorization(task.ActorPermissionCodesJson);
        return snapshot?.Binding is null ? Task.FromResult(false)
            : ValidateAsync(task, snapshot.Binding, ReportingExportTaskPermissions.Create, token);
    }

    /// <summary>下载可使用同创建人新的有效会话，但必须保留原始受保护列边界。</summary>
    public Task<bool> CanDownloadAsync(ReportingExportTaskRecord task, SessionBindingSnapshot binding, CancellationToken token) =>
        ValidateAsync(task, binding, ReportingExportTaskPermissions.Download, token);

    private async Task<bool> ValidateAsync(ReportingExportTaskRecord task, SessionBindingSnapshot binding,
        string operationPermission, CancellationToken token)
    {
        if (tenant.Id is null || binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty
            || binding.UserId != task.RequestedByUserId || task.TenantId != tenant.Id || task.TenantId != binding.TenantId
            || string.IsNullOrWhiteSpace(binding.SecurityStamp)) return false;
        var snapshot = ReportingExportTaskMapper.DeserializeAuthorization(task.ActorPermissionCodesJson);
        if (snapshot is null) return false;
        foreach (var permission in new[] { operationPermission, ReportingExecutionPermissions.Run })
            if (!await HasPermissionAsync(binding, permission, token).ConfigureAwait(false)) return false;
        try
        {
            var published = await definitions.ResolveAsync(task.DefinitionId, task.VersionNumber, token).ConfigureAwait(false);
            if (!published.IsSuccess) return false;
            var original = snapshot.PermissionCodes.ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<ReportingLayoutColumnDefinition> columns;
            try { columns = ReportingLayoutConfigParser.ParseColumns(published.Value!.Version.LayoutConfigJson); }
            catch (InvalidOperationException) { return false; }
            foreach (var permission in columns.Select(column => column.RequiredPermission)
                .Where(permission => permission is not null && original.Contains(permission)).Distinct(StringComparer.Ordinal))
                if (!await HasPermissionAsync(binding, permission!, token).ConfigureAwait(false)) return false;
        }
        catch (System.Text.Json.JsonException)
        {
            // 损坏发布快照无法证明原列边界，拒绝文件读取；不能按空列权限猜测。
            return false;
        }
        return true;
    }

    private async Task<bool> HasPermissionAsync(SessionBindingSnapshot binding, string permission, CancellationToken token)
    {
        var actor = await authorization.AuthorizeAsync(binding, permission, token).ConfigureAwait(false);
        return actor is not null && actor.UserId == binding.UserId && actor.TenantId == tenant.Id && actor.SessionId == binding.SessionId;
    }
}

/// <summary>持久任务授权委托；只存服务端生成的会话绑定，不向公共任务 DTO 暴露。</summary>
internal sealed record ReportingExportAuthorizationSnapshot(string[] PermissionCodes, SessionBindingSnapshot? Binding);

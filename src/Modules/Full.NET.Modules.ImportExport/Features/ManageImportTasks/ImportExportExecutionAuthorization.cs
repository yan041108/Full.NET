using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.ImportExport.Features.ManageImportTasks;

/// <summary>导入调度只通过 Identity Port 复核当前会话与精确权限，不信任排队时的 Claims 快照。</summary>
internal sealed class ImportExportExecutionAuthorization(IBackgroundSessionAuthorization authorization)
{
    /// <summary>每批验证主执行权限及原先已授予的 Schema 能力；任一撤销均拒绝整批，保持预览检查点稳定。</summary>
    internal async Task<bool> IsAllowedAsync(
        SessionBindingSnapshot? binding,
        Guid tenantId,
        IStaticImportSchemaHandler handler,
        IReadOnlyDictionary<string, bool>? capabilityFlags,
        CancellationToken cancellationToken)
    {
        if (binding is null || binding.TenantId != tenantId || tenantId == Guid.Empty
            || binding.UserId == Guid.Empty || binding.SessionId == Guid.Empty)
            return false;

        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            ImportExportPermissions.ImportTasksExecute,
            handler.GetDefinition().RequiredPermission,
        };
        foreach (var permission in handler.ExecutionCapabilityPermissions)
            if (capabilityFlags?.TryGetValue(permission, out var allowed) == true && allowed)
                required.Add(permission);

        foreach (var permission in required)
        {
            var actor = await authorization.AuthorizeAsync(binding, permission, cancellationToken).ConfigureAwait(false);
            if (actor is null || actor.UserId != binding.UserId || actor.TenantId != tenantId || actor.SessionId != binding.SessionId)
                return false;
        }
        return true;
    }

    /// <summary>仅保存 Schema 声明的能力；无关权限撤销不影响本任务，后来新增权限也不能改变有效行集合。</summary>
    internal static IReadOnlyDictionary<string, bool> FreezeCapabilities(
        IStaticImportSchemaHandler handler,
        IReadOnlyDictionary<string, bool>? capabilities) =>
        handler.ExecutionCapabilityPermissions.ToDictionary(
            permission => permission,
            permission => capabilities?.TryGetValue(permission, out var allowed) == true && allowed,
            StringComparer.Ordinal);
}

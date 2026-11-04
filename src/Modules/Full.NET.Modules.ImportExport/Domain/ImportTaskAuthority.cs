using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Features.ManageImportTasks;
using Full.NET.Modules.ImportExport.Persistence;
using Full.NET.Modules.ImportExport.Serialization;

namespace Full.NET.Modules.ImportExport.Domain;

/// <summary>后台只使用显式排队绑定；授权快照仅供本次阶段使用，不可缓存为后续写入许可。</summary>
internal sealed record AuthorizedImportTask(SessionBindingSnapshot Binding, ImportColumnMappingRequest? ColumnMapping);

/// <summary>创建、本人排队及后台阶段共同检查来源权限；不存在或无绑定的旧队列失败关闭。</summary>
internal sealed class ImportTaskAuthority(StaticImportSchemaRegistry registry, IBackgroundSessionAuthorization sessions, ICurrentTenant tenant)
{
    internal Task<Error?> ValidateCreationAsync(StaticImportSchemaDefinition definition, StaticImportPreviewContext context,
        CancellationToken token) => definition.ScopeKey != StaticImportSchemaScopeKeys.Tenant ? Task.FromResult<Error?>(Denied())
            : ValidateBindingAsync(context.SessionBinding, context.RequestedByUserId,
                RequiredPermissions(definition, context.CapabilityFlags, ImportExportPermissions.ImportTasksCreate), token);

    internal async Task<Result<AuthorizedImportTask>> ValidateQueueAsync(ImportExportTaskRecord task, StaticImportPreviewContext context, CancellationToken token)
    {
        var definition = registry.TryResolve(task.SchemaKey)?.GetDefinition();
        if (definition is null || definition.ScopeKey != StaticImportSchemaScopeKeys.Tenant
            || !definition.Worksheets.Any(worksheet => worksheet.WorksheetKey == task.WorksheetKey)
            || task.TenantId != tenant.Id || context.RequestedByUserId != task.RequestedByUserId)
            return Result<AuthorizedImportTask>.Failure(Denied());
        var error = await ValidateBindingAsync(context.SessionBinding, task.RequestedByUserId,
            RequiredPermissions(definition, ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson).CapabilityFlags, ImportExportPermissions.ImportTasksExecute), token).ConfigureAwait(false);
        if (error is not null) return Result<AuthorizedImportTask>.Failure(error);
        var mapping = ImportColumnMappingSnapshots.Read(definition, task.WorksheetKey, task.ColumnMappingJson);
        return mapping.IsSuccess ? Result<AuthorizedImportTask>.Success(new(context.SessionBinding!, mapping.Value!.Configuration))
            : Result<AuthorizedImportTask>.Failure(mapping.Error!);
    }

    internal async Task<Result<AuthorizedImportTask>> ValidateStoredAsync(ImportExportTaskRecord task, CancellationToken token)
    {
        if (string.IsNullOrEmpty(task.ActorSessionBindingJson) || Encoding.UTF8.GetByteCount(task.ActorSessionBindingJson) > 4096)
            return Result<AuthorizedImportTask>.Failure(Denied());
        SessionBindingSnapshot? binding;
        try { binding = JsonSerializer.Deserialize(task.ActorSessionBindingJson, ImportExportJsonSerializerContext.Default.SessionBindingSnapshot); }
        catch (JsonException) { return Result<AuthorizedImportTask>.Failure(Denied()); }
        var definition = registry.TryResolve(task.SchemaKey)?.GetDefinition();
        if (definition is null || definition.ScopeKey != StaticImportSchemaScopeKeys.Tenant
            || !definition.Worksheets.Any(worksheet => worksheet.WorksheetKey == task.WorksheetKey)
            || task.TenantId != tenant.Id) return Result<AuthorizedImportTask>.Failure(Denied());
        var error = await ValidateBindingAsync(binding, task.RequestedByUserId,
            RequiredPermissions(definition, ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson).CapabilityFlags, ImportExportPermissions.ImportTasksExecute), token).ConfigureAwait(false);
        if (error is not null) return Result<AuthorizedImportTask>.Failure(error);
        var mapping = ImportColumnMappingSnapshots.Read(definition, task.WorksheetKey, task.ColumnMappingJson);
        return mapping.IsSuccess ? Result<AuthorizedImportTask>.Success(new(binding!, mapping.Value!.Configuration))
            : Result<AuthorizedImportTask>.Failure(mapping.Error!);
    }

    /// <summary>回执按当前创建人会话与原能力上限复核，文件就绪不能成为永久读取许可。</summary>
    internal async Task<Error?> ValidateReadAsync(ImportExportTaskRecord task, SessionBindingSnapshot binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var definition = registry.TryResolve(task.SchemaKey)?.GetDefinition();
        if (definition is null || definition.ScopeKey != StaticImportSchemaScopeKeys.Tenant || task.TenantId != tenant.Id
            || task.RequestedByUserId != binding.UserId) return Denied();
        ImportExportExecutionStateDocument state;
        try { state = ImportExportTaskMapper.DeserializeExecutionState(task.ExecutionRowsJson); }
        catch (JsonException) { return Denied(); }
        if (state.CapabilityFlags is null) return Denied();
        return await ValidateBindingAsync(binding, task.RequestedByUserId,
            RequiredPermissions(definition, state.CapabilityFlags, ImportExportPermissions.ImportTasksRead), token).ConfigureAwait(false);
    }

    // 恢复使用原输入冻结的能力上限，新增授权不能扩大有效行，撤销任一已使用能力则拒绝整批。
    private IReadOnlyCollection<string> RequiredPermissions(StaticImportSchemaDefinition definition,
        IReadOnlyDictionary<string, bool>? capabilities, string operation)
    {
        var required = new HashSet<string>(StringComparer.Ordinal) { operation, definition.RequiredPermission };
        if (registry.TryResolve(definition.SchemaKey) is { } handler)
            foreach (var permission in handler.ExecutionCapabilityPermissions)
                if (capabilities?.TryGetValue(permission, out var allowed) == true && allowed)
                    required.Add(permission);
        return required;
    }

    private async Task<Error?> ValidateBindingAsync(SessionBindingSnapshot? binding, Guid userId, IReadOnlyCollection<string> permissions, CancellationToken token)
    {
        if (binding is null || userId == Guid.Empty || binding.UserId != userId || !tenant.IsAvailable || tenant.IsHost
            || tenant.Id is null || binding.TenantId != tenant.Id) return Denied();
        token.ThrowIfCancellationRequested();
        var tenantId = tenant.Id;
        var actor = await sessions.AuthorizeAllAsync(binding, permissions, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return actor is not null && actor.UserId == userId && actor.SessionId == binding.SessionId && actor.TenantId == tenantId
            && tenant.IsAvailable && tenant.Id == tenantId ? null : Denied();
    }

    internal static Error Denied() => new(ImportTaskAuthorityErrorCodes.Denied,
        "The import session, owner or required source permission is no longer valid.", ErrorType.Forbidden);
}

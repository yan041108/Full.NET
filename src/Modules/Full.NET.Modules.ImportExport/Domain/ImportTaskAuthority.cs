using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
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
                [ImportExportPermissions.ImportTasksCreate, definition.RequiredPermission], token);

    internal async Task<Result<AuthorizedImportTask>> ValidateQueueAsync(ImportExportTaskRecord task, StaticImportPreviewContext context, CancellationToken token)
    {
        var definition = registry.TryResolve(task.SchemaKey)?.GetDefinition();
        if (definition is null || definition.ScopeKey != StaticImportSchemaScopeKeys.Tenant
            || !definition.Worksheets.Any(worksheet => worksheet.WorksheetKey == task.WorksheetKey)
            || task.TenantId != tenant.Id || context.RequestedByUserId != task.RequestedByUserId)
            return Result<AuthorizedImportTask>.Failure(Denied());
        var error = await ValidateBindingAsync(context.SessionBinding, task.RequestedByUserId,
            [ImportExportPermissions.ImportTasksExecute, definition.RequiredPermission], token).ConfigureAwait(false);
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
            [ImportExportPermissions.ImportTasksExecute, definition.RequiredPermission], token).ConfigureAwait(false);
        if (error is not null) return Result<AuthorizedImportTask>.Failure(error);
        var mapping = ImportColumnMappingSnapshots.Read(definition, task.WorksheetKey, task.ColumnMappingJson);
        return mapping.IsSuccess ? Result<AuthorizedImportTask>.Success(new(binding!, mapping.Value!.Configuration))
            : Result<AuthorizedImportTask>.Failure(mapping.Error!);
    }

    private async Task<Error?> ValidateBindingAsync(SessionBindingSnapshot? binding, Guid userId, IReadOnlyCollection<string> permissions, CancellationToken token)
    {
        if (binding is null || userId == Guid.Empty || binding.UserId != userId || !tenant.IsAvailable || tenant.IsHost
            || tenant.Id is null || binding.TenantId != tenant.Id) return Denied();
        var actor = await sessions.AuthorizeAllAsync(binding, permissions, token).ConfigureAwait(false);
        return actor is not null && actor.UserId == userId && actor.TenantId == tenant.Id ? null : Denied();
    }

    internal static Error Denied() => new(ImportTaskAuthorityErrorCodes.Denied,
        "The import session, owner or required source permission is no longer valid.", ErrorType.Forbidden);
}

using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostRoles;

/// <summary>
/// Host 角色创建、更新、权限替换、复制与禁用；系统角色受不变量保护。
/// </summary>
internal sealed class HostRoleManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostRoleQueryService roleQueries,
    AuthorizationCatalog authorizationCatalog,
    FieldProjectionCatalog fieldProjectionCatalog,
    IPermissionSnapshotReader permissionSnapshots,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    private static readonly Regex RoleCodePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<Result<HostRoleResponse>> CreateAsync(
        CreateHostRoleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<HostRoleResponse>> UpdateAsync(
        Guid roleId,
        UpdateHostRoleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(roleId, request, token),
            cancellationToken);

    public Task<Result<HostRoleResponse>> ReplacePermissionsAsync(
        Guid roleId,
        ReplaceHostRolePermissionsRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReplacePermissionsCoreAsync(roleId, request, token),
            cancellationToken);

    public Task<Result<HostRoleResponse>> DisableAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(roleId, token),
            cancellationToken);

    /// <summary>复制源角色的权限、数据范围与字段授权到新角色；不继承系统或超级管理员标记。</summary>
    public Task<Result<HostRoleResponse>> CopyAsync(
        Guid sourceRoleId,
        Guid actorUserId,
        CopyHostRoleRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CopyCoreAsync(sourceRoleId, actorUserId, request, token),
            cancellationToken);

    /// <summary>重新启用已禁用的自定义 Host 角色。</summary>
    public Task<Result<HostRoleResponse>> EnableAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => EnableCoreAsync(roleId, token),
            cancellationToken);

    /// <summary>删除无成员引用的自定义 Host 角色。</summary>
    public Task<Result<bool>> DeleteAsync(
        Guid roleId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DeleteCoreAsync(roleId, token),
            cancellationToken);

    private async Task<Result<HostRoleResponse>> CreateCoreAsync(
        CreateHostRoleRequest request,
        CancellationToken cancellationToken)
    {
        var code = request.Code?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        if (!RoleCodePattern.IsMatch(code) || name.Length is < 1 or > 128)
        {
            return Result<HostRoleResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Role code or name is invalid.",
                ErrorType.Validation));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindRoleByScopeAndCode,
                IdentitySqlParameters.Create(("ScopeKey", HostScope), ("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeConflict();
        }

        var now = clock.UtcNow;
        var roleId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertRole,
                new InsertIdentityRole(
                    roleId,
                    null,
                    HostScope,
                    code,
                    name,
                    false,
                    true,
                    false,
                    RoleDataScopeKinds.All,
                    now,
                    null,
                    1),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Host role insert affected {affectedRows} rows instead of one.");
        }

        return Result<HostRoleResponse>.Success(
            new HostRoleResponse(
                roleId,
                code,
                name,
                false,
                true,
                false,
                [],
                now,
                null,
                1));
    }

    private async Task<Result<HostRoleResponse>> UpdateCoreAsync(
        Guid roleId,
        UpdateHostRoleRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return Result<HostRoleResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Role name is invalid.",
                ErrorType.Validation));
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.IsSystem)
        {
            return SystemLocked();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostRoleName,
                IdentitySqlParameters.Create(
                    ("RoleId", roleId),
                    ("Name", name),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(roleId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await LoadResponseAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostRoleResponse>> ReplacePermissionsCoreAsync(
        Guid roleId,
        ReplaceHostRolePermissionsRequest request,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.IsSystem)
        {
            return SystemLocked();
        }

        var suppliedCodes = request.PermissionCodes ?? [];
        if (suppliedCodes.Any(string.IsNullOrWhiteSpace))
        {
            return InvalidPermissionSet();
        }

        var normalizedCodes = NormalizePermissionCodes(suppliedCodes);
        if (normalizedCodes.Distinct(StringComparer.Ordinal).Count() != normalizedCodes.Length)
        {
            return InvalidPermissionSet();
        }

        var validationError = ValidateAssignablePermissions(normalizedCodes);
        if (validationError is not null)
        {
            return validationError;
        }

        var hierarchyError = ValidatePageActionHierarchy(normalizedCodes);
        if (hierarchyError is not null)
        {
            return hierarchyError;
        }

        var now = clock.UtcNow;
        var versionRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostRoleVersion,
                IdentitySqlParameters.Create(
                    ("RoleId", roleId),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (versionRows != 1)
        {
            return await ResolveUpdateFailureAsync(roleId, cancellationToken)
                .ConfigureAwait(false);
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteRolePermissions,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var permissionCode in normalizedCodes)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.EnsureRolePermission,
                    new IdentityRolePermission(roleId, permissionCode),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await InvalidateRoleMembersAsync(roleId, now, cancellationToken)
            .ConfigureAwait(false);

        return await LoadResponseAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostRoleResponse>> DisableCoreAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        if (record.IsSystem)
        {
            return SystemLocked();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.DisableHostRole,
                IdentitySqlParameters.Create(("RoleId", roleId), ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return SystemLocked();
        }

        await InvalidateRoleMembersAsync(roleId, now, cancellationToken)
            .ConfigureAwait(false);

        return await LoadResponseAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostRoleResponse>> EnableCoreAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.IsSystem || record.IsSuperAdministrator)
        {
            return SystemLocked();
        }

        if (record.IsActive)
        {
            return await LoadResponseAsync(roleId, cancellationToken).ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.EnableHostRole,
                IdentitySqlParameters.Create(("RoleId", roleId), ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return SystemLocked();
        }

        return await LoadResponseAsync(roleId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> DeleteCoreAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RoleNotFound,
                "The host role was not found.",
                ErrorType.NotFound));
        }

        if (record.IsSystem || record.IsSuperAdministrator)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RoleSystemLocked,
                "System roles are protected and cannot be changed.",
                ErrorType.BusinessRule));
        }

        var memberCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                IdentitySql.CountHostRoleMembers,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (memberCount > 0)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RoleHasMembers,
                "The role still has assigned members and cannot be deleted.",
                ErrorType.Conflict));
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteRolePermissions,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteAllHostRoleFieldGrants,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteRoleDataScopeUnits,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteHostRole,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return Result<bool>.Failure(new Error(
                IdentityErrorCodes.RoleSystemLocked,
                "System roles are protected and cannot be changed.",
                ErrorType.BusinessRule));
        }

        return Result<bool>.Success(true);
    }

    private async Task<Result<HostRoleResponse>> CopyCoreAsync(
        Guid sourceRoleId,
        Guid actorUserId,
        CopyHostRoleRequest request,
        CancellationToken cancellationToken)
    {
        var source = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", sourceRoleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (source is null || !source.IsActive)
        {
            return NotFound();
        }

        if (source.IsSuperAdministrator)
        {
            return CopySourceNotAllowed();
        }

        var code = request.Code?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        if (!RoleCodePattern.IsMatch(code) || name.Length is < 1 or > 128)
        {
            return Result<HostRoleResponse>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Role code or name is invalid.",
                ErrorType.Validation));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindRoleByScopeAndCode,
                IdentitySqlParameters.Create(("ScopeKey", HostScope), ("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeConflict();
        }

        var sourcePermissionCodes = await roleQueries.LoadPermissionCodesAsync(
                sourceRoleId,
                cancellationToken)
            .ConfigureAwait(false);
        var actorSnapshot = await permissionSnapshots.ReadAsync(
                actorUserId,
                HostScope,
                tenantId: null,
                cancellationToken)
            .ConfigureAwait(false);
        var copiedPermissionCodes = FilterCopyPermissionCodes(
            sourcePermissionCodes,
            actorSnapshot);
        var hierarchyError = ValidatePageActionHierarchy(copiedPermissionCodes);
        if (hierarchyError is not null)
        {
            return hierarchyError;
        }

        var sourceUnitIds = (await queryExecutor.QueryAsync<Guid>(
                    IdentitySql.GetRoleDataScopeUnitIds,
                    IdentitySqlParameters.Create(("RoleId", sourceRoleId)),
                    cancellationToken)
                .ConfigureAwait(false)).ToArray();
        var sourceFieldGrantRows = await queryExecutor.QueryAsync<IdentityRoleFieldGrantRow>(
                IdentitySql.ListHostRoleFieldGrantRowsByRoleId,
                IdentitySqlParameters.Create(("RoleId", sourceRoleId)),
                cancellationToken)
            .ConfigureAwait(false);
        var copiedFieldGrants = FilterCopyFieldGrants(sourceFieldGrantRows);

        var now = clock.UtcNow;
        var newRoleId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertRole,
                new InsertIdentityRole(
                    newRoleId,
                    null,
                    HostScope,
                    code,
                    name,
                    false,
                    true,
                    false,
                    source.DataScopeKind,
                    now,
                    null,
                    1),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Host role copy insert affected {affectedRows} rows instead of one.");
        }

        foreach (var permissionCode in copiedPermissionCodes)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.EnsureRolePermission,
                    new IdentityRolePermission(newRoleId, permissionCode),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var unitId in sourceUnitIds)
        {
            await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertRoleDataScopeUnit,
                    IdentitySqlParameters.Create(("RoleId", newRoleId), ("UnitId", unitId)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var grant in copiedFieldGrants)
        {
            var grantRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostRoleFieldGrant,
                    IdentitySqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("RoleId", newRoleId),
                        ("ResourceKey", grant.ResourceKey),
                        ("FieldKey", grant.FieldKey),
                        ("CreatedAtUtc", now),
                        ("CreatedById", actorUserId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (grantRows != 1)
            {
                throw new InvalidOperationException(
                    $"Role field grant copy insert affected {grantRows} rows instead of one.");
            }
        }

        return await LoadResponseAsync(newRoleId, cancellationToken).ConfigureAwait(false);
    }

    private string[] FilterCopyPermissionCodes(
        IReadOnlyList<string> sourcePermissionCodes,
        PermissionSnapshot actorSnapshot)
    {
        var assignableCodes = authorizationCatalog.Permissions
            .Where(permission => (permission.Scope & AuthorizationScope.Host) != 0)
            .Select(permission => permission.Code)
            .Where(code => !code.StartsWith(
                "identity.super_administrators.",
                StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var actorCodes = actorSnapshot.IsSuperAdministrator
            ? null
            : actorSnapshot.Permissions.ToHashSet(StringComparer.Ordinal);
        var filtered = sourcePermissionCodes
            .Select(code => code.Trim())
            .Where(code => assignableCodes.Contains(code))
            .Where(code => actorCodes is null || actorCodes.Contains(code))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return RemoveOrphanActionPermissions(filtered);
    }

    private IReadOnlyList<IdentityRoleFieldGrantRow> FilterCopyFieldGrants(
        IReadOnlyList<IdentityRoleFieldGrantRow> sourceFieldGrants)
    {
        var copied = new List<IdentityRoleFieldGrantRow>();
        foreach (var grant in sourceFieldGrants)
        {
            if (!fieldProjectionCatalog.TryGetResource(grant.ResourceKey, out var resource))
            {
                continue;
            }

            var assignable = resource.Fields
                .Where(field => field.Assignable)
                .Select(field => field.FieldKey)
                .ToHashSet(StringComparer.Ordinal);
            if (assignable.Contains(grant.FieldKey))
            {
                copied.Add(grant);
            }
        }

        return copied;
    }

    private string[] RemoveOrphanActionPermissions(IReadOnlyList<string> permissionCodes)
    {
        var granted = permissionCodes.ToHashSet(StringComparer.Ordinal);
        var pagePermissionByNavigationId = authorizationCatalog.Navigation
            .ToDictionary(
                item => item.Id,
                item => item.RequiredPermission,
                StringComparer.Ordinal);
        foreach (var action in authorizationCatalog.Actions)
        {
            if (!granted.Contains(action.PermissionCode))
            {
                continue;
            }

            if (!pagePermissionByNavigationId.TryGetValue(
                    action.NavigationId,
                    out var pagePermission))
            {
                granted.Remove(action.PermissionCode);
                continue;
            }

            if (!granted.Contains(pagePermission))
            {
                granted.Remove(action.PermissionCode);
            }
        }

        return granted
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task InvalidateRoleMembersAsync(
        Guid roleId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await commandExecutor.ExecuteAsync(
                IdentitySql.RotateSecurityStampsByRole,
                IdentitySqlParameters.Create(
                    ("RoleId", roleId),
                    ("SecurityStamp", idGenerator.NewId().ToString("N")),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        await commandExecutor.ExecuteAsync(
                IdentitySql.RevokeSessionsByRole,
                IdentitySqlParameters.Create(("RoleId", roleId), ("RevokedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<HostRoleResponse>> LoadResponseAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var permissionCodes = await roleQueries.LoadPermissionCodesAsync(
                roleId,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<HostRoleResponse>.Success(
            HostRoleQueryService.Map(record, permissionCodes));
    }

    private async Task<Result<HostRoleResponse>> ResolveUpdateFailureAsync(
        Guid roleId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
                IdentitySql.FindHostRoleById,
                IdentitySqlParameters.Create(("RoleId", roleId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        if (record.IsSystem)
        {
            return SystemLocked();
        }

        return VersionConflict();
    }

    private static string[] NormalizePermissionCodes(
        IReadOnlyList<string> permissionCodes) =>
        permissionCodes
            .Select(code => code.Trim())
            .Order(StringComparer.Ordinal)
            .ToArray();

    private Result<HostRoleResponse>? ValidateAssignablePermissions(
        IReadOnlyList<string> permissionCodes)
    {
        var permissionsByCode = authorizationCatalog.Permissions
            .ToDictionary(
                permission => permission.Code,
                permission => permission,
                StringComparer.Ordinal);
        foreach (var code in permissionCodes)
        {
            if (!permissionsByCode.ContainsKey(code))
            {
                return Result<HostRoleResponse>.Failure(new Error(
                    ValidationErrorCodes.Failed,
                    $"Permission '{code}' is not assignable to host roles.",
                    ErrorType.Validation));
            }

            if (code.StartsWith("identity.super_administrators.", StringComparison.Ordinal))
            {
                return Result<HostRoleResponse>.Failure(new Error(
                    ValidationErrorCodes.Failed,
                    "Super administrator permissions cannot be assigned to custom roles.",
                    ErrorType.Validation));
            }
        }

        return null;
    }

    private Result<HostRoleResponse>? ValidatePageActionHierarchy(
        IReadOnlyList<string> permissionCodes)
    {
        var granted = permissionCodes.ToHashSet(StringComparer.Ordinal);
        var pagePermissionByNavigationId = authorizationCatalog.Navigation
            .ToDictionary(
                item => item.Id,
                item => item.RequiredPermission,
                StringComparer.Ordinal);

        foreach (var action in authorizationCatalog.Actions)
        {
            if (!granted.Contains(action.PermissionCode))
            {
                continue;
            }

            if (!pagePermissionByNavigationId.TryGetValue(
                    action.NavigationId,
                    out var pagePermission))
            {
                continue;
            }

            if (!granted.Contains(pagePermission))
            {
                return Result<HostRoleResponse>.Failure(new Error(
                    IdentityErrorCodes.ActionRequiresPage,
                    "Action permissions require the parent page permission.",
                    ErrorType.Validation));
            }
        }

        return null;
    }

    private static Result<HostRoleResponse> CodeConflict() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.RoleCodeExists,
            "A host role with this code already exists.",
            ErrorType.Conflict));

    private static Result<HostRoleResponse> InvalidPermissionSet() =>
        Result<HostRoleResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            "Permission codes must be non-empty and unique.",
            ErrorType.Validation));

    private static Result<HostRoleResponse> NotFound() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.RoleNotFound,
            "The host role was not found.",
            ErrorType.NotFound));

    private static Result<HostRoleResponse> SystemLocked() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.RoleSystemLocked,
            "System roles are protected and cannot be changed.",
            ErrorType.BusinessRule));

    private static Result<HostRoleResponse> CopySourceNotAllowed() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.RoleCopySourceNotAllowed,
            "Super administrator roles cannot be used as a copy source.",
            ErrorType.BusinessRule));

    private static Result<HostRoleResponse> VersionConflict() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host role was updated concurrently.",
            ErrorType.Conflict));
}

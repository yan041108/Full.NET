using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostRoles;

/// <summary>
/// Host 角色创建、更新、权限替换与禁用；系统角色受不变量保护。
/// </summary>
internal sealed class HostRoleManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostRoleQueryService roleQueries,
    AuthorizationCatalog authorizationCatalog,
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

    private static Result<HostRoleResponse> VersionConflict() =>
        Result<HostRoleResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host role was updated concurrently.",
            ErrorType.Conflict));
}

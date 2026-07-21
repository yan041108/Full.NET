using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostMenus;

/// <summary>
/// Host 自定义菜单创建、更新与禁用；系统项由 Contributor 维护且不可通过本服务变更。
/// </summary>
internal sealed class HostMenuManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostMenuQueryService menuQueries,
    AuthorizationCatalog authorizationCatalog,
    IClock clock,
    IIdGenerator idGenerator)
{
    private const string HostScope = "host";

    private static readonly Regex RouteNamePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<Result<HostMenuResponse>> CreateAsync(
        CreateHostMenuRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<HostMenuResponse>> UpdateAsync(
        Guid menuId,
        UpdateHostMenuRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(menuId, request, token),
            cancellationToken);

    public Task<Result<HostMenuResponse>> DisableAsync(
        Guid menuId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(menuId, token),
            cancellationToken);

    private async Task<Result<HostMenuResponse>> CreateCoreAsync(
        CreateHostMenuRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateWriteRequest(
            request.RouteName,
            request.Path,
            request.ComponentKey,
            request.Title,
            request.Caption,
            request.Icon,
            request.RequiredPermission);
        if (validationError is not null)
        {
            return validationError;
        }

        if (AdminNavigationWhitelist.IsReservedRouteName(request.RouteName))
        {
            return RouteNameConflict();
        }

        var parentId = ParseParentId(request.ParentId);
        if (request.ParentId is { Length: > 0 } && parentId is null)
        {
            return ValidationFailure("Parent menu id is invalid.");
        }

        if (parentId is Guid parsedParentId)
        {
            var parentError = await EnsureParentExistsAsync(parsedParentId, cancellationToken)
                .ConfigureAwait(false);
            if (parentError is not null)
            {
                return parentError;
            }
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuByScopeAndRouteName,
                new { ScopeKey = HostScope, RouteName = request.RouteName.Trim() },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return RouteNameConflict();
        }

        var now = clock.UtcNow;
        var menuId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.InsertHostMenu,
                new InsertIdentityNavigation(
                    menuId,
                    null,
                    HostScope,
                    parentId,
                    request.RouteName.Trim(),
                    request.Path.Trim(),
                    request.ComponentKey.Trim(),
                    request.Title.Trim(),
                    request.Caption.Trim(),
                    request.Icon.Trim(),
                    request.DisplayOrder,
                    request.RequiredPermission.Trim(),
                    false,
                    true,
                    now,
                    null,
                    1),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Host menu insert affected {affectedRows} rows instead of one.");
        }

        return await menuQueries.GetByIdAsync(menuId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostMenuResponse>> UpdateCoreAsync(
        Guid menuId,
        UpdateHostMenuRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateWriteRequest(
            routeName: null,
            request.Path,
            request.ComponentKey,
            request.Title,
            request.Caption,
            request.Icon,
            request.RequiredPermission);
        if (validationError is not null)
        {
            return validationError;
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                new { MenuId = menuId },
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

        var parentId = ParseParentId(request.ParentId);
        if (request.ParentId is { Length: > 0 } && parentId is null)
        {
            return ValidationFailure("Parent menu id is invalid.");
        }

        if (parentId is Guid parsedParentId)
        {
            if (parsedParentId == menuId)
            {
                return ValidationFailure("A menu cannot be its own parent.");
            }

            var parentError = await EnsureParentExistsAsync(parsedParentId, cancellationToken)
                .ConfigureAwait(false);
            if (parentError is not null)
            {
                return parentError;
            }
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostMenu,
                new
                {
                    MenuId = menuId,
                    ParentId = parentId,
                    Path = request.Path.Trim(),
                    ComponentKey = request.ComponentKey.Trim(),
                    Title = request.Title.Trim(),
                    Caption = request.Caption.Trim(),
                    Icon = request.Icon.Trim(),
                    DisplayOrder = request.DisplayOrder,
                    RequiredPermission = request.RequiredPermission.Trim(),
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(menuId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await menuQueries.GetByIdAsync(menuId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostMenuResponse>> DisableCoreAsync(
        Guid menuId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                new { MenuId = menuId },
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
                IdentitySql.DisableHostMenu,
                new { MenuId = menuId, UpdatedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return SystemLocked();
        }

        return await menuQueries.GetByIdAsync(menuId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostMenuResponse>?> EnsureParentExistsAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var parent = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                new { MenuId = parentId },
                cancellationToken)
            .ConfigureAwait(false);
        if (parent is null || !parent.IsActive)
        {
            return ValidationFailure("Parent menu was not found or is inactive.");
        }

        return null;
    }

    private async Task<Result<HostMenuResponse>> ResolveUpdateFailureAsync(
        Guid menuId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                new { MenuId = menuId },
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

    private Result<HostMenuResponse>? ValidateWriteRequest(
        string? routeName,
        string path,
        string componentKey,
        string title,
        string caption,
        string icon,
        string requiredPermission)
    {
        if (routeName is not null && !RouteNamePattern.IsMatch(routeName.Trim()))
        {
            return ValidationFailure("Route name is invalid.");
        }

        var normalizedComponentKey = componentKey?.Trim() ?? string.Empty;
        if (!AdminNavigationWhitelist.TryGetEntry(normalizedComponentKey, out var whitelistEntry))
        {
            return ValidationFailure("Component key is not supported.");
        }

        var normalizedPath = path?.Trim() ?? string.Empty;
        if (!string.Equals(normalizedPath, whitelistEntry.Path, StringComparison.Ordinal))
        {
            return ValidationFailure("Path does not match the component whitelist.");
        }

        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedCaption = caption?.Trim() ?? string.Empty;
        var normalizedIcon = icon?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 128
            || normalizedCaption.Length is < 1 or > 128
            || normalizedIcon.Length is < 1 or > 64)
        {
            return ValidationFailure("Title, caption or icon is invalid.");
        }

        var permission = requiredPermission?.Trim() ?? string.Empty;
        if (!IsAssignablePermission(permission))
        {
            return ValidationFailure(
                $"Permission '{permission}' is not assignable to host menus.");
        }

        return null;
    }

    private bool IsAssignablePermission(string permissionCode)
    {
        if (permissionCode.Length == 0)
        {
            return false;
        }

        return authorizationCatalog.Permissions.Any(permission =>
            permission.Scope.HasFlag(AuthorizationScope.Host)
            && string.Equals(permission.Code, permissionCode, StringComparison.Ordinal)
            && !permission.Code.StartsWith(
                "identity.super_administrators.",
                StringComparison.Ordinal));
    }

    private static Guid? ParseParentId(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return null;
        }

        return Guid.TryParse(parentId, out var parsed) ? parsed : null;
    }

    private static Result<HostMenuResponse> RouteNameConflict() =>
        Result<HostMenuResponse>.Failure(new Error(
            IdentityErrorCodes.MenuRouteNameExists,
            "A host menu with this route name already exists.",
            ErrorType.Conflict));

    private static Result<HostMenuResponse> NotFound() =>
        Result<HostMenuResponse>.Failure(new Error(
            IdentityErrorCodes.MenuNotFound,
            "The host menu was not found.",
            ErrorType.NotFound));

    private static Result<HostMenuResponse> SystemLocked() =>
        Result<HostMenuResponse>.Failure(new Error(
            IdentityErrorCodes.MenuSystemLocked,
            "System menus are protected and cannot be changed.",
            ErrorType.BusinessRule));

    private static Result<HostMenuResponse> VersionConflict() =>
        Result<HostMenuResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The host menu was updated concurrently.",
            ErrorType.Conflict));

    private static Result<HostMenuResponse> ValidationFailure(string message) =>
        Result<HostMenuResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));
}

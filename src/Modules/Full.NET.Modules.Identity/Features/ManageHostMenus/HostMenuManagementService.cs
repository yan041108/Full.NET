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
/// Host 自定义菜单创建、更新、启用与禁用；系统项展示字段可通过本服务调整，路由与权限保持锁定。
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
    private const string LayoutComponentKey = "layout";

    private static readonly Regex RouteNamePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DirectoryPathPattern = new(
        "^/[a-z0-9][a-z0-9-/]*$",
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

    public Task<Result<HostMenuResponse>> EnableAsync(
        Guid menuId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => EnableCoreAsync(menuId, token),
            cancellationToken);

    private async Task<Result<HostMenuResponse>> CreateCoreAsync(
        CreateHostMenuRequest request,
        CancellationToken cancellationToken)
    {
        var menuType = NormalizeWritableMenuType(request.MenuType);
        if (menuType is null)
        {
            return ValidationFailure("Menu type is invalid.");
        }

        var validationError = ValidateWriteRequest(
            menuType,
            request.RouteName,
            request.Path,
            request.ComponentKey,
            request.Title,
            request.Caption,
            request.Icon,
            request.RequiredPermission,
            request.Redirect,
            request.LinkUrl,
            request.Remark);
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
                    ResolveComponentKey(menuType, request.ComponentKey),
                    request.Title.Trim(),
                    request.Caption.Trim(),
                    request.Icon.Trim(),
                    request.DisplayOrder,
                    request.RequiredPermission.Trim(),
                    false,
                    true,
                    now,
                    null,
                    1,
                    menuType,
                    NormalizeOptionalText(request.Redirect),
                    NormalizeOptionalText(request.LinkUrl),
                    request.IsHidden,
                    request.IsKeepAlive,
                    request.IsAffix,
                    request.IsEmbedded,
                    NormalizeOptionalText(request.Remark)),
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
        var menuType = NormalizeWritableMenuType(request.MenuType);
        if (menuType is null)
        {
            return ValidationFailure("Menu type is invalid.");
        }

        var validationError = ValidateWriteRequest(
            menuType,
            routeName: null,
            request.Path,
            request.ComponentKey,
            request.Title,
            request.Caption,
            request.Icon,
            request.RequiredPermission,
            request.Redirect,
            request.LinkUrl,
            request.Remark);
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
            return await UpdateSystemMenuCoreAsync(record, request, cancellationToken)
                .ConfigureAwait(false);
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

            var cycleError = await EnsureNoParentCycleAsync(
                    menuId,
                    parsedParentId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cycleError is not null)
            {
                return cycleError;
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
                    ComponentKey = ResolveComponentKey(menuType, request.ComponentKey),
                    Title = request.Title.Trim(),
                    Caption = request.Caption.Trim(),
                    Icon = request.Icon.Trim(),
                    DisplayOrder = request.DisplayOrder,
                    RequiredPermission = request.RequiredPermission.Trim(),
                    MenuType = menuType,
                    Redirect = NormalizeOptionalText(request.Redirect),
                    LinkUrl = NormalizeOptionalText(request.LinkUrl),
                    request.IsHidden,
                    request.IsKeepAlive,
                    request.IsAffix,
                    request.IsEmbedded,
                    Remark = NormalizeOptionalText(request.Remark),
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

    private async Task<Result<HostMenuResponse>> EnableCoreAsync(
        Guid menuId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                IdentitySql.FindHostMenuById,
                new { MenuId = menuId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || record.IsActive)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.EnableHostMenu,
                new { MenuId = menuId, UpdatedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return SystemLocked();
        }

        return await menuQueries.GetByIdAsync(menuId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostMenuResponse>> UpdateSystemMenuCoreAsync(
        IdentityNavigationRecord record,
        UpdateHostMenuRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateSystemPresentation(
            request.Title,
            request.Caption,
            request.Icon,
            request.DisplayOrder);
        if (validationError is not null)
        {
            return validationError;
        }

        var parentId = ParseParentId(request.ParentId);
        if (request.ParentId is { Length: > 0 } && parentId is null)
        {
            return ValidationFailure("Parent menu id is invalid.");
        }

        if (parentId is Guid parsedParentId)
        {
            if (parsedParentId == record.Id)
            {
                return ValidationFailure("A menu cannot be its own parent.");
            }

            var parentError = await EnsureParentExistsAsync(parsedParentId, cancellationToken)
                .ConfigureAwait(false);
            if (parentError is not null)
            {
                return parentError;
            }

            var cycleError = await EnsureNoParentCycleAsync(
                    record.Id,
                    parsedParentId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cycleError is not null)
            {
                return cycleError;
            }
        }

        var now = clock.UtcNow;
        var menuType = NormalizeWritableMenuType(request.MenuType) ?? record.MenuType;
        if (string.Equals(menuType, IdentityHostMenuTypes.Button, StringComparison.Ordinal)
            && !string.Equals(record.MenuType, IdentityHostMenuTypes.Button, StringComparison.Ordinal))
        {
            menuType = IdentityHostMenuTypes.Menu;
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                IdentitySql.UpdateHostSystemMenu,
                new
                {
                    MenuId = record.Id,
                    ParentId = parentId,
                    Title = request.Title.Trim(),
                    Caption = request.Caption.Trim(),
                    Icon = request.Icon.Trim(),
                    DisplayOrder = request.DisplayOrder,
                    MenuType = menuType,
                    Redirect = NormalizeOptionalText(request.Redirect),
                    LinkUrl = NormalizeOptionalText(request.LinkUrl),
                    request.IsHidden,
                    request.IsKeepAlive,
                    request.IsAffix,
                    request.IsEmbedded,
                    Remark = NormalizeOptionalText(request.Remark),
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(record.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        return await menuQueries.GetByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<HostMenuResponse>?> EnsureNoParentCycleAsync(
        Guid menuId,
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { menuId };
        var currentParentId = (Guid?)parentId;
        while (currentParentId is Guid candidateParentId)
        {
            if (!visited.Add(candidateParentId))
            {
                return ValidationFailure("Parent menu would create a cycle.");
            }

            var parent = await queryExecutor.QuerySingleOrDefaultAsync<IdentityNavigationRecord>(
                    IdentitySql.FindHostMenuById,
                    new { MenuId = candidateParentId },
                    cancellationToken)
                .ConfigureAwait(false);
            if (parent is null || !parent.IsActive)
            {
                return ValidationFailure("Parent menu was not found or is inactive.");
            }

            currentParentId = parent.ParentId;
        }

        return null;
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

        return VersionConflict();
    }

    private Result<HostMenuResponse>? ValidateSystemPresentation(
        string title,
        string caption,
        string icon,
        int displayOrder)
    {
        _ = displayOrder;
        var normalizedTitle = title?.Trim() ?? string.Empty;
        var normalizedCaption = caption?.Trim() ?? string.Empty;
        var normalizedIcon = icon?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 128
            || normalizedCaption.Length is < 1 or > 128
            || normalizedIcon.Length is < 1 or > 64)
        {
            return ValidationFailure("Title, caption or icon is invalid.");
        }

        return null;
    }

    private Result<HostMenuResponse>? ValidateWriteRequest(
        string menuType,
        string? routeName,
        string path,
        string componentKey,
        string title,
        string caption,
        string icon,
        string requiredPermission,
        string? redirect,
        string? linkUrl,
        string? remark)
    {
        if (routeName is not null && !RouteNamePattern.IsMatch(routeName.Trim()))
        {
            return ValidationFailure("Route name is invalid.");
        }

        var normalizedPath = path?.Trim() ?? string.Empty;
        var normalizedComponentKey = componentKey?.Trim() ?? string.Empty;
        if (string.Equals(menuType, IdentityHostMenuTypes.Directory, StringComparison.Ordinal))
        {
            if (!string.Equals(normalizedComponentKey, LayoutComponentKey, StringComparison.Ordinal))
            {
                return ValidationFailure("Directory menus must use the layout component key.");
            }

            if (!DirectoryPathPattern.IsMatch(normalizedPath) || normalizedPath.Length > 256)
            {
                return ValidationFailure("Directory path is invalid.");
            }
        }
        else if (!AdminNavigationWhitelist.TryGetEntry(normalizedComponentKey, out var whitelistEntry)
            || !string.Equals(normalizedPath, whitelistEntry.Path, StringComparison.Ordinal))
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

        if (NormalizeOptionalText(redirect) is { Length: > 256 })
        {
            return ValidationFailure("Redirect is invalid.");
        }

        if (NormalizeOptionalText(linkUrl) is { Length: > 512 })
        {
            return ValidationFailure("Link URL is invalid.");
        }

        if (NormalizeOptionalText(remark) is { Length: > 500 })
        {
            return ValidationFailure("Remark is invalid.");
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

    private static string ResolveComponentKey(string menuType, string componentKey) =>
        string.Equals(menuType, IdentityHostMenuTypes.Directory, StringComparison.Ordinal)
            ? LayoutComponentKey
            : componentKey.Trim();

    private static string? NormalizeWritableMenuType(string? menuType)
    {
        if (string.Equals(menuType, IdentityHostMenuTypes.Button, StringComparison.Ordinal))
        {
            return null;
        }

        return string.Equals(menuType, IdentityHostMenuTypes.Directory, StringComparison.Ordinal)
            ? IdentityHostMenuTypes.Directory
            : IdentityHostMenuTypes.Menu;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
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

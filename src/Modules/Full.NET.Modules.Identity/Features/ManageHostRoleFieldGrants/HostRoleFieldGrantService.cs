using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.FieldProjection;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.ManageHostRoleFieldGrants;

/// <summary>管理 Host 角色的显式字段授权，并以角色版本提供并发与撤销边界。</summary>
internal sealed class HostRoleFieldGrantService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    FieldProjectionCatalog catalog,
    IClock clock,
    IIdGenerator idGenerator)
{
    public IReadOnlyCollection<FieldProjectionResourceDefinition> GetCatalog() =>
        catalog.Resources;

    public async Task<Result<HostRoleFieldGrantsResponse>> GetAsync(
        Guid roleId,
        string resourceKey,
        CancellationToken cancellationToken = default)
    {
        if (!catalog.TryGetResource(resourceKey, out var resource))
        {
            return InvalidProjection();
        }

        var role = await FindHostRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return NotFound();
        }

        var fieldKeys = await queryExecutor.QueryAsync<string>(
                IdentitySql.GetHostRoleFieldGrants,
                IdentitySqlParameters.Create(("RoleId", roleId), ("ResourceKey", resourceKey)),
                cancellationToken)
            .ConfigureAwait(false);
        var assignable = resource.Fields
            .Where(field => field.Assignable)
            .Select(field => field.FieldKey)
            .ToHashSet(StringComparer.Ordinal);
        if (fieldKeys.Any(fieldKey => !assignable.Contains(fieldKey)))
        {
            return InvalidProjection();
        }

        return Result<HostRoleFieldGrantsResponse>.Success(
            new HostRoleFieldGrantsResponse(
                roleId,
                resourceKey,
                fieldKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                role.Version));
    }

    public Task<Result<HostRoleFieldGrantsResponse>> ReplaceAsync(
        Guid roleId,
        Guid actorUserId,
        ReplaceHostRoleFieldGrantsRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ReplaceCoreAsync(roleId, actorUserId, request, token),
            cancellationToken);

    private async Task<Result<HostRoleFieldGrantsResponse>> ReplaceCoreAsync(
        Guid roleId,
        Guid actorUserId,
        ReplaceHostRoleFieldGrantsRequest request,
        CancellationToken cancellationToken)
    {
        var role = await FindHostRoleAsync(roleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystem || role.IsSuperAdministrator)
        {
            return SystemLocked();
        }

        var validation = Validate(request);
        if (!validation.IsSuccess)
        {
            return Result<HostRoleFieldGrantsResponse>.Failure(validation.Error!);
        }

        var fieldKeys = validation.Value!;
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
            return VersionConflict();
        }

        await commandExecutor.ExecuteAsync(
                IdentitySql.DeleteHostRoleFieldGrants,
                IdentitySqlParameters.Create(("RoleId", roleId), ("ResourceKey", request.ResourceKey)),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var fieldKey in fieldKeys)
        {
            var affectedRows = await commandExecutor.ExecuteAsync(
                    IdentitySql.InsertHostRoleFieldGrant,
                    IdentitySqlParameters.Create(
                        ("Id", idGenerator.NewId()),
                        ("RoleId", roleId),
                        ("ResourceKey", request.ResourceKey),
                        ("FieldKey", fieldKey),
                        ("CreatedAtUtc", now),
                        ("CreatedById", actorUserId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (affectedRows != 1)
            {
                throw new InvalidOperationException(
                    $"Role field grant insert affected {affectedRows} rows instead of one.");
            }
        }

        return Result<HostRoleFieldGrantsResponse>.Success(
            new HostRoleFieldGrantsResponse(
                roleId,
                request.ResourceKey,
                fieldKeys,
                request.Version + 1));
    }

    private Result<IReadOnlyList<string>> Validate(
        ReplaceHostRoleFieldGrantsRequest request)
    {
        if (request.FieldKeys is null
            || !catalog.TryGetResource(request.ResourceKey ?? string.Empty, out var resource))
        {
            return Result<IReadOnlyList<string>>.Failure(InvalidProjectionError());
        }

        var requested = request.FieldKeys.ToArray();
        var normalized = requested.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var assignable = resource.Fields
            .Where(field => field.Assignable)
            .Select(field => field.FieldKey)
            .ToHashSet(StringComparer.Ordinal);
        if (requested.Any(string.IsNullOrWhiteSpace)
            || normalized.Length != requested.Length
            || normalized.Any(fieldKey => !assignable.Contains(fieldKey)))
        {
            return Result<IReadOnlyList<string>>.Failure(InvalidProjectionError());
        }

        return Result<IReadOnlyList<string>>.Success(normalized);
    }

    private Task<IdentityRoleRecord?> FindHostRoleAsync(
        Guid roleId,
        CancellationToken cancellationToken) =>
        queryExecutor.QuerySingleOrDefaultAsync<IdentityRoleRecord>(
            IdentitySql.FindHostRoleById,
            IdentitySqlParameters.Create(("RoleId", roleId)),
            cancellationToken);

    private static Error InvalidProjectionError() => new(
        IdentityErrorCodes.FieldProjectionInvalid,
        "The field projection resource or field keys are invalid.",
        ErrorType.Validation);

    private static Result<HostRoleFieldGrantsResponse> InvalidProjection() =>
        Result<HostRoleFieldGrantsResponse>.Failure(InvalidProjectionError());

    private static Result<HostRoleFieldGrantsResponse> NotFound() =>
        Result<HostRoleFieldGrantsResponse>.Failure(new Error(
            IdentityErrorCodes.RoleNotFound,
            "The host role was not found.",
            ErrorType.NotFound));

    private static Result<HostRoleFieldGrantsResponse> SystemLocked() =>
        Result<HostRoleFieldGrantsResponse>.Failure(new Error(
            IdentityErrorCodes.RoleSystemLocked,
            "System and super-administrator roles cannot change field grants.",
            ErrorType.Conflict));

    private static Result<HostRoleFieldGrantsResponse> VersionConflict() =>
        Result<HostRoleFieldGrantsResponse>.Failure(new Error(
            IdentityErrorCodes.FieldProjectionVersionConflict,
            "The host role changed concurrently.",
            ErrorType.Conflict));
}

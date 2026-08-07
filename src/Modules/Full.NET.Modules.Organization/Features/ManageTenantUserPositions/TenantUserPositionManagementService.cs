using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserPositions;

/// <summary>租户用户-职位隶属写入服务。</summary>
internal sealed class TenantUserPositionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantUserPositionQueryService assignmentQueries,
    TenantPositionQueryService positionQueries,
    IHostUserDirectory hostUserDirectory,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<OrganizationUserPositionResponse>> CreateAsync(
        CreateOrganizationUserPositionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<OrganizationUserPositionResponse>> UpdateAsync(
        Guid assignmentId,
        UpdateOrganizationUserPositionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(assignmentId, request, token),
            cancellationToken);

    public Task<Result<OrganizationUserPositionResponse>> DisableAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DisableCoreAsync(assignmentId, token),
            cancellationToken);

    private async Task<Result<OrganizationUserPositionResponse>> CreateCoreAsync(
        CreateOrganizationUserPositionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var hostUser = await hostUserDirectory.FindActiveHostUserAsync(
                request.UserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (hostUser is null)
        {
            return UserNotFound();
        }

        var positionResult = await positionQueries.FindByIdAsync(
                request.PositionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (!positionResult.IsSuccess)
        {
            return Result<OrganizationUserPositionResponse>.Failure(positionResult.Error!);
        }

        if (!positionResult.Value!.IsActive)
        {
            return Result<OrganizationUserPositionResponse>.Failure(new Error(
                OrganizationErrorCodes.PositionNotFound,
                "The organization position was not found.",
                ErrorType.NotFound));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUserPositionRecord>(
                OrganizationSql.FindUserPositionByTenantUserAndPosition,
                new { UserId = request.UserId, PositionId = request.PositionId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return AlreadyAssigned();
        }

        var now = clock.UtcNow;
        var assignmentId = idGenerator.NewId();
        if (request.IsPrimary)
        {
            await commandExecutor.ExecuteAsync(
                    OrganizationSql.ClearPrimaryUserPositions,
                    new
                    {
                        UserId = request.UserId,
                        AssignmentId = assignmentId,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.InsertUserPosition,
                new InsertOrganizationUserPosition(
                    assignmentId,
                    request.UserId,
                    request.PositionId,
                    request.IsPrimary,
                    true,
                    now,
                    null,
                    1),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Organization user position insert affected {affectedRows} rows instead of one.");
        }

        return await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserPositionResponse>> UpdateCoreAsync(
        Guid assignmentId,
        UpdateOrganizationUserPositionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var current = await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken)
            .ConfigureAwait(false);
        if (!current.IsSuccess)
        {
            return current;
        }

        var now = clock.UtcNow;
        if (request.IsPrimary)
        {
            await commandExecutor.ExecuteAsync(
                    OrganizationSql.ClearPrimaryUserPositions,
                    new
                    {
                        UserId = current.Value!.UserId,
                        AssignmentId = assignmentId,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.UpdateUserPositionPrimary,
                new
                {
                    AssignmentId = assignmentId,
                    request.IsPrimary,
                    request.Version,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return Conflict();
        }

        return await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserPositionResponse>> DisableCoreAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.DisableUserPosition,
                new { AssignmentId = assignmentId, UpdatedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return NotFound();
        }

        return await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("organization.tenant_context_required");
        }
    }

    private static Result<OrganizationUserPositionResponse> AlreadyAssigned() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UserPositionAlreadyAssigned,
            "The user is already assigned to this position.",
            ErrorType.Conflict));

    private static Result<OrganizationUserPositionResponse> UserNotFound() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UserPositionUserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationUserPositionResponse> NotFound() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UserPositionNotFound,
            "The organization user position assignment was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationUserPositionResponse> Conflict() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The organization user position assignment changed concurrently.",
            ErrorType.Conflict));
}

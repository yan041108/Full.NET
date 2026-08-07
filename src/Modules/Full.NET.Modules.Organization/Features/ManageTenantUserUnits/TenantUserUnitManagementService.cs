using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantUnits;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

/// <summary>租户用户-机构隶属写入服务。</summary>
internal sealed class TenantUserUnitManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantUserUnitQueryService assignmentQueries,
    TenantUnitQueryService unitQueries,
    IHostUserDirectory hostUserDirectory,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<OrganizationUserUnitResponse>> CreateAsync(
        CreateOrganizationUserUnitRequest request,
        CancellationToken cancellationToken = default)
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

        return await transaction.ExecuteResultAsync(
                token => CreateCoreAsync(request, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Result<OrganizationUserUnitResponse>> UpdateAsync(
        Guid assignmentId,
        UpdateOrganizationUserUnitRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(assignmentId, request, token),
            cancellationToken);

    public Task<Result<OrganizationUserUnitResponse>> DisableAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DisableCoreAsync(assignmentId, token),
            cancellationToken);

    private async Task<Result<OrganizationUserUnitResponse>> CreateCoreAsync(
        CreateOrganizationUserUnitRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var unitResult = await unitQueries.FindByIdAsync(request.UnitId, cancellationToken)
            .ConfigureAwait(false);
        if (!unitResult.IsSuccess)
        {
            return Result<OrganizationUserUnitResponse>.Failure(unitResult.Error!);
        }

        if (!unitResult.Value!.IsActive)
        {
            return Result<OrganizationUserUnitResponse>.Failure(new Error(
                OrganizationErrorCodes.UnitNotFound,
                "The organization unit was not found.",
                ErrorType.NotFound));
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUserUnitRecord>(
                OrganizationSql.FindUserUnitByTenantUserAndUnit,
                new { UserId = request.UserId, UnitId = request.UnitId },
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.IsActive)
            {
                return AlreadyAssigned();
            }

            return await ReactivateAsync(existing, request.IsPrimary, cancellationToken)
                .ConfigureAwait(false);
        }

        var now = clock.UtcNow;
        var assignmentId = idGenerator.NewId();
        if (request.IsPrimary)
        {
            await commandExecutor.ExecuteAsync(
                    OrganizationSql.ClearPrimaryUserUnits,
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
                OrganizationSql.InsertUserUnit,
                new InsertOrganizationUserUnit(
                    assignmentId,
                    request.UserId,
                    request.UnitId,
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
                $"Organization user unit insert affected {affectedRows} rows instead of one.");
        }

        return await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserUnitResponse>> ReactivateAsync(
        OrganizationUserUnitRecord existing,
        bool isPrimary,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.ReactivateUserUnit,
                new
                {
                    AssignmentId = existing.Id,
                    IsPrimary = isPrimary,
                    UpdatedAtUtc = now,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "Organization user-unit reactivation affected rows outside the expected boundary.");
        }

        if (affectedRows == 0)
        {
            return AlreadyAssigned();
        }

        // 只有重启目标隶属成功后才能清理旧主部门；否则失败结果也会提交前置写入。
        if (isPrimary)
        {
            await commandExecutor.ExecuteAsync(
                    OrganizationSql.ClearPrimaryUserUnits,
                    new
                    {
                        UserId = existing.UserId,
                        AssignmentId = existing.Id,
                        UpdatedAtUtc = now,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await assignmentQueries.GetByIdAsync(existing.Id, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserUnitResponse>> UpdateCoreAsync(
        Guid assignmentId,
        UpdateOrganizationUserUnitRequest request,
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
                    OrganizationSql.ClearPrimaryUserUnits,
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
                OrganizationSql.UpdateUserUnitPrimary,
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

    private async Task<Result<OrganizationUserUnitResponse>> DisableCoreAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.DisableUserUnit,
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

    private static Result<OrganizationUserUnitResponse> AlreadyAssigned() =>
        Result<OrganizationUserUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UserUnitAlreadyAssigned,
            "The user is already assigned to this organization unit.",
            ErrorType.Conflict));

    private static Result<OrganizationUserUnitResponse> UserNotFound() =>
        Result<OrganizationUserUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UserUnitUserNotFound,
            "The host user was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationUserUnitResponse> NotFound() =>
        Result<OrganizationUserUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UserUnitNotFound,
            "The organization user assignment was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationUserUnitResponse> Conflict() =>
        Result<OrganizationUserUnitResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The organization user assignment changed concurrently.",
            ErrorType.Conflict));
}

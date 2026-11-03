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
    ITenantMemberSelectionDirectory tenantMemberDirectory,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    public async Task<Result<OrganizationUserPositionResponse>> CreateAsync(
        CreateOrganizationUserPositionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var tenantMember = await tenantMemberDirectory.FindActiveTenantMemberAsync(
                request.UserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (tenantMember is null)
        {
            return TenantMemberRequired();
        }

        // 成员校验已经返回展示信息，复用事务前快照，避免事务内或提交后再次读取 Identity。
        var user = new HostUserDirectoryEntry(tenantMember.Id, tenantMember.Username, tenantMember.DisplayName);
        return await transaction.ExecuteResultAsync(
                token => CreateCoreAsync(request, user, token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<OrganizationUserPositionResponse>> UpdateAsync(
        Guid assignmentId,
        UpdateOrganizationUserPositionRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        // 提前解析用户展示信息；目录失败时尚未开始写入，提交后也无需再调用 Identity。
        var current = await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        var user = new HostUserDirectoryEntry(current.Value!.UserId, current.Value.Username, current.Value.DisplayName);
        return await transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(assignmentId, request, user, token), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OrganizationUserPositionResponse>> DisableAsync(
        Guid assignmentId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenantContext();
        var current = await assignmentQueries.GetByIdAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess) return current;
        var user = new HostUserDirectoryEntry(current.Value!.UserId, current.Value.Username, current.Value.DisplayName);
        return await transaction.ExecuteResultAsync(
            token => DisableCoreAsync(assignmentId, user, token), cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserPositionResponse>> CreateCoreAsync(
        CreateOrganizationUserPositionRequest request,
        HostUserDirectoryEntry user,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
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
                OrganizationSqlParameters.Create(
                    ("UserId", request.UserId),
                    ("PositionId", request.PositionId)),
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
                    OrganizationSqlParameters.Create(
                        ("UserId", request.UserId),
                        ("AssignmentId", assignmentId),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.InsertUserPosition,
                OrganizationSqlParameters.Create(
                    ("Id", assignmentId),
                    ("UserId", request.UserId),
                    ("PositionId", request.PositionId),
                    ("IsPrimary", request.IsPrimary),
                    ("IsActive", true),
                    ("CreatedAtUtc", now),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Organization user position insert affected {affectedRows} rows instead of one.");
        }

        return await assignmentQueries.GetLocalByIdAsync(assignmentId, user, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserPositionResponse>> UpdateCoreAsync(
        Guid assignmentId,
        UpdateOrganizationUserPositionRequest request,
        HostUserDirectoryEntry user,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var current = await assignmentQueries.GetLocalByIdAsync(assignmentId, user, cancellationToken)
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
                    OrganizationSqlParameters.Create(
                        ("UserId", current.Value!.UserId),
                        ("AssignmentId", assignmentId),
                        ("UpdatedAtUtc", now)),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.UpdateUserPositionPrimary,
                OrganizationSqlParameters.Create(
                    ("AssignmentId", assignmentId),
                    ("IsPrimary", request.IsPrimary),
                    ("Version", request.Version),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return Conflict();
        }

        return await assignmentQueries.GetLocalByIdAsync(assignmentId, user, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUserPositionResponse>> DisableCoreAsync(
        Guid assignmentId,
        HostUserDirectoryEntry user,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.DisableUserPosition,
                OrganizationSqlParameters.Create(
                    ("AssignmentId", assignmentId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows == 0)
        {
            return NotFound();
        }

        return await assignmentQueries.GetLocalByIdAsync(assignmentId, user, cancellationToken)
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

    private static Result<OrganizationUserPositionResponse> TenantMemberRequired() =>
        Result<OrganizationUserPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UserPositionTenantMemberRequired,
            "The user is not an active tenant member.",
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

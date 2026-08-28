using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.ManageTenantUnits;

/// <summary>租户机构创建、更新与禁用。</summary>
internal sealed class TenantUnitManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IOutboxWriter outboxWriter,
    TenantUnitQueryService unitQueries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex CodePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<Result<OrganizationUnitResponse>> CreateAsync(
        CreateOrganizationUnitRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<OrganizationUnitResponse>> UpdateAsync(
        Guid unitId,
        UpdateOrganizationUnitRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(unitId, request, token),
            cancellationToken);

    public Task<Result<OrganizationUnitResponse>> DisableAsync(
        Guid unitId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DisableCoreAsync(unitId, token),
            cancellationToken);

    private async Task<Result<OrganizationUnitResponse>> CreateCoreAsync(
        CreateOrganizationUnitRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(request.Code, request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var parentId = ParseParentId(request.ParentId);
        if (request.ParentId is { Length: > 0 } && parentId is null)
        {
            return ValidationFailure("Parent unit id is invalid.");
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

        var code = request.Code.Trim();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitByTenantAndCode,
                OrganizationSqlParameters.Create(("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeConflict();
        }

        var now = clock.UtcNow;
        var unitId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.InsertUnit,
                OrganizationSqlParameters.Create(
                    ("Id", unitId),
                    ("ParentId", parentId),
                    ("Code", code),
                    ("Name", request.Name.Trim()),
                    ("DisplayOrder", request.DisplayOrder),
                    ("IsActive", true),
                    ("CreatedAtUtc", now),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Organization unit insert affected {affectedRows} rows instead of one.");
        }

        var created = await unitQueries.FindByIdAsync(unitId, cancellationToken).ConfigureAwait(false);
        if (created.IsSuccess)
        {
            await PublishUnitChangedAsync(
                    currentTenant.Id!.Value,
                    created.Value!,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return created;
    }

    private async Task<Result<OrganizationUnitResponse>> UpdateCoreAsync(
        Guid unitId,
        UpdateOrganizationUnitRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(code: null, request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                OrganizationSqlParameters.Create(("UnitId", unitId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var parentId = ParseParentId(request.ParentId);
        if (request.ParentId is { Length: > 0 } && parentId is null)
        {
            return ValidationFailure("Parent unit id is invalid.");
        }

        if (parentId is Guid parsedParentId)
        {
            if (parsedParentId == unitId)
            {
                return ParentCycle();
            }

            var parentError = await EnsureParentExistsAsync(parsedParentId, cancellationToken)
                .ConfigureAwait(false);
            if (parentError is not null)
            {
                return parentError;
            }

            // 沿拟议上级向上游走；若碰到当前节点，说明会挂到自身后代之下形成环。
            if (await WouldCreateParentCycleAsync(unitId, parsedParentId, cancellationToken)
                    .ConfigureAwait(false))
            {
                return ParentCycle();
            }
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.UpdateUnit,
                OrganizationSqlParameters.Create(
                    ("UnitId", unitId),
                    ("ParentId", parentId),
                    ("Name", request.Name.Trim()),
                    ("DisplayOrder", request.DisplayOrder),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(unitId, cancellationToken)
                .ConfigureAwait(false);
        }

        var updated = await unitQueries.FindByIdAsync(unitId, cancellationToken).ConfigureAwait(false);
        if (updated.IsSuccess)
        {
            await PublishUnitChangedAsync(
                    currentTenant.Id!.Value,
                    updated.Value!,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return updated;
    }

    private async Task<Result<OrganizationUnitResponse>> DisableCoreAsync(
        Guid unitId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                OrganizationSqlParameters.Create(("UnitId", unitId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.DisableUnit,
                OrganizationSqlParameters.Create(("UnitId", unitId), ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        var disabled = await unitQueries.FindByIdAsync(unitId, cancellationToken).ConfigureAwait(false);
        if (disabled.IsSuccess)
        {
            await PublishUnitChangedAsync(
                    currentTenant.Id!.Value,
                    disabled.Value!,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return disabled;
    }

    private async Task PublishUnitChangedAsync(
        Guid tenantId,
        OrganizationUnitResponse unit,
        DateTimeOffset changedAtUtc,
        CancellationToken cancellationToken) =>
        await outboxWriter.AddAsync(
                IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged,
                1,
                new IdentityOrganizationUnitChangedIntegrationEvent(
                    tenantId,
                    unit.Id,
                    unit.Name,
                    unit.IsActive,
                    unit.Version,
                    unit.UpdatedAtUtc ?? changedAtUtc),
                IntegrationEventMetadata.Create(
                    partitionKey: tenantId.ToString("D"),
                    producer: "fullnet.organization"),
                cancellationToken)
            .ConfigureAwait(false);

    private async Task<Result<OrganizationUnitResponse>?> EnsureParentExistsAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var parent = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                OrganizationSqlParameters.Create(("UnitId", parentId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (parent is null || !parent.IsActive)
        {
            return ValidationFailure("Parent unit was not found or is inactive.");
        }

        return null;
    }

    private async Task<bool> WouldCreateParentCycleAsync(
        Guid unitId,
        Guid newParentId,
        CancellationToken cancellationToken)
    {
        var links = await queryExecutor.QueryAsync<OrganizationUnitParentLink>(
                OrganizationSql.ListUnitParentLinks,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var parentById = links.ToDictionary(link => link.Id, link => link.ParentId);
        var current = (Guid?)newParentId;
        var seen = new HashSet<Guid>();
        while (current is Guid id)
        {
            if (id == unitId)
            {
                return true;
            }

            // 已有脏环时也失败关闭，避免无限向上游走。
            if (!seen.Add(id))
            {
                return true;
            }

            if (!parentById.TryGetValue(id, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    private async Task<Result<OrganizationUnitResponse>> ResolveUpdateFailureAsync(
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                OrganizationSqlParameters.Create(("UnitId", unitId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return VersionConflict();
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("organization.tenant_context_required");
        }
    }

    private static Result<OrganizationUnitResponse>? ValidateWriteRequest(
        string? code,
        string name)
    {
        if (code is not null && !CodePattern.IsMatch(code.Trim()))
        {
            return ValidationFailure("Unit code is invalid.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 128)
        {
            return ValidationFailure("Unit name is invalid.");
        }

        return null;
    }

    private static Guid? ParseParentId(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return null;
        }

        return Guid.TryParse(parentId, out var parsed) ? parsed : null;
    }

    private static Result<OrganizationUnitResponse> CodeConflict() =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UnitCodeExists,
            "An organization unit with this code already exists.",
            ErrorType.Conflict));

    private static Result<OrganizationUnitResponse> NotFound() =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UnitNotFound,
            "The organization unit was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationUnitResponse> VersionConflict() =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The organization unit was updated concurrently.",
            ErrorType.Conflict));

    private static Result<OrganizationUnitResponse> ParentCycle() =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            OrganizationErrorCodes.UnitParentCycle,
            "The selected parent would create a cycle in the organization tree.",
            ErrorType.Validation));

    private static Result<OrganizationUnitResponse> ValidationFailure(string message) =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));
}

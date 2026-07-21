using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Persistence;

namespace Full.NET.Modules.Organization.Features.ManageTenantUnits;

/// <summary>租户机构创建、更新与禁用。</summary>
internal sealed class TenantUnitManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
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
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<OrganizationUnitResponse>> UpdateAsync(
        Guid unitId,
        UpdateOrganizationUnitRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(unitId, request, token),
            cancellationToken);

    public Task<Result<OrganizationUnitResponse>> DisableAsync(
        Guid unitId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
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
                new { Code = code },
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
                new InsertOrganizationUnit(
                    unitId,
                    currentTenant.Id!.Value,
                    parentId,
                    code,
                    request.Name.Trim(),
                    request.DisplayOrder,
                    true,
                    now,
                    null,
                    1),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                $"Organization unit insert affected {affectedRows} rows instead of one.");
        }

        return await unitQueries.FindByIdAsync(unitId, cancellationToken).ConfigureAwait(false);
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
                new { UnitId = unitId },
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
                return ValidationFailure("A unit cannot be its own parent.");
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
                OrganizationSql.UpdateUnit,
                new
                {
                    UnitId = unitId,
                    ParentId = parentId,
                    Name = request.Name.Trim(),
                    DisplayOrder = request.DisplayOrder,
                    UpdatedAtUtc = now,
                    request.Version,
                },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(unitId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await unitQueries.FindByIdAsync(unitId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUnitResponse>> DisableCoreAsync(
        Guid unitId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                new { UnitId = unitId },
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                OrganizationSql.DisableUnit,
                new { UnitId = unitId, UpdatedAtUtc = now },
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        return await unitQueries.FindByIdAsync(unitId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<OrganizationUnitResponse>?> EnsureParentExistsAsync(
        Guid parentId,
        CancellationToken cancellationToken)
    {
        var parent = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                new { UnitId = parentId },
                cancellationToken)
            .ConfigureAwait(false);
        if (parent is null || !parent.IsActive)
        {
            return ValidationFailure("Parent unit was not found or is inactive.");
        }

        return null;
    }

    private async Task<Result<OrganizationUnitResponse>> ResolveUpdateFailureAsync(
        Guid unitId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitById,
                new { UnitId = unitId },
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

    private static Result<OrganizationUnitResponse> ValidationFailure(string message) =>
        Result<OrganizationUnitResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));
}

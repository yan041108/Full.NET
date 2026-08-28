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

namespace Full.NET.Modules.Organization.Features.ManageTenantPositionLevels;

/// <summary>租户职级创建、更新与禁用。</summary>
internal sealed class TenantPositionLevelManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantPositionLevelQueryService positionLevelQueries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex CodePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<Result<OrganizationPositionLevelResponse>> CreateAsync(
        CreateOrganizationPositionLevelRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<OrganizationPositionLevelResponse>> UpdateAsync(
        Guid positionLevelId,
        UpdateOrganizationPositionLevelRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(positionLevelId, request, token),
            cancellationToken);

    public Task<Result<OrganizationPositionLevelResponse>> DisableAsync(
        Guid positionLevelId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DisableCoreAsync(positionLevelId, token),
            cancellationToken);

    private async Task<Result<OrganizationPositionLevelResponse>> CreateCoreAsync(
        CreateOrganizationPositionLevelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(request.Code, request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var code = request.Code.Trim();
        var existing = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                PositionLevelSql.FindByTenantAndCode,
                OrganizationSqlParameters.Create(("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeConflict();
        }

        var now = clock.UtcNow;
        var positionLevelId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionLevelSql.Insert,
                OrganizationSqlParameters.Create(
                    ("Id", positionLevelId),
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
                $"Organization position level insert affected {affectedRows} rows instead of one.");
        }

        return await positionLevelQueries.FindByIdAsync(
                positionLevelId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionLevelResponse>> UpdateCoreAsync(
        Guid positionLevelId,
        UpdateOrganizationPositionLevelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(code: null, request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                PositionLevelSql.FindById,
                OrganizationSqlParameters.Create(("PositionLevelId", positionLevelId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionLevelSql.Update,
                OrganizationSqlParameters.Create(
                    ("PositionLevelId", positionLevelId),
                    ("Name", request.Name.Trim()),
                    ("DisplayOrder", request.DisplayOrder),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(positionLevelId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await positionLevelQueries.FindByIdAsync(
                positionLevelId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionLevelResponse>> DisableCoreAsync(
        Guid positionLevelId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                PositionLevelSql.FindById,
                OrganizationSqlParameters.Create(("PositionLevelId", positionLevelId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionLevelSql.Disable,
                OrganizationSqlParameters.Create(
                    ("PositionLevelId", positionLevelId),
                    ("UpdatedAtUtc", clock.UtcNow)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        return await positionLevelQueries.FindByIdAsync(
                positionLevelId,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionLevelResponse>> ResolveUpdateFailureAsync(
        Guid positionLevelId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor
            .QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                PositionLevelSql.FindById,
                OrganizationSqlParameters.Create(("PositionLevelId", positionLevelId)),
                cancellationToken)
            .ConfigureAwait(false);
        return record is null ? NotFound() : VersionConflict();
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException(
                "organization.tenant_context_required");
        }
    }

    private static Result<OrganizationPositionLevelResponse>? ValidateWriteRequest(
        string? code,
        string name)
    {
        if (code is not null && !CodePattern.IsMatch(code.Trim()))
        {
            return ValidationFailure("Position level code is invalid.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 128)
        {
            return ValidationFailure("Position level name is invalid.");
        }

        return null;
    }

    private static Result<OrganizationPositionLevelResponse> CodeConflict() =>
        Result<OrganizationPositionLevelResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionLevelCodeExists,
            "An organization position level with this code already exists.",
            ErrorType.Conflict));

    private static Result<OrganizationPositionLevelResponse> NotFound() =>
        Result<OrganizationPositionLevelResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionLevelNotFound,
            "The organization position level was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationPositionLevelResponse> VersionConflict() =>
        Result<OrganizationPositionLevelResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The organization position level was updated concurrently.",
            ErrorType.Conflict));

    private static Result<OrganizationPositionLevelResponse> ValidationFailure(
        string message) =>
        Result<OrganizationPositionLevelResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));
}

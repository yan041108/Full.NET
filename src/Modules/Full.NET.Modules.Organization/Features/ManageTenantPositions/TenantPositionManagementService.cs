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

namespace Full.NET.Modules.Organization.Features.ManageTenantPositions;

/// <summary>租户职位创建、更新与禁用。</summary>
internal sealed partial class TenantPositionManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantPositionQueryService positionQueries,
    ICurrentTenant currentTenant,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex CodePattern = new(
        "^[a-z][a-z0-9-]{2,63}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<Result<OrganizationPositionResponse>> CreateAsync(
        CreateOrganizationPositionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<OrganizationPositionResponse>> UpdateAsync(
        Guid positionId,
        UpdateOrganizationPositionRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => UpdateCoreAsync(positionId, request, token),
            cancellationToken);

    public Task<Result<OrganizationPositionResponse>> DisableAsync(
        Guid positionId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => DisableCoreAsync(positionId, token),
            cancellationToken);

    public Task<Result<OrganizationPositionResponse>> AssignUnitAsync(
        Guid positionId,
        AssignOrganizationPositionUnitRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => AssignUnitCoreAsync(positionId, request, token),
            cancellationToken);

    public Task<Result<OrganizationPositionResponse>> AssignPositionLevelAsync(
        Guid positionId,
        AssignOrganizationPositionLevelRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteResultAsync(
            token => AssignPositionLevelCoreAsync(positionId, request, token),
            cancellationToken);

    /// <summary>按固定模板逐行导入职位；可选机构/职级绑定受调用方能力约束。</summary>
    public async Task<Result<ImportOrganizationPositionsResponse>> ImportAsync(
        ImportOrganizationPositionsRequest request,
        OrganizationPositionImportCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);
        var rows = request.Rows ?? [];
        var results = new List<ImportOrganizationPositionRowResult>(rows.Count);
        var succeeded = 0;
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);
        var line = 0;
        foreach (var row in rows)
        {
            line++;
            if (row is null)
            {
                results.Add(new ImportOrganizationPositionRowResult(
                    line,
                    false,
                    null,
                    ValidationErrorCodes.Failed,
                    "Import row is required."));
                continue;
            }

            var normalizedCode = row.Code?.Trim() ?? string.Empty;
            if (!seenCodes.Add(normalizedCode))
            {
                results.Add(new ImportOrganizationPositionRowResult(
                    line,
                    false,
                    null,
                    OrganizationErrorCodes.PositionImportDuplicateCode,
                    "Duplicate position code in import workbook."));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.UnitCode) && !capabilities.CanAssignUnit)
            {
                results.Add(new ImportOrganizationPositionRowResult(
                    line,
                    false,
                    null,
                    CommonErrorCodes.PermissionDenied,
                    "Importing organization unit assignment is not allowed."));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.PositionLevelCode)
                && !capabilities.CanAssignPositionLevel)
            {
                results.Add(new ImportOrganizationPositionRowResult(
                    line,
                    false,
                    null,
                    CommonErrorCodes.PermissionDenied,
                    "Importing position level assignment is not allowed."));
                continue;
            }

            var imported = await transaction.ExecuteResultAsync(
                token => ImportRowCoreAsync(row, capabilities, token), cancellationToken).ConfigureAwait(false);
            if (imported.IsSuccess) succeeded++;
            results.Add(new ImportOrganizationPositionRowResult(line, imported.IsSuccess,
                imported.IsSuccess ? imported.Value!.Id : null, imported.Error?.Code, imported.Error?.Message));
        }

        return Result<ImportOrganizationPositionsResponse>.Success(
            new ImportOrganizationPositionsResponse(succeeded, results));
    }

    /// <summary>一行的岗位创建及关联属于同一本地事务；任一步失败必须回滚整行。</summary>
    private async Task<Result<OrganizationPositionResponse>> ImportRowCoreAsync(
        ImportOrganizationPositionRow row, OrganizationPositionImportCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        if ((!string.IsNullOrWhiteSpace(row.UnitCode) && !capabilities.CanAssignUnit)
            || (!string.IsNullOrWhiteSpace(row.PositionLevelCode) && !capabilities.CanAssignPositionLevel))
        {
            return Result<OrganizationPositionResponse>.Failure(new Error(CommonErrorCodes.PermissionDenied,
                "Import assignment is not allowed.", ErrorType.Forbidden));
        }
        var created = await CreateCoreAsync(new CreateOrganizationPositionRequest(
            row.Code?.Trim() ?? "", row.Name ?? "", row.DisplayOrder), cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess) return created;
        var position = created.Value!;
        if (!string.IsNullOrWhiteSpace(row.UnitCode))
        {
            var unit = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                OrganizationSql.FindUnitByTenantAndCode, OrganizationSqlParameters.Create(("Code", row.UnitCode.Trim())),
                cancellationToken).ConfigureAwait(false);
            if (unit is null || !unit.IsActive) return UnitNotFound();
            var assigned = await AssignUnitCoreAsync(position.Id,
                new AssignOrganizationPositionUnitRequest(unit.Id, position.Version), cancellationToken).ConfigureAwait(false);
            if (!assigned.IsSuccess) return assigned;
            position = assigned.Value!;
        }
        if (!string.IsNullOrWhiteSpace(row.PositionLevelCode))
        {
            var level = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                PositionLevelSql.FindByTenantAndCode, OrganizationSqlParameters.Create(("Code", row.PositionLevelCode.Trim())),
                cancellationToken).ConfigureAwait(false);
            if (level is null || !level.IsActive) return PositionLevelNotFound();
            var assigned = await AssignPositionLevelCoreAsync(position.Id,
                new AssignOrganizationPositionLevelRequest(level.Id, position.Version), cancellationToken).ConfigureAwait(false);
            if (!assigned.IsSuccess) return assigned;
            position = assigned.Value!;
        }
        return Result<OrganizationPositionResponse>.Success(position);
    }

    private async Task<Result<OrganizationPositionResponse>> CreateCoreAsync(
        CreateOrganizationPositionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(request.Code, request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var code = request.Code.Trim();
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindByTenantAndCode,
                OrganizationSqlParameters.Create(("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeConflict();
        }

        var now = clock.UtcNow;
        var positionId = idGenerator.NewId();
        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionSql.Insert,
                OrganizationSqlParameters.Create(
                    ("Id", positionId),
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
                $"Organization position insert affected {affectedRows} rows instead of one.");
        }

        return await positionQueries.FindByIdAsync(positionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionResponse>> UpdateCoreAsync(
        Guid positionId,
        UpdateOrganizationPositionRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var validationError = ValidateWriteRequest(code: null, request.Name);
        if (validationError is not null)
        {
            return validationError;
        }

        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindById,
                OrganizationSqlParameters.Create(("PositionId", positionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionSql.Update,
                OrganizationSqlParameters.Create(
                    ("PositionId", positionId),
                    ("Name", request.Name.Trim()),
                    ("DisplayOrder", request.DisplayOrder),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(positionId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await positionQueries.FindByIdAsync(positionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionResponse>> DisableCoreAsync(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindById,
                OrganizationSqlParameters.Create(("PositionId", positionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null || !record.IsActive)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionSql.Disable,
                OrganizationSqlParameters.Create(("PositionId", positionId), ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        return await positionQueries.FindByIdAsync(positionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionResponse>> AssignUnitCoreAsync(
        Guid positionId,
        AssignOrganizationPositionUnitRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var position = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindById,
                OrganizationSqlParameters.Create(("PositionId", positionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (position is null || !position.IsActive)
        {
            return NotFound();
        }

        if (request.UnitId is Guid unitId)
        {
            var unit = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationUnitRecord>(
                    OrganizationSql.FindUnitById,
                    OrganizationSqlParameters.Create(("UnitId", unitId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (unit is null || !unit.IsActive)
            {
                return UnitNotFound();
            }
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionSql.AssignUnit,
                OrganizationSqlParameters.Create(
                    ("PositionId", positionId),
                    ("UnitId", request.UnitId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(positionId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await positionQueries.FindByIdAsync(positionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<OrganizationPositionResponse>> ResolveUpdateFailureAsync(
        Guid positionId,
        CancellationToken cancellationToken)
    {
        var record = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindById,
                OrganizationSqlParameters.Create(("PositionId", positionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return NotFound();
        }

        return VersionConflict();
    }

    private async Task<Result<OrganizationPositionResponse>> AssignPositionLevelCoreAsync(
        Guid positionId,
        AssignOrganizationPositionLevelRequest request,
        CancellationToken cancellationToken)
    {
        EnsureTenantContext();
        var position = await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionRecord>(
                PositionSql.FindById,
                OrganizationSqlParameters.Create(("PositionId", positionId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (position is null || !position.IsActive)
        {
            return NotFound();
        }

        if (request.PositionLevelId is Guid positionLevelId)
        {
            var positionLevel =
                await queryExecutor.QuerySingleOrDefaultAsync<OrganizationPositionLevelRecord>(
                        PositionLevelSql.FindById,
                        OrganizationSqlParameters.Create(("PositionLevelId", positionLevelId)),
                        cancellationToken)
                    .ConfigureAwait(false);
            if (positionLevel is null || !positionLevel.IsActive)
            {
                return PositionLevelNotFound();
            }
        }

        var affectedRows = await commandExecutor.ExecuteAsync(
                PositionSql.AssignPositionLevel,
                OrganizationSqlParameters.Create(
                    ("PositionId", positionId),
                    ("PositionLevelId", request.PositionLevelId),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return await ResolveUpdateFailureAsync(positionId, cancellationToken)
                .ConfigureAwait(false);
        }

        return await positionQueries.FindByIdAsync(positionId, cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureTenantContext()
    {
        if (!currentTenant.IsAvailable || currentTenant.IsHost || currentTenant.Id is null)
        {
            throw new TenantContextMissingException("organization.tenant_context_required");
        }
    }

    private static Result<OrganizationPositionResponse>? ValidateWriteRequest(
        string? code,
        string name)
    {
        if (code is not null && !CodePattern.IsMatch(code.Trim()))
        {
            return ValidationFailure("Position code is invalid.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 128)
        {
            return ValidationFailure("Position name is invalid.");
        }

        return null;
    }

    private static Result<OrganizationPositionResponse> CodeConflict() =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionCodeExists,
            "An organization position with this code already exists.",
            ErrorType.Conflict));

    private static Result<OrganizationPositionResponse> NotFound() =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionNotFound,
            "The organization position was not found.",
            ErrorType.NotFound));

    private static Result<OrganizationPositionResponse> UnitNotFound() =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.UnitNotFound,
            "The organization unit was not found or is inactive.",
            ErrorType.NotFound));

    private static Result<OrganizationPositionResponse> PositionLevelNotFound() =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            OrganizationErrorCodes.PositionLevelNotFound,
            "The organization position level was not found or is inactive.",
            ErrorType.NotFound));

    private static Result<OrganizationPositionResponse> VersionConflict() =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            IdentityErrorCodes.ProfileVersionConflict,
            "The organization position was updated concurrently.",
            ErrorType.Conflict));

    private static Result<OrganizationPositionResponse> ValidationFailure(string message) =>
        Result<OrganizationPositionResponse>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));
}

using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageHostTenantPackages;

/// <summary>Host 租户套餐创建、更新与禁用。</summary>
internal sealed partial class HostTenantPackageManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    HostTenantPackageQueryService packageQueries,
    IClock clock,
    IIdGenerator idGenerator)
{
    public Task<Result<TenantPackageSummary>> CreateAsync(
        CreateHostTenantPackageRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCoreAsync(request, token),
            cancellationToken);

    public Task<Result<TenantPackageSummary>> UpdateAsync(
        Guid packageId,
        UpdateHostTenantPackageRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateCoreAsync(packageId, request, token),
            cancellationToken);

    public Task<Result<TenantPackageSummary>> DisableAsync(
        Guid packageId,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => DisableCoreAsync(packageId, token),
            cancellationToken);

    private async Task<Result<TenantPackageSummary>> CreateCoreAsync(
        CreateHostTenantPackageRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeCode(request.Code);
        if (!CodePattern().IsMatch(code))
        {
            return ValidationFailure(
                "Package code must be 3-64 lowercase letters, numbers, or hyphens.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return ValidationFailure("Package name is invalid.");
        }

        var description = NormalizeDescription(request.Description);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Package description must not exceed 512 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
                TenantPackageSql.FindByCode,
                TenancySqlParameters.Create(("Code", code)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return CodeExists();
        }

        var now = clock.UtcNow;
        var packageId = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                TenantPackageSql.Insert,
                TenancySqlParameters.Create(
                    ("Id", packageId),
                    ("Code", code),
                    ("Name", name),
                    ("Description", description),
                    ("IsActive", true),
                    ("CreatedAtUtc", now),
                    ("Version", 1)),
                cancellationToken)
            .ConfigureAwait(false);

        return await packageQueries.GetByIdAsync(packageId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantPackageSummary>> UpdateCoreAsync(
        Guid packageId,
        UpdateHostTenantPackageRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 128)
        {
            return ValidationFailure("Package name is invalid.");
        }

        var description = NormalizeDescription(request.Description);
        if (description is { Length: > 512 })
        {
            return ValidationFailure("Package description must not exceed 512 characters.");
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
                TenantPackageSql.FindPackageById,
                TenancySqlParameters.Create(("PackageId", packageId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantPackageSql.UpdateHostPackage,
                TenancySqlParameters.Create(
                    ("PackageId", packageId),
                    ("Name", name),
                    ("Description", description),
                    ("UpdatedAtUtc", now),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            var stillExists = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
                    TenantPackageSql.FindPackageById,
                    TenancySqlParameters.Create(("PackageId", packageId)),
                    cancellationToken)
                .ConfigureAwait(false);
            if (stillExists is null)
            {
                return NotFound();
            }

            return VersionConflict();
        }

        return await packageQueries.GetByIdAsync(packageId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<TenantPackageSummary>> DisableCoreAsync(
        Guid packageId,
        CancellationToken cancellationToken)
    {
        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantPackageIdentityRecord>(
                TenantPackageSql.FindPackageById,
                TenancySqlParameters.Create(("PackageId", packageId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return NotFound();
        }

        if (!existing.IsActive)
        {
            return await packageQueries.GetByIdAsync(packageId, cancellationToken)
                .ConfigureAwait(false);
        }

        var assignedTenantCount = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                TenantPackageSql.CountAssignedTenants,
                TenancySqlParameters.Create(("PackageId", packageId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (assignedTenantCount > 0)
        {
            return PackageInUse();
        }

        var now = clock.UtcNow;
        var affectedRows = await commandExecutor.ExecuteAsync(
                TenantPackageSql.DisableHostPackage,
                TenancySqlParameters.Create(
                    ("PackageId", packageId),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affectedRows != 1)
        {
            return NotFound();
        }

        return await packageQueries.GetByIdAsync(packageId, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? NormalizeDescription(string? description)
    {
        if (description is null)
        {
            return null;
        }

        var trimmed = description.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static Result<TenantPackageSummary> ValidationFailure(string message) =>
        Result<TenantPackageSummary>.Failure(new Error(
            ValidationErrorCodes.Failed,
            message,
            ErrorType.Validation));

    private static Result<TenantPackageSummary> NotFound() =>
        Result<TenantPackageSummary>.Failure(new Error(
            TenancyErrorCodes.PackageNotFound,
            "The tenant package was not found.",
            ErrorType.NotFound));

    private static Result<TenantPackageSummary> CodeExists() =>
        Result<TenantPackageSummary>.Failure(new Error(
            TenancyErrorCodes.PackageCodeExists,
            "A tenant package with the same code already exists.",
            ErrorType.Conflict));

    private static Result<TenantPackageSummary> VersionConflict() =>
        Result<TenantPackageSummary>.Failure(new Error(
            TenancyErrorCodes.PackageVersionConflict,
            "The tenant package record was updated concurrently.",
            ErrorType.Conflict));

    private static Result<TenantPackageSummary> PackageInUse() =>
        Result<TenantPackageSummary>.Failure(new Error(
            TenancyErrorCodes.PackageInUse,
            "The tenant package is still assigned to one or more tenants.",
            ErrorType.BusinessRule));

    [GeneratedRegex(
        "^[a-z0-9][a-z0-9-]{1,62}[a-z0-9]$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}

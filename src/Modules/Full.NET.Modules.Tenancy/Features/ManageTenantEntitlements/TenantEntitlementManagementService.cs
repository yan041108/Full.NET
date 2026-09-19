using System.Text.RegularExpressions;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements.Persistence;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantEntitlements;

internal sealed partial class TenantEntitlementManagementService(
    IQueryExecutor queryExecutor,
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    TenantEntitlementQueryService queries,
    IClock clock,
    IIdGenerator idGenerator)
{
    private static readonly Regex CodePattern = CodeRegex();

    public Task<Result<TenantEntitlementCatalogResponse>> CreateCatalogAsync(
        CreateTenantEntitlementCatalogRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateCatalogCoreAsync(request, token),
            cancellationToken);

    public Task<Result<TenantEntitlementBindingResponse>> CreateBindingAsync(
        Guid tenantId,
        CreateTenantEntitlementBindingRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => CreateBindingCoreAsync(tenantId, request, token),
            cancellationToken);

    public Task<Result<TenantEntitlementEnforcementResponse>> UpdateEnforcementPhaseAsync(
        UpdateTenantEntitlementEnforcementRequest request,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => UpdateEnforcementPhaseCoreAsync(request, token),
            cancellationToken);

    internal static Result<string> ValidateCatalogCode(string? code)
    {
        var normalized = code?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!CodePattern.IsMatch(normalized))
        {
            return Result<string>.Failure(new Error(
                TenancyErrorCodes.EntitlementCodeInvalid,
                "Entitlement code is invalid.",
                ErrorType.Validation));
        }

        return Result<string>.Success(normalized);
    }

    private async Task<Result<TenantEntitlementCatalogResponse>> CreateCatalogCoreAsync(
        CreateTenantEntitlementCatalogRequest request,
        CancellationToken cancellationToken)
    {
        var codeValidation = ValidateCatalogCode(request.Code);
        if (!codeValidation.IsSuccess)
        {
            return Result<TenantEntitlementCatalogResponse>.Failure(codeValidation.Error!);
        }

        var existing = await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.FindCatalogByCode,
                Tenancy.Persistence.TenancySqlParameters.Create(("Code", codeValidation.Value)),
                cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<TenantEntitlementCatalogResponse>.Failure(new Error(
                TenancyErrorCodes.EntitlementCodeExists,
                "The entitlement code already exists.",
                ErrorType.Conflict));
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                TenantEntitlementSql.InsertCatalog,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("Id", id),
                    ("Code", codeValidation.Value),
                    ("Name", request.Name.Trim()),
                    ("Description", request.Description?.Trim()),
                    ("EntitlementType", request.EntitlementType.Trim()),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        return Result<TenantEntitlementCatalogResponse>.Success(
            new TenantEntitlementCatalogResponse(
                id,
                codeValidation.Value!,
                request.Name.Trim(),
                request.Description?.Trim(),
                request.EntitlementType.Trim(),
                true,
                1));
    }

    private async Task<Result<TenantEntitlementBindingResponse>> CreateBindingCoreAsync(
        Guid tenantId,
        CreateTenantEntitlementBindingRequest request,
        CancellationToken cancellationToken)
    {
        var entitlement = await queryExecutor.QuerySingleOrDefaultAsync<TenantEntitlementCatalogRecord>(
                TenantEntitlementSql.FindCatalogById,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("EntitlementId", request.EntitlementId)),
                cancellationToken)
            .ConfigureAwait(false);
        if (entitlement is null || !entitlement.IsActive)
        {
            return Result<TenantEntitlementBindingResponse>.Failure(new Error(
                TenancyErrorCodes.EntitlementBindingNotFound,
                "The entitlement catalog entry was not found.",
                ErrorType.NotFound));
        }

        var now = clock.UtcNow;
        var id = idGenerator.NewId();
        await commandExecutor.ExecuteAsync(
                TenantEntitlementSql.InsertBinding,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("Id", id),
                    ("TenantId", tenantId),
                    ("EntitlementId", request.EntitlementId),
                    ("EffectiveFromUtc", request.EffectiveFromUtc),
                    ("EffectiveToUtc", request.EffectiveToUtc),
                    ("SourcePackageId", request.SourcePackageId),
                    ("CreatedAtUtc", now),
                    ("UpdatedAtUtc", now)),
                cancellationToken)
            .ConfigureAwait(false);
        var bindings = await queries.ListBindingsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);
        var created = bindings.Value!.FirstOrDefault(binding => binding.Id == id);
        if (created is null)
        {
            return Result<TenantEntitlementBindingResponse>.Failure(new Error(
                TenancyErrorCodes.EntitlementBindingNotFound,
                "The entitlement binding was not found after creation.",
                ErrorType.NotFound));
        }

        return Result<TenantEntitlementBindingResponse>.Success(created);
    }

    private async Task<Result<TenantEntitlementEnforcementResponse>> UpdateEnforcementPhaseCoreAsync(
        UpdateTenantEntitlementEnforcementRequest request,
        CancellationToken cancellationToken)
    {
        var phase = request.Phase?.Trim() ?? string.Empty;
        if (phase is not (
            TenantEntitlementEnforcementPhases.Compatibility
            or TenantEntitlementEnforcementPhases.Shadow
            or TenantEntitlementEnforcementPhases.Enforced))
        {
            return Result<TenantEntitlementEnforcementResponse>.Failure(new Error(
                TenancyErrorCodes.EntitlementPhaseInvalid,
                "Entitlement enforcement phase is invalid.",
                ErrorType.Validation));
        }

        var affected = await commandExecutor.ExecuteAsync(
                TenantEntitlementSql.UpdateEnforcementPhase,
                Tenancy.Persistence.TenancySqlParameters.Create(
                    ("SettingsId", TenancySettingsConstants.SettingsId),
                    ("Phase", phase),
                    ("UpdatedAtUtc", clock.UtcNow),
                    ("Version", request.Version)),
                cancellationToken)
            .ConfigureAwait(false);
        if (affected != 1)
        {
            return Result<TenantEntitlementEnforcementResponse>.Failure(new Error(
                TenancyErrorCodes.SettingsVersionConflict,
                "Tenancy settings were updated concurrently.",
                ErrorType.Conflict));
        }

        return await queries.GetEnforcementPhaseAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{2,63}$")]
    private static partial Regex CodeRegex();
}

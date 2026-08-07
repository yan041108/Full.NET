using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>按 keyset 分页对账 Organization 源与 Identity 本地机构单元投影。</summary>
internal sealed class OrganizationUnitProjectionReconciliationService(
    IIdentityOrganizationUnitProjectionSource source,
    IQueryExecutor queryExecutor,
    OrganizationUnitProjectionWriter writer)
{
    public Task<Result<ReconcileOrganizationUnitProjectionResponse>> ReconcileAsync(
        ReconcileOrganizationUnitProjectionRequest request,
        CancellationToken cancellationToken = default) =>
        ReconcileAsync(
            request.TenantId,
            request.AfterUnitId,
            request.PageSize,
            request.Mode,
            cancellationToken);

    public async Task<Result<ReconcileOrganizationUnitProjectionResponse>> ReconcileAsync(
        Guid tenantId,
        Guid? afterUnitId,
        int pageSize,
        string mode,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Failure(IdentityErrorCodes.OrganizationUnitProjectionInvalidTenant);
        }

        if (pageSize is < 1 or > 100)
        {
            return Failure(IdentityErrorCodes.OrganizationUnitProjectionInvalidPageSize);
        }

        var apply = false;
        if (string.Equals(
                mode,
                IdentityOrganizationUnitProjectionReconciliationModes.DryRun,
                StringComparison.Ordinal))
        {
            apply = false;
        }
        else if (string.Equals(
                     mode,
                     IdentityOrganizationUnitProjectionReconciliationModes.Apply,
                     StringComparison.Ordinal))
        {
            apply = true;
        }
        else
        {
            return Failure(IdentityErrorCodes.OrganizationUnitProjectionInvalidMode);
        }

        var pageResult = await source.ListAsync(
                tenantId,
                afterUnitId,
                pageSize,
                cancellationToken)
            .ConfigureAwait(false);
        if (!pageResult.IsSuccess)
        {
            return Result<ReconcileOrganizationUnitProjectionResponse>.Failure(
                pageResult.Error!);
        }

        var page = pageResult.Value!;
        if (page.Items.Count == 0)
        {
            return Result<ReconcileOrganizationUnitProjectionResponse>.Success(
                new ReconcileOrganizationUnitProjectionResponse(
                    tenantId,
                    0,
                    0,
                    0,
                    0,
                    0,
                    null,
                    false,
                    true));
        }

        var localRows = await LoadLocalRowsAsync(
                tenantId,
                page.Items.Select(item => item.UnitId).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var localByUnitId = localRows.ToDictionary(row => row.UnitId);

        var missing = 0;
        var stale = 0;
        var extra = 0;
        var applied = 0;
        foreach (var snapshot in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            localByUnitId.TryGetValue(snapshot.UnitId, out var local);
            switch (Classify(snapshot, local))
            {
                case ProjectionDifferenceKind.Missing:
                    missing++;
                    if (apply)
                    {
                        await writer.ApplySnapshotAsync(
                                tenantId,
                                snapshot,
                                cancellationToken)
                            .ConfigureAwait(false);
                        applied++;
                    }

                    break;
                case ProjectionDifferenceKind.Stale:
                    stale++;
                    if (apply)
                    {
                        await writer.ApplySnapshotAsync(
                                tenantId,
                                snapshot,
                                cancellationToken)
                            .ConfigureAwait(false);
                        applied++;
                    }

                    break;
                case ProjectionDifferenceKind.Extra:
                    extra++;
                    break;
            }
        }

        return Result<ReconcileOrganizationUnitProjectionResponse>.Success(
            new ReconcileOrganizationUnitProjectionResponse(
                tenantId,
                page.Items.Count,
                missing,
                stale,
                extra,
                applied,
                page.NextAfterUnitId,
                page.HasMore,
                !page.HasMore));
    }

    private async Task<IReadOnlyList<OrganizationUnitProjectionRecord>> LoadLocalRowsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> unitIds,
        CancellationToken cancellationToken)
    {
        if (unitIds.Count == 0)
        {
            return [];
        }

        return await queryExecutor.QueryAsync<OrganizationUnitProjectionRecord>(
                OrganizationUnitProjectionSql.FindByTenantAndUnits,
                new { TenantId = tenantId, UnitIds = unitIds },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static ProjectionDifferenceKind Classify(
        IdentityOrganizationUnitProjectionSnapshot snapshot,
        OrganizationUnitProjectionRecord? local)
    {
        if (local is null)
        {
            return ProjectionDifferenceKind.Missing;
        }

        if (local.SourceVersion > snapshot.Version)
        {
            return ProjectionDifferenceKind.Extra;
        }

        if (local.SourceVersion < snapshot.Version
            || !string.Equals(local.Name, snapshot.Name, StringComparison.Ordinal)
            || local.IsActive != snapshot.IsActive)
        {
            return ProjectionDifferenceKind.Stale;
        }

        return ProjectionDifferenceKind.InSync;
    }

    private static Result<ReconcileOrganizationUnitProjectionResponse> Failure(string code) =>
        Result<ReconcileOrganizationUnitProjectionResponse>.Failure(new Error(
            code,
            code,
            ErrorType.Validation));

    private enum ProjectionDifferenceKind
    {
        InSync,
        Missing,
        Stale,
        Extra,
    }
}
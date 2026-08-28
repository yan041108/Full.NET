using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.OrganizationUnitProjection;

/// <summary>按版本单调写入 Identity 本地机构单元投影。</summary>
internal sealed class OrganizationUnitProjectionWriter(
    ICommandExecutor commandExecutor,
    ICommandTransaction transaction,
    IClock clock)
{
    public Task ApplyAsync(
        IdentityOrganizationUnitChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default) =>
        transaction.ExecuteAsync(
            token => ApplyCoreAsync(integrationEvent, token),
            cancellationToken);

    public Task ApplySnapshotAsync(
        Guid tenantId,
        IdentityOrganizationUnitProjectionSnapshot snapshot,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            new IdentityOrganizationUnitChangedIntegrationEvent(
                tenantId,
                snapshot.UnitId,
                snapshot.Name,
                snapshot.IsActive,
                snapshot.Version,
                snapshot.ChangedAtUtc),
            cancellationToken);

    private async Task<bool> ApplyCoreAsync(
        IdentityOrganizationUnitChangedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        var projectedAtUtc = clock.UtcNow;
        var updatedRows = await commandExecutor.ExecuteAsync(
                OrganizationUnitProjectionSql.UpdateIfNewer,
                IdentitySqlParameters.Create(
                    ("TenantId", integrationEvent.TenantId),
                    ("UnitId", integrationEvent.UnitId),
                    ("Name", integrationEvent.Name),
                    ("IsActive", integrationEvent.IsActive),
                    ("SourceVersion", integrationEvent.Version),
                    ("SourceUpdatedAtUtc", integrationEvent.ChangedAtUtc),
                    ("ProjectedAtUtc", projectedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);
        if (updatedRows > 0)
        {
            return true;
        }

        await commandExecutor.ExecuteAsync(
                OrganizationUnitProjectionSql.InsertIfMissing,
                IdentitySqlParameters.Create(
                    ("TenantId", integrationEvent.TenantId),
                    ("UnitId", integrationEvent.UnitId),
                    ("Name", integrationEvent.Name),
                    ("IsActive", integrationEvent.IsActive),
                    ("SourceVersion", integrationEvent.Version),
                    ("SourceUpdatedAtUtc", integrationEvent.ChangedAtUtc),
                    ("ProjectedAtUtc", projectedAtUtc)),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}

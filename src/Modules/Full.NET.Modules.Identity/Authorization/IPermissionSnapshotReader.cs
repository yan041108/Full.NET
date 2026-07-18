namespace Full.NET.Modules.Identity.Authorization;

internal interface IPermissionSnapshotReader
{
    Task<PermissionSnapshot> ReadAsync(
        Guid userId,
        string scopeKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

internal sealed record PermissionSnapshot(
    IReadOnlyList<string> Permissions,
    bool IsSuperAdministrator);

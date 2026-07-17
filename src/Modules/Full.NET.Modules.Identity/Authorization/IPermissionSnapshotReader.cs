namespace Full.NET.Modules.Identity.Authorization;

internal interface IPermissionSnapshotReader
{
    Task<IReadOnlyList<string>> ReadAsync(
        Guid userId,
        string scopeKey,
        Guid? tenantId,
        CancellationToken cancellationToken = default);
}

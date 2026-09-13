using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

/// <summary>后台运行重查会话与权限快照，不缓存 ClaimsPrincipal。</summary>
internal sealed class BackgroundSessionAuthorization(
    IBackgroundSessionBindingValidator bindingValidator,
    IPermissionSnapshotReader permissions) : IBackgroundSessionAuthorization
{
    public async Task<AuthorizedSessionActor?> AuthorizeAsync(
        SessionBindingSnapshot binding,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(permissionCode)
            || !await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var snapshot = await permissions.ReadAsync(
            binding.UserId,
            binding.ActorScope,
            binding.TenantId,
            cancellationToken).ConfigureAwait(false);
        return snapshot.IsSuperAdministrator || snapshot.Permissions.Contains(permissionCode, StringComparer.Ordinal)
            ? new(binding.UserId, binding.TenantId, binding.SessionId)
            : null;
    }
}

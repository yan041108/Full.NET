using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Security;

/// <summary>后台运行重查会话与权限快照，不缓存 ClaimsPrincipal。</summary>
internal sealed class BackgroundSessionAuthorization(
    IBackgroundSessionBindingValidator bindingValidator,
    IPermissionSnapshotReader permissions) : IBackgroundSessionAuthorization
{
    public Task<AuthorizedSessionActor?> AuthorizeAsync(
        SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default) =>
        AuthorizeAllAsync(binding, [permissionCode], cancellationToken);

    public async Task<AuthorizedSessionActor?> AuthorizeAllAsync(
        SessionBindingSnapshot binding, IReadOnlyCollection<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(permissionCodes);
        // 在首个 await 前冻结要求，调用方不能在校验期间删减权限。
        var required = permissionCodes.ToArray();
        if (required.Length == 0 || required.Any(string.IsNullOrWhiteSpace)
            || !await bindingValidator.IsValidAsync(binding, cancellationToken).ConfigureAwait(false))
            return null;
        var snapshot = await permissions.ReadAsync(binding.UserId, binding.ActorScope,
            binding.TenantId, cancellationToken).ConfigureAwait(false);
        return snapshot.IsSuperAdministrator
            || required.All(code => snapshot.Permissions.Contains(code, StringComparer.Ordinal))
            ? new(binding.UserId, binding.TenantId, binding.SessionId) : null;
    }
}

using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Tenancy.Features;

/// <summary>在单次请求内临时切换到 Host 上下文，执行 HostOnly SQL 后恢复租户上下文。</summary>
internal static class TenancyHostExecutionScope
{
    internal static async Task<T> RunAsync<T>(
        ICurrentTenantContextWriter currentTenant,
        Func<Task<T>> action)
    {
        var snapshot = CaptureTenantSnapshot(currentTenant);
        currentTenant.SetHost();
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            RestoreTenantSnapshot(currentTenant, snapshot);
        }
    }

    internal static Task RunAsync(
        ICurrentTenantContextWriter currentTenant,
        Func<Task> action) =>
        RunAsync(
            currentTenant,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            });

    private static (bool IsHost, TenantContext? Tenant) CaptureTenantSnapshot(
        ICurrentTenantContextWriter currentTenant) =>
        (currentTenant.IsHost, currentTenant.IsHost || currentTenant.Id is null
            ? null
            : new TenantContext(
                currentTenant.Id!.Value,
                currentTenant.Identifier ?? string.Empty,
                currentTenant.Name ?? string.Empty));

    private static void RestoreTenantSnapshot(
        ICurrentTenantContextWriter currentTenant,
        (bool IsHost, TenantContext? Tenant) snapshot)
    {
        if (snapshot.IsHost)
        {
            currentTenant.SetHost();
            return;
        }

        if (snapshot.Tenant is not null)
        {
            currentTenant.SetTenant(snapshot.Tenant);
            return;
        }

        currentTenant.Clear();
    }
}

using Full.NET.Abstractions.Tenancy;

namespace Full.NET.Modules.Jobs.Middleware;

/// <summary>
/// 在租户请求上下文中临时切换到 Host 数据作用域，供超级管理员跨上下文访问 Host 作业 API。
/// </summary>
internal static class HostJobsHostContextScope
{
    internal static Task RunAsync(
        ICurrentTenant currentTenant,
        ICurrentTenantContextWriter tenantWriter,
        Func<Task> action) =>
        RunAsync(
            currentTenant,
            tenantWriter,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            });

    internal static async Task<T> RunAsync<T>(
        ICurrentTenant currentTenant,
        ICurrentTenantContextWriter tenantWriter,
        Func<Task<T>> action)
    {
        if (currentTenant.IsHost)
        {
            return await action().ConfigureAwait(false);
        }

        var wasHost = currentTenant.IsHost;
        var previousTenantId = currentTenant.Id;
        var previousIdentifier = currentTenant.Identifier;
        var previousName = currentTenant.Name;

        tenantWriter.SetHost();
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            if (wasHost)
            {
                tenantWriter.SetHost();
            }
            else if (previousTenantId is Guid restoredTenantId
                     && previousIdentifier is not null
                     && previousName is not null)
            {
                tenantWriter.SetTenant(
                    new TenantContext(restoredTenantId, previousIdentifier, previousName));
            }
            else
            {
                tenantWriter.Clear();
            }
        }
    }
}

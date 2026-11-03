using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Tenancy.Features.ManageTenantLifecycle;

/// <summary>必须在 Tenancy 本地事务中领取；不同停止入口共享一行锁，最后一名判断与写入不可交错。</summary>
internal sealed class TenantLifecycleWriteGuard(ICommandExecutor commands)
{
    internal static readonly SqlStatement Acquire = new("tenancy.lifecycle.acquire_guard", """
        UPDATE fn_tenancy_lifecycle_guard SET Version=Version+1 WHERE Id=@Id
        """, SqlDataScope.HostOnly);
    internal static readonly Guid GuardId = Guid.Parse("019a4906-51a0-7000-8000-000000000001");

    internal async Task EnterAsync(CancellationToken token)
    {
        if (await commands.ExecuteAsync(Acquire, new Dictionary<string, object?> { ["Id"] = GuardId }, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("tenancy.lifecycle.guard_missing");
    }
}

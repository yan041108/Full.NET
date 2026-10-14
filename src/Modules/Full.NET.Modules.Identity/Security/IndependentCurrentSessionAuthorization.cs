using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.Modules.Identity.Security;

/// <summary>隔离跨模块权威读取的连接与事务；复制可信上下文值，不能共享或回写父级租户实例。</summary>
internal sealed class IndependentCurrentSessionAuthorization(IServiceScopeFactory scopes, ICurrentTenant current,
    IDatabaseAuthorizationReadScope capacity)
    : IIndependentCurrentSessionAuthorization
{
    public bool HasIndependentReadCapacity => capacity.HasIndependentReadCapacity;
    public async Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasIndependentReadCapacity) throw new Full.NET.Abstractions.Results.ServiceCapacityExceededException(
            Full.NET.Abstractions.Results.ServiceCapacityFailureKind.Rejected, TimeSpan.FromMilliseconds(250));
        var tenantId = current.Id; var host = current.IsHost;
        if (tenantId == Guid.Empty || tenantId.HasValue && host || !tenantId.HasValue && !host) return null;
        var identifier = current.Identifier ?? tenantId?.ToString("N") ?? "host";
        var name = current.Name ?? identifier;
        await using var scope = scopes.CreateAsyncScope();
        // 必须独立解析 DbSession；误用共享作用域时立即拒绝，不能让 Identity 查询参加业务事务。
        if (scope.ServiceProvider.GetRequiredService<IDataTransactionState>().HasTransaction)
            throw new InvalidOperationException("Independent authorization cannot join a transaction.");
        var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        using var admission = scope.ServiceProvider.GetRequiredService<IDatabaseAuthorizationReadScope>().Enter();
        if (tenantId is { } id) writer.SetTenant(new(id, identifier, name)); else writer.SetHost();
        try
        {
            var actor = await scope.ServiceProvider.GetRequiredService<ICurrentSessionAuthorization>()
                .AuthorizeAsync(permissionCode, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return current.Id == tenantId && current.IsHost == host && actor?.TenantId == tenantId ? actor : null;
        }
        finally { writer.Clear(); }
    }
}

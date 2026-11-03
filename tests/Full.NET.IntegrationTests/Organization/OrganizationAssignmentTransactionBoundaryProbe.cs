using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Organization;

/// <summary>保留真实 Identity 实现及 Scoped DbSession，观察管理服务间接调用时的实际事务状态。</summary>
internal sealed class OrganizationAssignmentTransactionBoundaryProbe
{
    private int _reads;

    internal void Configure(IServiceCollection services)
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(IHostUserDisplayDirectory));
        // 正式注册使用工厂解析同一个 Scoped 目录；不能替换为独立 Scope 掩盖事务泄漏。
        var factory = original.ImplementationFactory
            ?? throw new InvalidOperationException("Identity 展示目录的正式注册必须提供 Scoped 工厂。");
        services.Replace(ServiceDescriptor.Scoped<IHostUserDisplayDirectory>(provider =>
            new ObservedDirectory((IHostUserDisplayDirectory)factory(provider),
                provider.GetRequiredService<IDataTransactionState>(), this)));
    }

    internal void AssertObserved() => Assert.IsGreaterThan(0, Volatile.Read(ref _reads),
        "必须实际经过展示目录，不能以没有执行跨模块读取的场景证明事务隔离。");

    private sealed class ObservedDirectory(
        IHostUserDisplayDirectory inner, IDataTransactionState transaction, OrganizationAssignmentTransactionBoundaryProbe probe)
        : IHostUserDisplayDirectory
    {
        public Task<IReadOnlyDictionary<Guid, HostUserDirectoryEntry>> FindHostUsersAsync(
            IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
        {
            Assert.IsFalse(transaction.HasTransaction, "Identity 展示读取通过详情服务间接进入了 Organization 本地事务。");
            Interlocked.Increment(ref probe._reads);
            return inner.FindHostUsersAsync(userIds, cancellationToken);
        }
    }
}

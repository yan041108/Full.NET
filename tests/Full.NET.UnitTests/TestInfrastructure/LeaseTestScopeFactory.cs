using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.TestInfrastructure;

/// <summary>续租使用独立的租户上下文；存储替身复用同一权威内存行，避免测试跨 Scope 清空执行上下文。</summary>
internal sealed class LeaseTestScopeFactory(IActiveTenantContextResolver resolver, ICommandExecutor commands) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new Scope(resolver, commands);

    private sealed class Scope(IActiveTenantContextResolver resolver, ICommandExecutor commands) : IServiceScope, IServiceProvider
    {
        private readonly CurrentTenantAccessor _tenant = new();
        private readonly ICommandTransaction _transaction = new DapperCommandTransaction(new RecordingDbTransactionCoordinator());
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) => serviceType == typeof(IActiveTenantContextResolver) ? resolver
            : serviceType == typeof(ICommandExecutor) ? commands
            : serviceType == typeof(ICurrentTenantContextWriter) ? _tenant
            : serviceType == typeof(ICommandTransaction) ? _transaction : null;
        public void Dispose() => _tenant.Clear();
    }
}

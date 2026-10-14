using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.Data.Dapper;

/// <summary>独立授权作用域的有限标记；不共享父事务，不从请求参数或业务主体获取预算。</summary>
internal sealed class DatabaseAuthorizationReadScope : IDatabaseAuthorizationReadScope
{
    private int depth;
    private readonly int timeoutMilliseconds;
    public bool IsActive => depth > 0;
    public bool HasIndependentReadCapacity { get; }

    public DatabaseAuthorizationReadScope(IOptions<DatabaseCapacityOptions> capacity, IOptions<DatabaseOptions> database)
    {
        var settings = capacity.Value;
        timeoutMilliseconds = settings.AcquireTimeoutMilliseconds;
        HasIndependentReadCapacity = (!settings.Enabled || settings.AuthorizationReadReserve > 0)
            && (!DatabasePoolConfiguration.IsPoolingEnabled(database.Value) || DatabasePoolConfiguration.ReadMaximumPoolSize(database.Value) >= 2);
    }
    public IDisposable Enter()
    {
        if (!HasIndependentReadCapacity) throw new ServiceCapacityExceededException(ServiceCapacityFailureKind.Rejected,
            TimeSpan.FromMilliseconds(timeoutMilliseconds));
        depth++; return new Scope(this);
    }
    private sealed class Scope(DatabaseAuthorizationReadScope owner) : IDisposable
    {
        private DatabaseAuthorizationReadScope? value = owner;
        public void Dispose()
        { var current = Interlocked.Exchange(ref value, null); if (current is not null) current.depth--; }
    }
}

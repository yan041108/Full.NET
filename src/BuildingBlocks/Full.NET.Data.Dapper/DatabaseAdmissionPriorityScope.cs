using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 异步调用链上的关键优先级标记；嵌套 <see cref="EnterCritical"/> 时走 Worker 预留许可。
/// </summary>
internal sealed class DatabaseAdmissionPriorityScope
    : IDatabaseAdmissionPriorityScope
{
    private int _criticalDepth;

    internal bool IsCritical => _criticalDepth > 0;

    public IDisposable EnterCritical()
    {
        _criticalDepth++;
        return new CriticalScope(this);
    }

    private void ExitCritical()
    {
        if (_criticalDepth > 0)
        {
            _criticalDepth--;
        }
    }

    private sealed class CriticalScope(
        DatabaseAdmissionPriorityScope owner) : IDisposable
    {
        private DatabaseAdmissionPriorityScope? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.ExitCritical();
    }
}

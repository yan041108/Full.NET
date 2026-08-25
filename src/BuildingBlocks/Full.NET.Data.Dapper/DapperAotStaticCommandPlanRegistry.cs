using System.Collections.Concurrent;
using Full.NET.Data.Abstractions;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 保存启动期显式登记的固定命令形状；运行期只按稳定语句名与 Provider 选择工厂。
/// </summary>
internal static class DapperAotStaticCommandPlanRegistry
{
    private static readonly ConcurrentDictionary<string, StaticCommandPlan> Plans = new(
        StringComparer.Ordinal);

    internal static void Register(
        string statementName,
        IReadOnlyList<string> parameterNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statementName);
        ArgumentNullException.ThrowIfNull(parameterNames);
        var candidate = new StaticCommandPlan(parameterNames);
        var registered = Plans.GetOrAdd(statementName, candidate);
        if (!registered.HasShape(parameterNames))
        {
            throw new InvalidOperationException(
                $"Native AOT command plan '{statementName}' is already registered with a different parameter shape.");
        }
    }

    internal static bool TryGetFactory(
        string statementName,
        DatabaseProvider provider,
        out DapperAotCommandFactory factory)
    {
        if (Plans.TryGetValue(statementName, out var plan))
        {
            factory = plan.GetFactory(provider);
            return true;
        }

        factory = null!;
        return false;
    }

    private sealed class StaticCommandPlan
    {
        private readonly string[] _parameterNames;
        private readonly DapperAotCommandFactory _sqlServerFactory;
        private readonly DapperAotCommandFactory _mySqlFactory;

        public StaticCommandPlan(IReadOnlyList<string> parameterNames)
        {
            _parameterNames = parameterNames.ToArray();
            if (_parameterNames.Any(string.IsNullOrWhiteSpace))
            {
                throw new ArgumentException(
                    "Command parameter names must not be empty.",
                    nameof(parameterNames));
            }

            _sqlServerFactory = new DapperAotCommandFactory(_parameterNames);
            _mySqlFactory = new DapperAotCommandFactory(_parameterNames);
        }

        public bool HasShape(IReadOnlyList<string> parameterNames) =>
            _parameterNames.AsSpan().SequenceEqual(parameterNames.ToArray());

        public DapperAotCommandFactory GetFactory(DatabaseProvider provider) =>
            provider switch
            {
                DatabaseProvider.SqlServer => _sqlServerFactory,
                DatabaseProvider.MySql => _mySqlFactory,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(provider),
                    provider,
                    "Unsupported database provider."),
            };
    }
}

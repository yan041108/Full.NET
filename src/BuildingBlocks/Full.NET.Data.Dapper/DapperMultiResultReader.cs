using Full.NET.Data.Abstractions;
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 将 Dapper GridReader 限制在基础设施边界内，并显式拒绝并行消费。
/// </summary>
internal sealed class DapperMultiResultReader(SqlMapper.GridReader reader)
    : IMultiResultReader
{
    private int _reading;

    public async Task<T?> ReadSingleOrDefaultAsync<T>()
    {
        EnterRead();
        try
        {
            return await reader.ReadSingleOrDefaultAsync<T>().ConfigureAwait(false);
        }
        finally
        {
            ExitRead();
        }
    }

    public async Task<IReadOnlyList<T>> ReadAsync<T>()
    {
        EnterRead();
        try
        {
            var rows = await reader.ReadAsync<T>().ConfigureAwait(false);
            return rows.AsList();
        }
        finally
        {
            ExitRead();
        }
    }

    private void EnterRead()
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
        {
            throw new InvalidOperationException(
                "Multi-result sets must be consumed sequentially.");
        }
    }

    private void ExitRead() => Volatile.Write(ref _reading, 0);
}

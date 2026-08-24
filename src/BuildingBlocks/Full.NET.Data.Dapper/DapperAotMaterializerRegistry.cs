#if FULLNET_AOT_COMPILE
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 行物化注册表；弥补泛型执行器无法被 Dapper.AOT 拦截器闭包化的路径。
/// </summary>
public static class DapperAotMaterializerRegistry
{
    private static readonly Dictionary<Type, object> Readers = new();

    public static void Register<T>(Func<DbDataReader, T> readRow) =>
        Readers[typeof(T)] = readRow;

    public static bool TryGetReader<T>(out Func<DbDataReader, T> readRow)
    {
        if (Readers.TryGetValue(typeof(T), out var boxed))
        {
            readRow = (Func<DbDataReader, T>)boxed;
            return true;
        }

        readRow = null!;
        return false;
    }
}
#endif

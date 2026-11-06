#if FULLNET_AOT_COMPILE
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 行物化注册表；弥补泛型执行器无法被 Dapper.AOT 拦截器闭包化的路径。
/// </summary>
public static class DapperAotMaterializerRegistry
{
    private static readonly Dictionary<Type, object> Readers = new();

    /// <summary>登记指定行类型的物化读取器，覆盖同类型的既有登记。</summary>
    /// <typeparam name="T">需要物化的行类型。</typeparam>
    /// <param name="readRow">从数据读取器读取单行并物化为 <typeparamref name="T"/> 的委托。</param>
    public static void Register<T>(Func<DbDataReader, T> readRow) =>
        Readers[typeof(T)] = readRow;

    /// <summary>尝试获取指定行类型已登记的物化读取器，未登记时不抛出。</summary>
    /// <typeparam name="T">待解析的行类型。</typeparam>
    /// <param name="readRow">命中的读取器；未登记时为 <see langword="null"/>。</param>
    /// <returns>存在已登记读取器返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
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

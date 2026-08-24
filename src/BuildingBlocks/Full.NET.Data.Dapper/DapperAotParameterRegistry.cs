#if FULLNET_AOT_COMPILE
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 参数绑定注册表；避免 DynamicParameters 对匿名类型与 record 的反射展开。
/// </summary>
public static class DapperAotParameterRegistry
{
    private static readonly Dictionary<Type, Func<object, DynamicParameters>> Binders = new();

    public static void Register<T>(Func<T, DynamicParameters> bind) =>
        Binders[typeof(T)] = values => bind((T)values);

    public static bool TryBind(object values, out DynamicParameters parameters)
    {
        if (Binders.TryGetValue(values.GetType(), out var bind))
        {
            parameters = bind(values);
            return true;
        }

        parameters = null!;
        return false;
    }
}
#endif

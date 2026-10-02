#if FULLNET_AOT_COMPILE
using global::Dapper;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 参数绑定注册表；避免 DynamicParameters 对匿名类型与 record 的反射展开。
/// </summary>
public static class DapperAotParameterRegistry
{
    private static readonly Dictionary<Type, Func<object, DynamicParameters>> Binders = new();

    /// <summary>
    /// 注册类型 <typeparamref name="T"/> 到 <see cref="DynamicParameters"/> 的 AOT 绑定器；
    /// Native AOT 下替代 Dapper 对匿名类型与 record 的反射展开。
    /// </summary>
    /// <typeparam name="T">参数值的 CLR 类型。</typeparam>
    /// <param name="bind">将 <typeparamref name="T"/> 实例转换为 <see cref="DynamicParameters"/> 的工厂。</param>
    public static void Register<T>(Func<T, DynamicParameters> bind) =>
        Binders[typeof(T)] = values => bind((T)values);

    /// <summary>
    /// 尝试用已注册的 AOT 绑定器将参数对象转换为 <see cref="DynamicParameters"/>。
    /// </summary>
    /// <param name="values">待绑定的参数对象。</param>
    /// <param name="parameters">绑定成功时输出 Dapper 参数集合。</param>
    /// <returns>类型已注册返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
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

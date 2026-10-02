#if FULLNET_AOT_COMPILE
namespace Full.NET.Data.Dapper;

/// <summary>
/// 模块在启动时注册 Native AOT 行物化器。
/// </summary>
public interface IDapperAotMaterializerContributor
{
    /// <summary>
    /// 向注册器贡献本模块在 Native AOT 下可静态解析的行物化器。
    /// </summary>
    /// <param name="registrar">宿主提供的物化器注册器；调用方不得缓存或跨作用域复用。</param>
    void RegisterMaterializers(DapperAotMaterializerRegistrar registrar);
}

/// <summary>
/// 模块侧注册 <see cref="DapperAotMaterializerRegistry"/> 的薄封装。
/// </summary>
public sealed class DapperAotMaterializerRegistrar
{
    /// <summary>
    /// 注册指定结果类型 <typeparamref name="T"/> 的行物化委托。
    /// </summary>
    /// <typeparam name="T">物化目标类型。</typeparam>
    /// <param name="readRow">从 <see cref="System.Data.Common.DbDataReader"/> 当前行读取并构造 <typeparamref name="T"/> 实例的委托；不得为 null。</param>
    public void Register<T>(Func<System.Data.Common.DbDataReader, T> readRow) =>
        DapperAotMaterializerRegistry.Register(readRow);
}
#endif

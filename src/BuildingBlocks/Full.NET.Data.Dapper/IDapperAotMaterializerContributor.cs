#if FULLNET_AOT_COMPILE
namespace Full.NET.Data.Dapper;

/// <summary>
/// 模块在启动时注册 Native AOT 行物化器。
/// </summary>
public interface IDapperAotMaterializerContributor
{
    void RegisterMaterializers(DapperAotMaterializerRegistrar registrar);
}

/// <summary>
/// 模块侧注册 <see cref="DapperAotMaterializerRegistry"/> 的薄封装。
/// </summary>
public sealed class DapperAotMaterializerRegistrar
{
    public void Register<T>(Func<System.Data.Common.DbDataReader, T> readRow) =>
        DapperAotMaterializerRegistry.Register(readRow);
}
#endif

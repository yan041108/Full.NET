using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// 为当前数据访问路径创建尚未打开的数据库连接，供执行器在受控时机附加事务和生命周期管理。
/// </summary>
internal interface IDbConnectionFactory
{
    /// <summary>
    /// 创建一个新的底层连接实例。
    /// </summary>
    /// <returns>返回未打开的 <see cref="DbConnection"/>，调用方负责后续打开、释放与异常路径清理。</returns>
    DbConnection Create();
}

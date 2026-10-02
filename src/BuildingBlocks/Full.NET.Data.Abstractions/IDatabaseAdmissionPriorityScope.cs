namespace Full.NET.Data.Abstractions;

/// <summary>
/// 为必须在数据库压力下完成的 Worker 续租与终态写入声明关键准入范围。
/// </summary>
/// <remarks>
/// 业务模块不得使用此接口提高普通查询优先级；它只允许宿主可靠性边界消费部署时显式保留的连接配额。
/// </remarks>
public interface IDatabaseAdmissionPriorityScope
{
    /// <summary>进入可嵌套的关键数据库操作范围。</summary>
    /// <returns>
    /// 表示关键范围生命周期的 <see cref="IDisposable"/>；调用方负责在关键操作结束时释放以归还高优先级连接配额。
    /// 嵌套调用应支持多次进入并按逆序释放，未释放将持续占用部署时保留的配额。
    /// </returns>
    IDisposable EnterCritical();
}

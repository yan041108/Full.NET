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
    IDisposable EnterCritical();
}

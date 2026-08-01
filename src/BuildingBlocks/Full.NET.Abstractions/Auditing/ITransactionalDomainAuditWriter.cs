namespace Full.NET.Abstractions.Auditing;

/// <summary>
/// B0 域内同事务审计写入契约：把领域审计记录写入调用方当前所在的数据库事务。
/// </summary>
/// <remarks>
/// 调用方必须已经处于业务写入所在的同一 <c>ICommandTransaction</c> 范围内再调用本接口；
/// 实现不得自行开启新的顶层事务，也不得把审计写入转投 Outbox 或其他异步队列——
/// 审计记录必须与触发它的业务写入同提交、同回滚。写入失败必须向调用方抛出异常，
/// 以便外层事务感知失败并回滚，而不是被吞掉后静默丢失审计。
/// </remarks>
/// <typeparam name="TAuditWrite">具体模块定义的领域审计写入载荷类型。</typeparam>
public interface ITransactionalDomainAuditWriter<in TAuditWrite>
{
    /// <summary>
    /// 在当前事务中写入一条领域审计记录。
    /// </summary>
    /// <param name="auditWrite">领域审计写入载荷。</param>
    /// <param name="cancellationToken">用于取消数据库操作的令牌。</param>
    Task WriteAsync(TAuditWrite auditWrite, CancellationToken cancellationToken);
}

namespace Full.NET.Data.Abstractions;

/// <summary>只读事务状态，用于在不可回滚的外部操作前拒绝持有业务事务；不暴露连接或事务控制。</summary>
public interface IDataTransactionState
{
    /// <summary>当前作用域是否已经持有数据库事务。</summary>
    bool HasTransaction { get; }
}

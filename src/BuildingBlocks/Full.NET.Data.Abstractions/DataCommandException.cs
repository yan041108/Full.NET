namespace Full.NET.Data.Abstractions;

/// <summary>
/// 表示数据执行边界已经识别、可由业务用例安全处理的命令失败。
/// </summary>
public sealed class DataCommandException(
    DataCommandFailureKind kind,
    Exception innerException)
    : Exception("The data command failed.", innerException)
{
    /// <summary>
    /// 获取当前命令失败的稳定分类；业务层可据此决定幂等恢复、重试或直接抛出 409。
    /// </summary>
    public DataCommandFailureKind Kind { get; } = kind;
}

/// <summary>
/// 定义不暴露具体数据库 Provider 的稳定命令失败类别，用于 <see cref="DataCommandException"/>
/// 将底层 SqlException / MySqlException 映射为业务层可决策的枚举值。
/// </summary>
/// <remarks>
/// 新增失败类别时需同步更新所有 Provider 特定的异常转换器：
/// SqlServer 从 SqlException.Number 映射，MySql 从 MySqlException.ErrorCode / SqlState 映射。
/// 分类失败时保留原始异常作为 InnerException，避免丢失诊断信息。
/// </remarks>
public enum DataCommandFailureKind
{
    /// <summary>
    /// 唯一约束冲突：INSERT / UPDATE 目标行违反 UNIQUE INDEX（含 PRIMARY KEY）。
    /// </summary>
    /// <remarks>
    /// 业务层可据此安全触发幂等恢复逻辑：先按冲突键查现有行，若语义等价则返回成功，
    /// 否则抛出领域级冲突异常转化为 HTTP 409。禁止通过捕获通用 Exception 代替本分类。
    /// </remarks>
    UniqueConstraint = 1,
}

namespace Full.NET.Data.Abstractions;

/// <summary>
/// 表示数据执行边界已经识别、可由业务用例安全处理的命令失败。
/// </summary>
public sealed class DataCommandException(
    DataCommandFailureKind kind,
    Exception innerException)
    : Exception("The data command failed.", innerException)
{
    public DataCommandFailureKind Kind { get; } = kind;
}

/// <summary>
/// 定义不暴露具体数据库 Provider 的稳定命令失败类别。
/// </summary>
public enum DataCommandFailureKind
{
    UniqueConstraint = 1,
}

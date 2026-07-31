namespace Full.NET.Modules.Jobs.Execution;

/// <summary>表示处理器遇到可安全再次执行的瞬时失败。</summary>
public sealed class RetryableJobException : Exception
{
    public RetryableJobException(string message)
        : base(message)
    {
    }

    public RetryableJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

namespace Full.NET.Modules.Jobs.Execution;

/// <summary>表示处理器遇到可安全再次执行的瞬时失败。</summary>
public sealed class RetryableJobException : Exception
{
    /// <summary>以失败说明构造可重试异常。</summary>
    /// <param name="message">描述瞬时失败原因的说明。</param>
    public RetryableJobException(string message)
        : base(message)
    {
    }

    /// <summary>以失败说明和内部异常构造可重试异常，保留原始失败栈用于诊断。</summary>
    /// <param name="message">描述瞬时失败原因的说明。</param>
    /// <param name="innerException">触发本次瞬时失败的内部异常。</param>
    public RetryableJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

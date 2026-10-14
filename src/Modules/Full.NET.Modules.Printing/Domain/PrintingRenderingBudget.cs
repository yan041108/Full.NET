using Full.NET.Modules.Printing.Contracts;

namespace Full.NET.Modules.Printing.Domain;

/// <summary>仅统计 Schema 投影后的字段，布局输入上限不能替代替换后的输出预算。</summary>
internal sealed class PrintingRenderingBudget
{
    private int fields;
    private long bytes;

    /// <summary>在编码和返回绑定字段前计入原始键值占用。</summary>
    /// <param name="key">Schema 声明的字段键。</param>
    /// <param name="value">绑定原文；空值保持为空。</param>
    /// <param name="token">当前预览或请求取消令牌。</param>
    internal void AddField(string key, string? value, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        bytes += ((long)key.Length + (value?.Length ?? 0)) * sizeof(char);
        if (++fields > PrintingLayoutPolicy.MaxBindingFields || bytes > PrintingLayoutPolicy.MaxBindingTextBytes)
            throw new PrintingRenderingLimitException(PrintingErrorCodes.BindingFailed);
    }
}

/// <summary>确定性渲染边界异常只携带既有机器码，不携带业务字段或完整布局。</summary>
internal sealed class PrintingRenderingLimitException(string code)
    : IOException("Printing content exceeds the supported rendering limits.")
{
    /// <summary>供消费服务转换为既有标准校验结果的稳定机器码。</summary>
    internal string Code { get; } = code;
}

using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Reporting.Contracts;

/// <summary>把业务模块已按当前主体授权的安全投影渲染为工作簿，不读取业务表或扩大字段权限。</summary>
/// <remarks>调用方负责权限和快照真实性；实现统一限制行列、输入文本与输出大小并阻止公式注入。</remarks>
public interface IReportingWorkbookRenderer
{
    /// <summary>渲染已授权列和行；超限、取消或无效形状不得返回部分成功文件。</summary>
    /// <param name="columns">精确允许的列定义。</param>
    /// <param name="rows">仅含上述列的业务值。</param>
    /// <param name="cancellationToken">取消渲染令牌。</param>
    /// <returns>完整 XLSX 或受控失败结果。</returns>
    Result<byte[]> Render(IReadOnlyList<ReportingExecutionColumnDefinition> columns,
        IReadOnlyList<ReportingExecutionRow> rows, CancellationToken cancellationToken = default);
}

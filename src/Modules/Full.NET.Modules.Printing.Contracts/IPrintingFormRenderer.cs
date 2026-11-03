using Full.NET.Abstractions.Results;

namespace Full.NET.Modules.Printing.Contracts;

/// <summary>按应用端固定布局绑定已经授权的字段，不读取业务表或根据浏览器数据推导权限。</summary>
/// <remarks>字段值统一编码；HTML 仍须在客户端白名单净化和隔离帧中展示，不授权任意脚本、外链或布局。</remarks>
public interface IPrintingFormRenderer
{
    /// <summary>渲染服务端固定布局，超限或未知绑定失败关闭。</summary>
    /// <param name="layoutHtml">调用模块代码审查过的静态布局，不能来自本次浏览器请求。</param>
    /// <param name="fields">已获当前对象及字段权限的投影。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仍需在消费边界净化的 HTML 或受控失败。</returns>
    Result<string> Render(string layoutHtml, IReadOnlyDictionary<string, string?> fields,
        CancellationToken cancellationToken = default);
}

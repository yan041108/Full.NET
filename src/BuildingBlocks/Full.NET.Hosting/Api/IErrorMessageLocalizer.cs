using System.Globalization;
using Full.NET.Abstractions.Results;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 将稳定错误契约转换为指定语言的安全显示文本。
/// </summary>
public interface IErrorMessageLocalizer
{
    /// <summary>
    /// 本地化错误显示文本；资源或参数不完整时返回安全默认消息。
    /// </summary>
    /// <param name="error">包含稳定代码与安全回退文本的错误。</param>
    /// <param name="culture">目标 UI Culture。</param>
    /// <returns>可发送给客户端的显示文本。</returns>
    string Localize(Error error, CultureInfo culture);
}

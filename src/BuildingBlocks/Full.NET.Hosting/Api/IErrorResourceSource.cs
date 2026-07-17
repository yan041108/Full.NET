using System.Globalization;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 提供一个稳定错误码前缀对应的本地化资源模板。
/// </summary>
/// <remarks>
/// 模块通过该接口贡献自己的资源，Hosting 无需引用模块程序集。
/// 实现必须线程安全，且不得在请求热路径扫描程序集或读取磁盘文件。
/// </remarks>
public interface IErrorResourceSource
{
    /// <summary>
    /// 获取该来源负责的稳定错误码前缀。
    /// </summary>
    string Prefix { get; }

    /// <summary>
    /// 尝试取得指定文化下的显示模板。
    /// </summary>
    /// <param name="code">稳定错误码。</param>
    /// <param name="culture">已经规范化的目标 UI Culture。</param>
    /// <param name="template">成功时返回资源模板。</param>
    /// <returns>资源存在且内容非空时返回 <see langword="true"/>。</returns>
    bool TryGetTemplate(
        string code,
        CultureInfo culture,
        out string template);
}

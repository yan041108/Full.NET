using System.Globalization;
using System.Resources;
using Full.NET.Hosting.Api;

namespace Full.NET.Modules.Reporting.Resources;

/// <summary>持久化任务保存机器码，交付时按当前请求语言解释发布和导出错误。</summary>
internal sealed class ReportingPublicationErrorResourceSource() : ResourceManagerErrorResourceSource("reporting.", Manager)
{
    private static readonly ResourceManager Manager = new("Full.NET.Modules.Reporting.Resources.ReportingPublicationErrors", typeof(ReportingPublicationErrorResourceSource).Assembly);
    internal static string? Message(string? code, string? fallback) => code is null ? fallback : Manager.GetString(code, CultureInfo.CurrentUICulture) ?? fallback;
}

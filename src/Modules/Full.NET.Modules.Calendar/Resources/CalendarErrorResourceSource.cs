using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Calendar.Contracts;

namespace Full.NET.Modules.Calendar.Resources;

/// <summary>
/// 以 ResourceManager 方式提供 Calendar 错误码的多语言资源来源，
/// 错误码本身保持稳定机器契约，仅展示文本按 BCP 47 语言标签本地化。
/// </summary>
internal sealed class CalendarErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        CalendarErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Calendar.Resources.CalendarErrors",
            typeof(CalendarErrorResourceSource).Assembly));

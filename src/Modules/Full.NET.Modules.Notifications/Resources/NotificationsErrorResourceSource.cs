using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Resources;

/// <summary>
/// 以 ResourceManager 方式提供 Notifications 错误码的多语言资源来源，
/// 错误码本身保持稳定机器契约，仅展示文本按 BCP 47 语言标签本地化。
/// </summary>
internal sealed class NotificationsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        NotificationsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Notifications.Resources.NotificationsErrors",
            typeof(NotificationsErrorResourceSource).Assembly));

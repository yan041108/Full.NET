using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Notifications.Contracts;

namespace Full.NET.Modules.Notifications.Resources;

internal sealed class NotificationsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        NotificationsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Notifications.Resources.NotificationsErrors",
            typeof(NotificationsErrorResourceSource).Assembly));

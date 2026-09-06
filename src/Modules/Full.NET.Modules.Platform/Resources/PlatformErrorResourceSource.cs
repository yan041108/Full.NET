using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Platform.Contracts;

namespace Full.NET.Modules.Platform.Resources;

/// <summary>
/// 以 ResourceManager 方式提供 Platform 错误码的多语言资源来源。
/// </summary>
internal sealed class PlatformErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        PlatformErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Platform.Resources.PlatformErrors",
            typeof(PlatformErrorResourceSource).Assembly));

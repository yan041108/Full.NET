using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Regions.Contracts;

namespace Full.NET.Modules.Regions.Resources;

/// <summary>
/// 以 ResourceManager 方式提供行政区域错误码的多语言资源来源。
/// </summary>
internal sealed class RegionsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        RegionsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Regions.Resources.RegionsErrors",
            typeof(RegionsErrorResourceSource).Assembly));

using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Settings.Contracts;

/// <summary>Settings 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Settings.Resources;

internal sealed class SettingsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        SettingsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Settings.Resources.SettingsErrors",
            typeof(SettingsErrorResourceSource).Assembly));

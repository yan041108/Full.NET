using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.ObservabilityAdmin.Contracts;

/// <summary>ObservabilityAdmin 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.ObservabilityAdmin.Resources;

internal sealed class ObservabilityAdminErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        ObservabilityAdminErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.ObservabilityAdmin.Resources.ObservabilityAdminErrors",
            typeof(ObservabilityAdminErrorResourceSource).Assembly));

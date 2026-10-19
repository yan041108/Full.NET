using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;

/// <summary>Identity 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Identity.Resources;

internal sealed class IdentityErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        IdentityErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Identity.Resources.IdentityErrors",
            typeof(IdentityErrorResourceSource).Assembly));

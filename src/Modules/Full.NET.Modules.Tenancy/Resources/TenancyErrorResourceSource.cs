using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Tenancy.Contracts;

/// <summary>Tenancy 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Tenancy.Resources;

internal sealed class TenancyErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        TenancyErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Tenancy.Resources.TenancyErrors",
            typeof(TenancyErrorResourceSource).Assembly));

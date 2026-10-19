using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Organization.Contracts;

/// <summary>Organization 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Organization.Resources;

internal sealed class OrganizationErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        "organization.",
        new ResourceManager(
            "Full.NET.Modules.Organization.Resources.OrganizationErrors",
            typeof(OrganizationErrorResourceSource).Assembly));
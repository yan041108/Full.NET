using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Jobs.Contracts;

/// <summary>Jobs 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Jobs.Resources;

internal sealed class JobsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        JobsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Jobs.Resources.JobsErrors",
            typeof(JobsErrorResourceSource).Assembly));

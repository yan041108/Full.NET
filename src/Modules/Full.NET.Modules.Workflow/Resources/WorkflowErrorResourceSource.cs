using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Workflow.Contracts;

/// <summary>Workflow 模块 ProblemDetails 错误码的嵌入式资源映射。</summary>
namespace Full.NET.Modules.Workflow.Resources;

internal sealed class WorkflowErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        WorkflowErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Workflow.Resources.WorkflowErrors",
            typeof(WorkflowErrorResourceSource).Assembly));

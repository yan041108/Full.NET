using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.Modules.Workflow.Resources;

internal sealed class WorkflowErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        WorkflowErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Workflow.Resources.WorkflowErrors",
            typeof(WorkflowErrorResourceSource).Assembly));

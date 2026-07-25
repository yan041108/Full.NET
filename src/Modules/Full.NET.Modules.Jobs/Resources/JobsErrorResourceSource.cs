using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Jobs.Contracts;

namespace Full.NET.Modules.Jobs.Resources;

internal sealed class JobsErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        JobsErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Jobs.Resources.JobsErrors",
            typeof(JobsErrorResourceSource).Assembly));

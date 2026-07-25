using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Auditing.Contracts;

namespace Full.NET.Modules.Auditing.Resources;

internal sealed class AuditingErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        AuditingErrorCodes.Prefix,
        new ResourceManager(
            "Full.NET.Modules.Auditing.Resources.AuditingErrors",
            typeof(AuditingErrorResourceSource).Assembly));

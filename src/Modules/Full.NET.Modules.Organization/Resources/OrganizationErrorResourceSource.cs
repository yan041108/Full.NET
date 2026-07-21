using System.Resources;
using Full.NET.Hosting.Api;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Organization.Resources;

internal sealed class OrganizationErrorResourceSource()
    : ResourceManagerErrorResourceSource(
        "organization.",
        new ResourceManager(
            "Full.NET.Modules.Organization.Resources.OrganizationErrors",
            typeof(OrganizationErrorResourceSource).Assembly));
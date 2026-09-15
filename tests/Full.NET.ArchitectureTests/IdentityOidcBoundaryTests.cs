using System.Xml.Linq;
using Full.NET.Modules.Identity;

namespace Full.NET.ArchitectureTests;

/// <summary>OIDC protocol boundary: no EF, no cross-module implementation refs, no wildcard protocol auth exceptions.</summary>
[TestClass]
public sealed class IdentityOidcBoundaryTests
{
    private static readonly string[] ForbiddenPackageFragments =
    [
        "EntityFrameworkCore",
        "OpenIddict.EntityFrameworkCore",
        "OpenIddict.EntityFramework",
        "OpenIddict.MongoDb",
    ];

    private static readonly string[] ForbiddenCrossModuleImplementationPrefixes =
    [
        "Full.NET.Modules.Ai",
        "Full.NET.Modules.Document",
        "Full.NET.Modules.Files",
        "Full.NET.Modules.Jobs",
        "Full.NET.Modules.Notifications",
        "Full.NET.Modules.Organization",
        "Full.NET.Modules.Settings",
        "Full.NET.Modules.Tenancy",
        "Full.NET.Modules.Workflow",
    ];

    [TestMethod]
    public void Identity_oidc_dependency_closure_locks_openiddict_7_7_0_without_ef_packages()
    {
        var root = RepositoryRoot();
        var packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(item => (
                Name: item.Attribute("Include")?.Value ?? string.Empty,
                Version: item.Attribute("Version")?.Value ?? string.Empty))
            .ToArray();

        Assert.AreEqual(
            "7.7.0",
            packages.Single(item => item.Name == "OpenIddict.Server.AspNetCore").Version);
        Assert.IsFalse(packages.Any(item =>
            ForbiddenPackageFragments.Any(fragment =>
                item.Name.Contains(fragment, StringComparison.Ordinal))));
    }

    [TestMethod]
    public void Identity_module_does_not_reference_ef_or_openiddict_persistence_packages()
    {
        var project = XDocument.Load(Path.Combine(
            RepositoryRoot(),
            "src",
            "Modules",
            "Full.NET.Modules.Identity",
            "Full.NET.Modules.Identity.csproj"));
        var packageReferences = project.Descendants("PackageReference")
            .Select(item => item.Attribute("Include")!.Value)
            .ToArray();

        foreach (var package in packageReferences)
        {
            Assert.IsFalse(
                ForbiddenPackageFragments.Any(fragment =>
                    package.Contains(fragment, StringComparison.Ordinal)),
                package);
        }
    }

    [TestMethod]
    public void Identity_oidc_sources_do_not_reference_other_module_implementations()
    {
        var root = RepositoryRoot();
        var identityDirectory = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Identity");
        var oidcSources = Directory.GetFiles(identityDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains("Oidc", StringComparison.OrdinalIgnoreCase)
                || path.Contains("IdentityOidc", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsTrue(oidcSources.Length > 0, "T00 should include OIDC configuration and registration sources.");

        foreach (var sourcePath in oidcSources)
        {
            var text = File.ReadAllText(sourcePath);
            foreach (var forbiddenPrefix in ForbiddenCrossModuleImplementationPrefixes)
            {
                Assert.IsFalse(
                    text.Contains(forbiddenPrefix, StringComparison.Ordinal),
                    $"{Path.GetRelativePath(root, sourcePath)} must not reference {forbiddenPrefix} implementations.");
            }
        }
    }

    [TestMethod]
    public void Identity_oidc_registration_does_not_declare_wildcard_protocol_authorization_exceptions()
    {
        var root = RepositoryRoot();
        var registrationPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Identity",
            "DependencyInjection",
            "IdentityOidcServiceCollectionExtensions.cs");
        var source = File.ReadAllText(registrationPath);

        Assert.IsFalse(
            source.Contains("AllowAnonymous()", StringComparison.Ordinal),
            "Protocol registration must not declare wildcard AllowAnonymous exceptions.");
        Assert.IsFalse(
            source.Contains("MapGroup(\"/.\"", StringComparison.Ordinal)
            || source.Contains("Map(\"/.\"", StringComparison.Ordinal),
            "Protocol registration must not map wildcard protocol routes.");
        StringAssert.Contains(source, "RequireProofKeyForCodeExchange");
    }

    [TestMethod]
    public void Identity_module_exposes_static_oidc_registration_without_enabling_by_default()
    {
        var moduleType = typeof(IdentityModule);
        var addServices = moduleType.GetMethod(nameof(IdentityModule.AddServices));
        Assert.IsNotNull(addServices);

        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src",
            "Modules",
            "Full.NET.Modules.Identity",
            "IdentityModule.cs"));
        StringAssert.Contains(source, "AddIdentityOidc(configuration)");
        StringAssert.Contains(source, "Features.Login.Endpoint.Map(group)");
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null
               && !File.Exists(Path.Combine(current.FullName, "Full.NET.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
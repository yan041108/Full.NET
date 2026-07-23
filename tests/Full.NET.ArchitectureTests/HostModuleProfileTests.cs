using System.Xml.Linq;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class HostModuleProfileTests
{
    [TestMethod]
    public void Runtime_hosts_do_not_reference_business_module_projects_directly()
    {
        var root = FindRepositoryRoot();
        var runtimeHosts = new[]
        {
            "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj",
            "src/Hosts/Full.NET.Host.Worker/Full.NET.Host.Worker.csproj",
            "src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj",
        };

        foreach (var relativePath in runtimeHosts)
        {
            var projectPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var moduleReferences = XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                .Where(value => value.Contains("Full.NET.Modules.", StringComparison.Ordinal))
                .ToArray();

            Assert.HasCount(
                0,
                moduleReferences,
                $"{relativePath} 必须通过 Full.NET.Composition 装配模块，"
                    + $"禁止直接引用具体业务模块项目：{string.Join(", ", moduleReferences)}");
        }
    }

    [TestMethod]
    public void Hosts_select_modules_through_the_shared_explicit_profile_catalog()
    {
        var root = FindRepositoryRoot();
        var expectedProfiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/Hosts/Full.NET.Host.Api/Program.cs"] = "FullNetHostProfile.Api",
            ["src/Hosts/Full.NET.Host.Worker/Program.cs"] = "FullNetHostProfile.Worker",
            ["src/Hosts/Full.NET.Host.Migrator/Program.cs"] = "FullNetHostProfile.Migrator",
        };

        foreach (var (relativePath, profile) in expectedProfiles)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            StringAssert.Contains(source, "AddFullNetApplicationModules");
            StringAssert.Contains(source, profile);
            Assert.IsFalse(
                source.Contains("AddFullNetModule<", StringComparison.Ordinal),
                $"{relativePath} 不得绕过共享宿主 Profile 手工注册完整模块。");
            Assert.IsFalse(
                source.Contains("AddFullNetTenancyWorkerServices", StringComparison.Ordinal),
                $"{relativePath} 不得绕过共享宿主 Profile 手工注册 Worker 能力。");
        }
    }

    [TestMethod]
    public void Composition_catalog_uses_migration_registration_hook_for_migrator_profile()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs".Replace(
                '/',
                Path.DirectorySeparatorChar)));

        StringAssert.Contains(source, "case FullNetHostProfile.Migrator:");
        StringAssert.Contains(source, "module.AddMigrationServices(services, configuration);");
        Assert.IsFalse(
            source.Contains(
                "case FullNetHostProfile.Migrator:\r\n                services.AddFullNetModularity();\r\n                foreach (var module in CreateModules())\r\n                {\r\n                    services.AddFullNetModule(module, configuration);",
                StringComparison.Ordinal)
            || source.Contains(
                "case FullNetHostProfile.Migrator:\n                services.AddFullNetModularity();\n                foreach (var module in CreateModules())\n                {\n                    services.AddFullNetModule(module, configuration);",
                StringComparison.Ordinal),
            "Migrator Profile 只能通过最小迁移/Seed 注册入口装配模块，不能继续复用完整模块 AddServices。");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 Full.NET 仓库根目录。");
    }
}

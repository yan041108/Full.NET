namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class HostModuleProfileTests
{
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

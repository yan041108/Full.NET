using System.Reflection;
using Full.NET.Modules.Jobs;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class JobsSettingsBoundaryTests
{
    private const string SettingsImplementationAssembly = "Full.NET.Modules.Settings";
    private const string SettingsContractsAssembly = "Full.NET.Modules.Settings.Contracts";

    [TestMethod]
    public void Jobs_module_must_not_reference_Settings_implementation_assembly()
    {
        // 中文注释：NetArchTest 的 HaveDependencyOn 会把 Settings.Contracts 误判为 Settings 实现程序集，故用精确程序集名检查。
        var referenced = typeof(JobsModule).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.IsTrue(
            referenced.Contains(SettingsContractsAssembly),
            "Jobs must reference Settings.Contracts for secret resolution.");
        Assert.IsFalse(
            referenced.Contains(SettingsImplementationAssembly),
            "Jobs must not reference the Settings implementation assembly.");
    }

    [TestMethod]
    public void Jobs_module_may_reference_Settings_contracts()
    {
        var references = typeof(JobsModule).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        CollectionAssert.Contains(references, SettingsContractsAssembly);
        CollectionAssert.DoesNotContain(references, SettingsImplementationAssembly);
    }
}

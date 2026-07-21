using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy;
using NetArchTest.Rules;
using System.Reflection;
using System.Reflection.Emit;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class SeedingDependencyRulesTests
{
    private static readonly Assembly[] PublishedAssemblies =
    [
        typeof(IdentityModule).Assembly,
        typeof(LoginRequest).Assembly,
        typeof(TenancyModule).Assembly,
        Assembly.Load("Full.NET.Modules.Tenancy"),
        typeof(OrganizationModule).Assembly,
        typeof(OrganizationUnitResponse).Assembly,
        ProductionAssemblies.HostApi,
        ProductionAssemblies.HostWorker,
        ProductionAssemblies.HostMigrator,
    ];

    [TestMethod]
    public void Published_modules_and_hosts_do_not_contain_test_scenario_types_or_options()
    {
        AssertPublishedModuleCoverage();

        var forbiddenTokens = new[] { "E2e", "TestOnly" };
        var violations = PublishedAssemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => FindTestScenarioViolations(type, forbiddenTokens))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            violations,
            $"发布程序集包含测试场景类型或配置节:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static void AssertPublishedModuleCoverage()
    {
        string[] expectedModuleAssemblies =
        [
            "Full.NET.Modules.Identity",
            "Full.NET.Modules.Identity.Contracts",
            "Full.NET.Modules.Organization",
            "Full.NET.Modules.Organization.Contracts",
            "Full.NET.Modules.Tenancy",
            "Full.NET.Modules.Tenancy.Http",
        ];
        var actualModuleAssemblies = PublishedAssemblies
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name?.StartsWith("Full.NET.Modules.", StringComparison.Ordinal) == true)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            expectedModuleAssemblies,
            actualModuleAssemblies,
            "发布程序集扫描清单必须覆盖每个实际模块输出，新增或拆分项目时须显式纳入门禁。");
    }

    [TestMethod]
    public void Seeding_abstractions_do_not_depend_on_runtime_or_business_layers()
    {
        var result = Types.InAssembly(typeof(SeedProfile).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Dapper",
                "Full.NET.Data.Dapper",
                "Full.NET.Modules",
                "Full.NET.Host")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Seed 契约依赖违规: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void Seeding_dapper_does_not_depend_on_business_or_host_layers()
    {
        var result = Types.InAssembly(typeof(SeedCommandLine).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Full.NET.Modules",
                "Full.NET.Host",
                "Full.NET.Composition")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Seed Dapper 基础设施依赖违规: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    private static IEnumerable<string> FindTestScenarioViolations(
        Type type,
        IReadOnlyCollection<string> forbiddenTokens)
    {
        if (forbiddenTokens.Any(token =>
                type.FullName?.Contains(token, StringComparison.OrdinalIgnoreCase) == true))
        {
            yield return $"{type.Assembly.GetName().Name}:type:{type.FullName}";
        }

        var contributorName = ReadConstantContributorName(type);
        if (contributorName is not null && forbiddenTokens.Any(token =>
                contributorName.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            yield return $"{type.Assembly.GetName().Name}:contributor:{contributorName}";
        }

        foreach (var property in type.GetProperties(
                     BindingFlags.Instance |
                     BindingFlags.Static |
                     BindingFlags.Public |
                     BindingFlags.NonPublic))
        {
            if (forbiddenTokens.Any(token =>
                    property.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                yield return $"{type.Assembly.GetName().Name}:option:{type.FullName}.{property.Name}";
            }
        }
    }

    private static string? ReadConstantContributorName(Type type)
    {
        if (!typeof(IDataSeedContributor).IsAssignableFrom(type))
        {
            return null;
        }

        var getter = type.GetProperty(nameof(IDataSeedContributor.Name))?.GetMethod;
        var instructions = getter?.GetMethodBody()?.GetILAsByteArray();
        if (instructions is not { Length: >= 6 }
            || instructions[0] != unchecked((byte)OpCodes.Ldstr.Value))
        {
            return null;
        }

        var metadataToken = BitConverter.ToInt32(instructions, 1);
        return getter!.Module.ResolveString(metadataToken);
    }
}

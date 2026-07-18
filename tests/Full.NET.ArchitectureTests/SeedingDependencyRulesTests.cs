using Full.NET.Seeding.Abstractions;
using Full.NET.Seeding.Dapper;
using NetArchTest.Rules;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class SeedingDependencyRulesTests
{
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
}

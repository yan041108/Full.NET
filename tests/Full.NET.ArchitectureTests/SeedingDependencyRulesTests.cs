using Full.NET.Seeding.Abstractions;
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
}

using NetArchTest.Rules;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class DataAccessRulesTests
{
    [TestMethod]
    public void OnlyApprovedInfrastructureAssemblies_depend_on_Dapper_package_namespace()
    {
        var result = Types
            .InAssemblies(ProductionAssemblies.All
                .Where(assembly =>
                    assembly != ProductionAssemblies.DataDapper &&
                    assembly != ProductionAssemblies.SeedingDapper))
            .ShouldNot()
            .HaveDependencyOn("Dapper")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Dapper dependencies outside approved infrastructure: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}

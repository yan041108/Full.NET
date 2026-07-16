using NetArchTest.Rules;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class DataAccessRulesTests
{
    [TestMethod]
    public void OnlyDataDapper_DependsOnDapperPackageNamespace()
    {
        var result = Types
            .InAssemblies(ProductionAssemblies.All
                .Where(assembly => assembly != ProductionAssemblies.DataDapper))
            .ShouldNot()
            .HaveDependencyOn("Dapper")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Dapper dependencies outside Data.Dapper: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}

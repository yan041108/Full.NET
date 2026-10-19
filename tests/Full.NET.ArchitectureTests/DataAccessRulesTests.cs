using NetArchTest.Rules;

namespace Full.NET.ArchitectureTests;

/// <summary>架构测试：数据访问须走 Dapper 显式 SQL，禁止引入 EF Core 等未批准 ORM。</summary>
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

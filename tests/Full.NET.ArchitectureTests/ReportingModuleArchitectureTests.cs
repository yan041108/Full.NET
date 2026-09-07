using System.Xml.Linq;

namespace Full.NET.ArchitectureTests;

/// <summary>报表模块不得把具体数据库驱动带入业务程序集。</summary>
[TestClass]
public sealed class ReportingModuleArchitectureTests
{
    /// <summary>报表项目引用只能停留在 Full.NET 数据抽象与 Dapper 边界。</summary>
    [TestMethod]
    public void Reporting_module_does_not_reference_ado_net_provider_packages()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var projectPath = Path.Combine(
            root,
            "src",
            "Modules",
            "Full.NET.Modules.Reporting",
            "Full.NET.Modules.Reporting.csproj");
        var document = XDocument.Load(projectPath);
        var packages = document.Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        CollectionAssert.DoesNotContain(packages, "Microsoft.Data.SqlClient");
        CollectionAssert.DoesNotContain(packages, "MySqlConnector");
    }

    /// <summary>报表源码不得直接引用具体驱动类型。</summary>
    [TestMethod]
    public void Reporting_module_source_does_not_use_concrete_sql_drivers()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var moduleRoot = Path.Combine(root, "src", "Modules", "Full.NET.Modules.Reporting");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(moduleRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        Assert.IsFalse(
            source.Contains("Microsoft.Data.SqlClient", StringComparison.Ordinal),
            "Reporting 源码引用了 Microsoft.Data.SqlClient。");
        Assert.IsFalse(
            source.Contains("MySqlConnector", StringComparison.Ordinal),
            "Reporting 源码引用了 MySqlConnector。");
        Assert.IsFalse(
            source.Contains("new SqlConnection", StringComparison.Ordinal),
            "Reporting 源码直接构造 SqlConnection。");
        Assert.IsFalse(
            source.Contains("new MySqlConnection", StringComparison.Ordinal),
            "Reporting 源码直接构造 MySqlConnection。");
    }
}

using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class SchemaNameTests
{
    [TestMethod]
    public void CreateFramework_builds_fn_table_name()
    {
        var name = SchemaName.CreateFramework("identity", "user");

        Assert.AreEqual("fn_identity_user", name.Value);
    }

    [TestMethod]
    public void CreateProject_builds_frozen_owner_table_name()
    {
        var name = SchemaName.CreateProject("acme", "sales", "order_item");

        Assert.AreEqual("acme_sales_order_item", name.Value);
    }

    [TestMethod]
    public void CreateProject_rejects_framework_owner()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => SchemaName.CreateProject("fn", "identity", "user"));
    }

    [TestMethod]
    public void CreateProject_rejects_reserved_owner()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => SchemaName.CreateProject("sys", "identity", "user"));
    }

    [TestMethod]
    public void CreateFramework_rejects_invalid_module_or_entity()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => SchemaName.CreateFramework("Identity", "user"));
        Assert.ThrowsExactly<ArgumentException>(
            () => SchemaName.CreateFramework("identity", "User"));
    }

    [TestMethod]
    public void CreateProject_rejects_overlong_table_name_without_truncation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => SchemaName.CreateProject(
            "customerone",
            "notifications",
            new string('a', 50)));
    }
}

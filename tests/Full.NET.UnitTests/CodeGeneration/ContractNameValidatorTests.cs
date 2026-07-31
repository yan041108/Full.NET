using System.Text.Json;
using Full.NET.Data.CodeGeneration.Naming;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class ContractNameValidatorTests
{
    [TestMethod]
    public void IsValidColumn_accepts_pascal_case()
    {
        Assert.IsTrue(ContractNameValidator.IsValidColumn("CreatedAtUtc"));
        Assert.IsFalse(ContractNameValidator.IsValidColumn("created_at_utc"));
    }

    [TestMethod]
    public void IsValidPermission_enforces_three_lower_snake_segments()
    {
        Assert.IsTrue(ContractNameValidator.IsValidPermission(
            "identity.super_administrators.read"));
        Assert.IsFalse(ContractNameValidator.IsValidPermission(
            "identity.super-administrators.read"));
    }

    [TestMethod]
    public void IsValidError_enforces_area_and_reason_segments()
    {
        Assert.IsTrue(ContractNameValidator.IsValidError(
            "identity.session.not_active"));
        Assert.IsFalse(ContractNameValidator.IsValidError(
            "identity.session_not_active"));
    }

    [TestMethod]
    public void IsValidMessage_excludes_schema_version_from_type()
    {
        Assert.IsTrue(ContractNameValidator.IsValidMessage(
            "fullnet.tenancy.tenant.provisioned"));
        Assert.IsFalse(ContractNameValidator.IsValidMessage(
            "fullnet.tenancy.tenant.provisioned.v2"));
    }

    [TestMethod]
    public void IsValidStatement_enforces_dot_separated_lower_snake_segments()
    {
        Assert.IsTrue(ContractNameValidator.IsValidStatement(
            "identity.user.find_by_id"));
        Assert.IsFalse(ContractNameValidator.IsValidStatement(
            "identity.find-user-by-id"));
    }

    [TestMethod]
    public void IsValidDotNetType_and_http_path_use_the_shared_profile()
    {
        Assert.IsTrue(ContractNameValidator.IsValidDotNetType("ProductItem"));
        Assert.IsFalse(ContractNameValidator.IsValidDotNetType("productItem"));
        Assert.IsTrue(ContractNameValidator.IsValidHttpPathSegment("product-items"));
        Assert.IsFalse(ContractNameValidator.IsValidHttpPathSegment("ProductItems"));
    }

    [TestMethod]
    public void Embedded_profile_matches_shared_examples()
    {
        using var examples = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "contracts/naming/examples.json")));
        foreach (var item in examples.RootElement.GetProperty("databaseObjects").EnumerateArray())
        {
            Assert.AreEqual(
                item.GetProperty("output").GetString(),
                DatabaseObjectNameBuilder.Build(item.GetProperty("input").GetString()!));
        }

        AssertContractExamples(examples, "columns", ContractNameValidator.IsValidColumn);
        AssertContractExamples(examples, "permissions", ContractNameValidator.IsValidPermission);
        AssertContractExamples(examples, "errors", ContractNameValidator.IsValidError);
        AssertContractExamples(examples, "messages", ContractNameValidator.IsValidMessage);
        AssertContractExamples(examples, "statements", ContractNameValidator.IsValidStatement);
    }

    private static void AssertContractExamples(
        JsonDocument document,
        string name,
        Func<string, bool> validator)
    {
        foreach (var item in document.RootElement
            .GetProperty("contracts")
            .GetProperty(name)
            .EnumerateArray())
        {
            Assert.AreEqual(
                item.GetProperty("valid").GetBoolean(),
                validator(item.GetProperty("value").GetString()!));
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

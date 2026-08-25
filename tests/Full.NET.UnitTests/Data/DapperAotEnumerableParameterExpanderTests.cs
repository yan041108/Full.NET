using Full.NET.Data.Dapper;
using global::Dapper;

namespace Full.NET.UnitTests.Data;

/// <summary>
/// 验证 Native AOT 集合参数展开与 Dapper <c>IN @Ids</c> 语义对齐。
/// </summary>
[TestClass]
public sealed class DapperAotEnumerableParameterExpanderTests
{
    [TestMethod]
    public void Expand_rewrites_in_clause_and_scalarizes_guid_array()
    {
        var id0 = Guid.Parse("01900000-0000-7000-8000-000000000001");
        var id1 = Guid.Parse("01900000-0000-7000-8000-000000000002");
        var parameters = new DynamicParameters();
        parameters.Add("Ids", new[] { id0, id1 });
        parameters.Add("LeaseId", Guid.Parse("01900000-0000-7000-8000-000000000099"));

        var (sql, expanded) = DapperAotEnumerableParameterExpander.Expand(
            "UPDATE fn_jobs_execution SET Status = @Running WHERE Id IN @Ids AND LeaseId = @LeaseId",
            parameters);

        Assert.AreEqual(
            "UPDATE fn_jobs_execution SET Status = @Running WHERE Id IN (@Ids0,@Ids1) AND LeaseId = @LeaseId",
            sql);
        CollectionAssert.AreEquivalent(
            new[] { "Ids0", "Ids1", "LeaseId" },
            expanded.ParameterNames.ToArray());
        Assert.AreEqual(id0, expanded.Get<Guid>("Ids0"));
        Assert.AreEqual(id1, expanded.Get<Guid>("Ids1"));
        Assert.AreEqual(
            Guid.Parse("01900000-0000-7000-8000-000000000099"),
            expanded.Get<Guid>("LeaseId"));
    }

    [TestMethod]
    public void Expand_empty_collection_uses_false_subquery()
    {
        var parameters = new DynamicParameters();
        parameters.Add("Ids", Array.Empty<Guid>());

        var (sql, expanded) = DapperAotEnumerableParameterExpander.Expand(
            "SELECT Id FROM fn_jobs_definition WHERE Id IN @Ids",
            parameters);

        Assert.AreEqual(
            "SELECT Id FROM fn_jobs_definition WHERE Id IN (SELECT NULL WHERE 1 = 0)",
            sql);
        Assert.AreEqual(0, expanded.ParameterNames.Count());
    }

    [TestMethod]
    public void Expand_does_not_treat_string_or_byte_array_as_collection()
    {
        var parameters = new DynamicParameters();
        parameters.Add("Name", "host");
        parameters.Add("Payload", new byte[] { 1, 2, 3 });

        var (sql, expanded) = DapperAotEnumerableParameterExpander.Expand(
            "SELECT 1 WHERE Name = @Name AND Payload = @Payload",
            parameters);

        Assert.AreEqual("SELECT 1 WHERE Name = @Name AND Payload = @Payload", sql);
        Assert.AreEqual("host", expanded.Get<string>("Name"));
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3 },
            expanded.Get<byte[]>("Payload"));
    }
}

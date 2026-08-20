using Full.NET.Modules.SerialNumbers.Persistence;

namespace Full.NET.UnitTests.SerialNumbers;

/// <summary>验收列表排序白名单与稳定次键，避免动态 ORDER BY 注入。</summary>
[TestClass]
public sealed class SerialNumberRuleListOrderByTests
{
    [TestMethod]
    public void ResolveRuleListOrderBy_uses_allowlisted_columns_and_stable_id()
    {
        Assert.AreEqual(
            "DisplayOrder ASC, Id ASC",
            SerialNumberSql.ResolveRuleListOrderBy(null, null));
        Assert.AreEqual(
            "RuleKey DESC, Id ASC",
            SerialNumberSql.ResolveRuleListOrderBy("ruleKey", "desc"));
        Assert.AreEqual(
            "DisplayName ASC, Id ASC",
            SerialNumberSql.ResolveRuleListOrderBy("name", "asc"));
        Assert.AreEqual(
            "IsEnabled DESC, Id ASC",
            SerialNumberSql.ResolveRuleListOrderBy("status", "DESC"));
        Assert.AreEqual(
            "DisplayOrder ASC, Id ASC",
            SerialNumberSql.ResolveRuleListOrderBy("drop table", "asc"));
    }
}

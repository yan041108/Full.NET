using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

/// <summary>人工组织文本有明确边界，规范化不会改变授权身份或借标签生成机器权限。</summary>
[TestClass]
public sealed class AiKnowledgeDirectoryPolicyTests
{
    [TestMethod]
    public void Tags_canonicalize_full_width_case_and_duplicates_with_stable_order()
        => CollectionAssert.AreEqual(new[] { "a", "中文" }, AiKnowledgeDirectoryPolicy.Tags(["Ａ", "a", " 中文 "])!);
    [TestMethod]
    [DataRow("")] [DataRow(" ")] [DataRow("x\ny")] [DataRow("x\u0000y")]
    public void Empty_or_control_text_is_rejected(string input)
    { Assert.IsNull(AiKnowledgeDirectoryPolicy.Tags([input])); Assert.IsNull(AiKnowledgeDirectoryPolicy.Name(input)); }
    [TestMethod]
    public void Limits_reject_instead_of_silently_truncating()
    {
        Assert.IsNull(AiKnowledgeDirectoryPolicy.Tags(Enumerable.Range(0, 11).Select(value => value.ToString()).ToArray()));
        Assert.IsNull(AiKnowledgeDirectoryPolicy.Tags([new string('中', 65)]));
        Assert.IsNull(AiKnowledgeDirectoryPolicy.Name(new string('中', 101)));
        Assert.HasCount(0, AiKnowledgeDirectoryPolicy.Tags([])!);
        Assert.IsNotNull(AiKnowledgeDirectoryPolicy.Tags(Enumerable.Range(0, 10).Select(value => new string('中', 63) + value).ToArray()));
    }
    [TestMethod]
    public void Malformed_unicode_is_rejected_before_database_serialization()
    { Assert.IsNull(AiKnowledgeDirectoryPolicy.Tags(["\ud800"])); Assert.IsNull(AiKnowledgeDirectoryPolicy.Name("\ud800")); }
}

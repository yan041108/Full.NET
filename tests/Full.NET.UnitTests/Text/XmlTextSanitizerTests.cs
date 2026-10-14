using Full.NET.Abstractions.Text;

namespace Full.NET.UnitTests.Text;

/// <summary>合法补充平面字符不能按独立 UTF-16 单元删除；输出仍须能进入 XML 文本节点。</summary>
[TestClass]
public sealed class XmlTextSanitizerTests
{
    [TestMethod]
    [DataRow("中文 😀 𠮷 \t\r\n")]
    [DataRow("\U00010000\U0010FFFF")]
    [DataRow("")]
    public void Valid_text_and_complete_surrogate_pairs_keep_original_instance(string text) =>
        Assert.AreSame(text, XmlTextSanitizer.Sanitize(text));

    [TestMethod]
    [DataRow("a\u0000b\u0001c", "abc")]
    [DataRow("\uFFFF\uFFFE😀𠮷", "😀𠮷")]
    public void Invalid_controls_and_isolated_surrogates_are_removed(string value, string expected) =>
        Assert.AreEqual(expected, XmlTextSanitizer.Sanitize(value));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Malformed_surrogates_are_created_in_method_body_to_avoid_attribute_encoding(bool overlap)
    {
        // 特性字符串经过元数据 UTF-8 编码，不能用它运输需要验证的孤立代理项。
        var value = overlap ? "\uD800\uD83D\uDE00\uDC00" : "a\uD800b\uDC00c";
        Assert.AreEqual(overlap ? "😀" : "abc", XmlTextSanitizer.Sanitize(value));
    }

    [TestMethod]
    public void Cancelled_empty_text_still_propagates_cancellation() =>
        Assert.ThrowsExactly<OperationCanceledException>(() => XmlTextSanitizer.Sanitize("", new(true)));

    [TestMethod]
    public void Null_is_not_silently_coerced_to_empty_text() =>
        Assert.ThrowsExactly<ArgumentNullException>(() => XmlTextSanitizer.Sanitize(null!));

    [TestMethod]
    public void Valid_pairs_at_scan_boundaries_are_preserved()
    {
        var value = new string('x', 1023) + "😀" + new string('x', 1023) + "𠮷\0end";
        Assert.AreEqual(value.Replace("\0", "", StringComparison.Ordinal), XmlTextSanitizer.Sanitize(value));
    }
}

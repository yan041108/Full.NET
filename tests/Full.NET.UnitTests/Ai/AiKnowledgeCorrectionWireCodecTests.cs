using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>线格式拒绝伪造来源、重复字段及半份结构；不代表数据库或 Native 运行验收。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionWireCodecTests
{
    private const string Runtime = "managed:utf8-text-v1";

    [TestMethod]
    public void Round_trip_retains_correction_supplement_exclusion_and_original_positions()
    {
        var original = Create("原始识别结果"); var block = original.Blocks[0];
        var corrected = original.Replace(block.Id, block.Parts[0].Id, "人工校对正文", default)
            .Append(block.Id, "人工补写", Guid.CreateVersion7, default).Exclude(block.Id, true, default);
        var encoded = AiKnowledgeCorrectionWireCodec.Encode(corrected, Runtime, default);
        var restored = AiKnowledgeCorrectionWireCodec.Decode(encoded, original.Source, Runtime, default);
        Assert.IsNotNull(restored); Assert.IsTrue(restored.Blocks[0].Excluded);
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Corrected, restored.Blocks[0].Parts[0].Origin);
        Assert.AreEqual(original.Source.Blocks[0].Position, restored.Locate(restored.Blocks[0].Parts[0]));
        Assert.AreEqual(AiKnowledgeCorrectionOrigin.Supplement, restored.Blocks[1].Parts[0].Origin);
        Assert.IsNull(restored.Locate(restored.Blocks[1].Parts[0]));
        CollectionAssert.AreEqual(encoded, AiKnowledgeCorrectionWireCodec.Encode(restored, Runtime, default));
    }
    [TestMethod]
    public void Source_and_parser_runtime_drift_cannot_reuse_the_same_revision()
    {
        var original = Create("相同大小的原文"); var different = Create("相同大小的改文");
        var encoded = AiKnowledgeCorrectionWireCodec.Encode(original, Runtime, default);
        Assert.IsNull(AiKnowledgeCorrectionWireCodec.Decode(encoded, different.Source, Runtime, default));
        // 原始文件摘要、运行时与坐标均相同，重新识别的正文变化也必须改变解析身份。
        var changedParse = AiKnowledgeParsedDocument.FromBlocks(original.Source.Format, original.Source.ParserVersion,
            original.Source.ContentHash, original.Source.SizeBytes,
            [original.Source.Blocks[0] with { Text = "相同大小的改文" }], default, original.Source.Policy);
        Assert.IsNotNull(changedParse);
        Assert.IsNull(AiKnowledgeCorrectionWireCodec.Decode(encoded, changedParse, Runtime, default));
        Assert.IsNull(AiKnowledgeCorrectionWireCodec.Decode(encoded, original.Source, "managed:utf8-text-v2", default));
    }
    [TestMethod]
    public void Missing_duplicate_unknown_and_invalid_protocol_fields_fail_closed()
    {
        var original = Create("来源正文"); var bytes = AiKnowledgeCorrectionWireCodec.Encode(original, Runtime, default);
        var json = Encoding.UTF8.GetString(bytes);
        foreach (var invalid in new[]
        {
            json.Replace("\"excluded\":false,", "", StringComparison.Ordinal),
            json.Replace("\"excluded\":false", "\"excluded\":false,\"excluded\":true", StringComparison.Ordinal),
            json.Replace("\"excluded\":false", "\"excluded\":false,\"approved\":true", StringComparison.Ordinal),
            json.Replace("\"protocolVersion\":1", "\"protocolVersion\":2", StringComparison.Ordinal),
            json.Replace("\"origin\":0", "\"origin\":99", StringComparison.Ordinal),
            json.Replace("\"anchors\":[", "\"anchors\":[null,", StringComparison.Ordinal)
        })
        {
            Assert.AreNotEqual(json, invalid, "变异必须确实改变持久快照。");
            Assert.IsNull(AiKnowledgeCorrectionWireCodec.Decode(Encoding.UTF8.GetBytes(invalid), original.Source, Runtime, default));
        }
    }
    [TestMethod]
    public void Budgets_and_cancellation_are_checked_before_releasing_a_snapshot()
    {
        var original = Create("正文");
        Assert.IsNull(AiKnowledgeCorrectionWireCodec.Decode(new byte[AiKnowledgeCorrectionWireCodec.MaxWireBytes + 1], original.Source, Runtime, default));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeCorrectionWireCodec.Encode(original, Runtime, cancellation.Token));
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCorrectionWireCodec.SourceHash(original.Source, "invalid\ud800", default));
    }
    private static AiKnowledgeCorrectionDocument Create(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var parsed = AiKnowledgeParsedDocument.FromText(new(text, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length), AiKnowledgeTextFormat.PlainText, default);
        Assert.IsNotNull(parsed); return AiKnowledgeCorrectionDocument.Create(parsed, Guid.CreateVersion7, default);
    }
}

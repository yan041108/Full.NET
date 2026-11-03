using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>持久行必须同时匹配原解析、文件元数据和修订身份，不能只相信数据库摘要字段。</summary>
[TestClass]
public sealed class AiKnowledgeCorrectionSnapshotTests
{
    private const string Runtime = "managed:utf8-text-v1";
    [TestMethod]
    public void Source_requires_authoritative_metadata_runtime_and_complete_payload()
    {
        var (document, source, _) = Create();
        AiKnowledgeCorrectionSnapshot? Verify(AiKnowledgeCorrectionSourceRecord row) => AiKnowledgeCorrectionSnapshot.Verify(row, null,
            document.Source.Format, document.Source.ContentHash, document.Source.SizeBytes, document.Source.Policy, default);
        var verified = Verify(source); Assert.IsNotNull(verified); Assert.IsNull(verified.Identity.RevisionId); Assert.AreEqual(0, verified.Identity.HeadVersion);
        foreach (var invalid in new[]
        {
            source with { Id = Guid.Empty }, source with { FileHash = new string('a', 64) },
            source with { ParserRuntimeId = "invalid runtime" }, source with { ParserRuntimeId = "managed:utf8-text-v2" },
            source with { SourceHash = new string('0', 64) }, source with { OriginalPayload = "{}"u8.ToArray() },
            source with { InitialPayload = "{}"u8.ToArray() }, source with { InitialPayloadHash = new string('0', 64) }
        }) Assert.IsNull(Verify(invalid));
        Assert.IsNull(AiKnowledgeCorrectionSnapshot.Verify(source, null, document.Source.Format, document.Source.ContentHash,
            document.Source.SizeBytes + 1, document.Source.Policy, default));
        Assert.IsNull(AiKnowledgeCorrectionSnapshot.Verify(source, null, AiKnowledgeParsedFormat.Markdown, document.Source.ContentHash,
            document.Source.SizeBytes, AiKnowledgeParsePolicy.Default(AiKnowledgeParsedFormat.Markdown), default));
    }
    [TestMethod]
    public void Revision_cannot_switch_source_or_fall_back_when_damaged_and_cancellation_propagates()
    {
        var (document, source, revision) = Create();
        AiKnowledgeCorrectionSnapshot? Verify(AiKnowledgeCorrectionRevisionRecord row, CancellationToken token = default) =>
            AiKnowledgeCorrectionSnapshot.Verify(source, row, document.Source.Format, document.Source.ContentHash,
                document.Source.SizeBytes, document.Source.Policy, token);
        var verified = Verify(revision); Assert.IsNotNull(verified); Assert.AreEqual(revision.Id, verified.Identity.RevisionId);
        Assert.AreEqual("人工校对", verified.Document.Blocks[0].Parts[0].Text);
        foreach (var invalid in new[]
        {
            revision with { VersionId = Guid.CreateVersion7() }, revision with { SourceId = Guid.CreateVersion7() },
            revision with { Id = Guid.Empty }, revision with { ParentId = Guid.Empty }, revision with { HeadVersion = 0 },
            revision with { DocumentVersion = 1 }, revision with { PayloadHash = new string('0', 64) },
            revision with { Payload = "{}"u8.ToArray(), PayloadHash = Hash("{}"u8.ToArray()) }
        }) Assert.IsNull(Verify(invalid));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => Verify(revision, canceled.Token));
    }
    private static (AiKnowledgeCorrectionDocument Document, AiKnowledgeCorrectionSourceRecord Source, AiKnowledgeCorrectionRevisionRecord Revision) Create()
    {
        const string text = "原始识别"; var bytes = Encoding.UTF8.GetBytes(text);
        var parsed = AiKnowledgeParsedDocument.FromText(new(text, Hash(bytes), bytes.Length), AiKnowledgeTextFormat.PlainText, default); Assert.IsNotNull(parsed);
        var document = AiKnowledgeCorrectionDocument.Create(parsed, Guid.CreateVersion7, default);
        var initial = AiKnowledgeCorrectionWireCodec.Encode(document, Runtime, default);
        var source = new AiKnowledgeCorrectionSourceRecord(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Hash(bytes), parsed.ContentHash,
            Runtime, AiKnowledgeCorrectionWireCodec.SourceHash(parsed, Runtime, default), AiKnowledgeParsedWireCodec.Encode(parsed, default), initial,
            Hash(initial), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        var corrected = document.Replace(document.Blocks[0].Id, document.Blocks[0].Parts[0].Id, "人工校对", default);
        var payload = AiKnowledgeCorrectionWireCodec.Encode(corrected, Runtime, default);
        return (document, source, new(Guid.CreateVersion7(), source.VersionId, source.Id, null, Guid.CreateVersion7(), Hash(bytes),
            payload, Hash(payload), "修正识别", source.ActorUserId, source.CreatedAtUtc, 2, 1));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>落库协议必须拒绝长度炸弹、非规范身份和离开完整原文的载荷。</summary>
[TestClass]
public sealed class AiKnowledgeIndexDocxCodecBoundaryTests
{
    [TestMethod]
    [DataRow(false)][DataRow(true)]
    public void Protocol_identifiers_use_canonical_network_byte_order(bool vector)
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan();
        var bytes = vector ? AiKnowledgeIndexDocxVectorCodec.Encode(plan, 0, AiKnowledgeIndexDocxCodecTests.Vectors(16), default)
            : AiKnowledgeIndexDocxManifestCodec.Encode(plan, default);
        var format = vector ? AiKnowledgeIndexDocxVectorCodec.FormatKey : AiKnowledgeIndexDocxManifestCodec.FormatKey;
        var offset = 8 + Encoding.UTF8.GetByteCount(format);
        CollectionAssert.AreEqual(Convert.FromHexString("019a2ba0123470008000000000000001"), bytes.AsSpan(offset, 16).ToArray());
    }

    [TestMethod]
    [DataRow(false, "empty")][DataRow(true, "empty")]
    [DataRow(false, "short")][DataRow(true, "short")]
    [DataRow(false, "oversize")][DataRow(true, "oversize")]
    [DataRow(false, "tail")][DataRow(true, "tail")]
    [DataRow(false, "truncated")][DataRow(true, "truncated")]
    [DataRow(false, "magic")][DataRow(true, "magic")]
    [DataRow(false, "length")][DataRow(true, "length")]
    [DataRow(false, "utf8")][DataRow(true, "utf8")]
    public void Malformed_wire_is_rejected(bool vector, string fault)
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan();
        var bytes = vector ? AiKnowledgeIndexDocxVectorCodec.Encode(plan, 0, AiKnowledgeIndexDocxCodecTests.Vectors(16), default)
            : AiKnowledgeIndexDocxManifestCodec.Encode(plan, default);
        switch (fault)
        {
            case "empty": bytes = []; break;
            case "short": bytes = new byte[7]; break;
            case "oversize": bytes = new byte[(vector ? AiKnowledgeIndexDocxVectorCodec.MaxPayloadBytes : AiKnowledgeIndexDocxManifestCodec.MaxPayloadBytes) + 1]; break;
            case "tail": bytes = [.. bytes, 0]; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "magic": bytes[0] ^= 1; break;
            case "length": BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 4); break;
            case "utf8": bytes[8] = 0xff; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() =>
        {
            if (vector) AiKnowledgeIndexDocxVectorCodec.Decode(bytes, default);
            else AiKnowledgeIndexDocxManifestCodec.Decode(bytes, default);
        });
    }

    [TestMethod]
    public void Manifest_rejects_noncanonical_source_digest()
    {
        var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(AiKnowledgeIndexDocxCodecTests.Plan(), default);
        var offset = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes(new string('a', 64)));
        Assert.IsGreaterThan(0, offset); bytes[offset] = (byte)'A';
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexDocxManifestCodec.Decode(bytes, default));
    }

    [TestMethod]
    [DataRow("count")][DataRow("dimensions")][DataRow("zero")][DataRow("nan")][DataRow("infinity")][DataRow("ordinal")]
    public void Invalid_vectors_cannot_be_encoded(string fault)
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan(); var vectors = AiKnowledgeIndexDocxCodecTests.Vectors(fault == "count" ? 15 : 16, fault == "dimensions" ? 3 : 1536);
        if (fault is "zero" or "nan" or "infinity")
        { var v = new float[1536]; v[0] = fault == "nan" ? float.NaN : fault == "infinity" ? float.PositiveInfinity : 0; vectors[0] = v; }
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexDocxVectorCodec.Encode(plan, fault == "ordinal" ? 2 : 0, vectors, default));
    }

    [TestMethod]
    [DataRow("text")][DataRow("tenant")][DataRow("ordinal")]
    public void Valid_batch_cannot_be_rebound_to_another_manifest(string fault)
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan();
        var bytes = AiKnowledgeIndexDocxVectorCodec.Encode(plan, 0, AiKnowledgeIndexDocxCodecTests.Vectors(16), default);
        var other = AiKnowledgeIndexDocxCodecTests.Plan(tenant: fault == "tenant" ? Guid.CreateVersion7() : null, altered: fault == "text");
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeIndexDocxVectorCodec.ReadBatch(AiKnowledgeIndexDocxManifestCodec.Encode(other, default), fault == "ordinal" ? 1 : 0, bytes, default));
    }

    [TestMethod]
    public void Snapshot_freezes_caller_bytes_and_verifies_actual_payload_hash()
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan(); var vectors = AiKnowledgeIndexDocxCodecTests.Vectors(1);
        var bytes = AiKnowledgeIndexDocxVectorCodec.Encode(plan, 1, vectors, default); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var restored = AiKnowledgeIndexDocxVectorCodec.ReadBatch(AiKnowledgeIndexDocxManifestCodec.Encode(plan, default), 1, bytes, default);
        Array.Fill(bytes, (byte)0);
        Assert.AreEqual(hash, restored.PayloadHash); Assert.AreEqual(plan.Batches[1].Chunks[0], restored.Payload.Chunks[0].Source);
        Assert.AreEqual(1f, restored.Payload.Chunks[0].Vector.Span[0]);
    }

    [TestMethod]
    public void Cancelled_codecs_do_not_convert_cancellation_into_invalid_data()
    {
        var plan = AiKnowledgeIndexDocxCodecTests.Plan(); var bytes = AiKnowledgeIndexDocxManifestCodec.Encode(plan, default);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexDocxManifestCodec.Decode(bytes, cancellation.Token));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeIndexDocxVectorCodec.Encode(plan, 0, AiKnowledgeIndexDocxCodecTests.Vectors(16), cancellation.Token));
    }

    [TestMethod]
    public async Task Real_parser_output_preserves_full_manifest_and_table_coordinates_in_vectors()
    {
        byte[] bytes; byte[] wire;
        if (Environment.GetEnvironmentVariable("FULLNET_DOCX_NATIVE_FIXTURE_DIRECTORY") is { Length: > 0 } directory)
        { bytes = File.ReadAllBytes(Path.Combine(directory, "source.docx")); wire = File.ReadAllBytes(Path.Combine(directory, "output.json")); }
        else
        {
            bytes = AiDocxParserTests.Package(AiDocxParserTests.Document("<w:p/><w:p><w:r><w:t>正文😀</w:t></w:r></w:p><w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr>" + AiDocxParserTests.Cell("A") + AiDocxParserTests.Cell("B") + "</w:tr></w:tbl><w:p/>"));
            var parsed = await AiDocxParserTests.Run(bytes); Assert.AreEqual(0, parsed.Exit); wire = parsed.Output;
        }
        using var stream = new MemoryStream(wire);
        var read = await AiKnowledgeDocxOutputReader.ReadAsync(stream, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, default);
        Assert.IsTrue(read.IsSuccess); var chunks = AiKnowledgeDocxModelChunker.Chunk(AiKnowledgeDocxPlanningTests.Knowledge(), AiKnowledgeDocxPlanningTests.Model(), read.Document!, new(), default);
        Assert.IsTrue(chunks.IsSuccess); var plan = AiKnowledgeIndexDocxPlan.Create(chunks.Output!, default)!;
        var manifest = AiKnowledgeIndexDocxManifestCodec.Encode(plan, default); var restored = AiKnowledgeIndexDocxManifestCodec.Decode(manifest, default);
        CollectionAssert.AreEqual(read.Document!.Blocks.ToArray(), restored.Source.Document.Blocks.ToArray());
        Assert.AreEqual(read.Document.Text, restored.Source.Document.Text);
        var payload = AiKnowledgeIndexDocxVectorCodec.Encode(plan, 0, AiKnowledgeIndexDocxCodecTests.Vectors(plan.Batches[0].Chunks.Count), default);
        var snapshot = AiKnowledgeIndexDocxVectorCodec.ReadBatch(manifest, 0, payload, default);
        Assert.IsTrue(snapshot.Payload.Chunks.Any(c => c.Block.IsHeader && c.Block.CellNumber == 2));
        foreach (var chunk in snapshot.Payload.Chunks) Assert.AreEqual(read.Document.Text.Substring(chunk.Source.Offset, chunk.Source.Length), chunk.Source.Text);
    }
}

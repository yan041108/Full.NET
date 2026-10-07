using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

/// <summary>串联真实 OOXML 解析和本地模型词表，不调用模型或数据库。</summary>
[TestClass]
public sealed class AiKnowledgeDocxPlanningTests
{
    internal static readonly Guid KnowledgeId = Guid.Parse("019a2ba0-1234-7000-8000-000000000001");
    internal static readonly Guid ModelId = Guid.Parse("019a2ba0-1234-7000-8000-000000000002");
    internal static AiKnowledgeBaseRecord Knowledge(Guid? tenant = null, int approvedVersion = 3) => new() { Id = KnowledgeId, Version = 2, TenantId = tenant, IsEnabled = true, EmbeddingModelConfigId = ModelId, EmbeddingModelVersion = approvedVersion };
    internal static AiModelConfigRecord Model(Guid? tenant = null) => new() { Id = ModelId, Version = 3, TenantId = tenant, IsEnabled = true, ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1" };

    [TestMethod]
    public async Task Real_docx_flows_through_reader_model_chunks_and_complete_plan()
    {
        var bytes = AiDocxParserTests.Package(AiDocxParserTests.Document("<w:p><w:r><w:t>标题😀</w:t></w:r></w:p><w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr>" + AiDocxParserTests.Cell("A") + AiDocxParserTests.Cell("B") + "</w:tr><w:tr>" + AiDocxParserTests.Cell("C") + AiDocxParserTests.Cell("D") + "</w:tr></w:tbl><w:p><w:r><w:t>正文</w:t></w:r></w:p>"));
        byte[] wire;
        // 显式原生专项提供实际工具来源和输出；普通 Unit 通过同一 OOXML 算法提供完整协议。
        if (Environment.GetEnvironmentVariable("FULLNET_DOCX_NATIVE_FIXTURE_DIRECTORY") is { Length: > 0 } directory)
        { bytes = File.ReadAllBytes(Path.Combine(directory, "source.docx")); wire = File.ReadAllBytes(Path.Combine(directory, "output.json")); }
        else { var parsed = await AiDocxParserTests.Run(bytes); Assert.AreEqual(0, parsed.Exit); wire = parsed.Output; }
        using var input = new MemoryStream(wire);
        var read = await AiKnowledgeDocxOutputReader.ReadAsync(input, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, default);
        Assert.IsTrue(read.IsSuccess, read.Failure.ToString()); Assert.IsTrue(input.CanRead);
        Assert.AreEqual("标题😀\nA\tB\nC\tD\n正文\n", read.Document!.Text);
        var chunks = AiKnowledgeDocxModelChunker.Chunk(Knowledge(), Model(), read.Document, new(), default);
        Assert.IsTrue(chunks.IsSuccess, chunks.Failure + ":" + chunks.ChunkFailure);
        Assert.AreEqual(6, chunks.Output!.Chunks.Count);
        foreach (var chunk in chunks.Output.Chunks)
        { Assert.AreEqual(chunk.Text, read.Document.Text.Substring(chunk.Offset, chunk.Length)); Assert.AreEqual(chunk.TokenCount, AiKnowledgeCl100kTokenizer.Get(default).CountTokens(chunk.Text)); }
        var plan = AiKnowledgeIndexDocxPlan.Create(chunks.Output, default);
        Assert.IsNotNull(plan); Assert.AreEqual(6, plan.Summary.ChunkCount); Assert.AreEqual(1, plan.Batches.Count);
        Assert.AreEqual(AiKnowledgeIndexDocxPlan.FormatKey, plan.Summary.FormatKey);
        Assert.AreEqual(2, plan.Source.Document.Blocks[2].CellNumber); Assert.IsTrue(plan.Source.Document.Blocks[2].IsHeader);
        Assert.AreEqual(plan.Summary.PlanHash, AiKnowledgeIndexDocxPlan.Create(chunks.Output, default)!.Summary.PlanHash);
    }

    [TestMethod]
    public async Task Logical_blocks_keep_global_positions_and_overlap_stays_inside_each_block()
    {
        var document = Document(new string('a', 1000) + "😀 end", "second cell");
        var read = await Read(Wire(document), document.ContentHash, document.SizeBytes); Assert.IsTrue(read.IsSuccess);
        var result = AiKnowledgeDocxModelChunker.Chunk(Knowledge(), Model(), read.Document!, new(MaxTokens: 16, OverlapTokens: 4), default);
        Assert.IsTrue(result.IsSuccess); Assert.IsGreaterThan(2, result.Output!.Chunks.Count);
        foreach (var chunk in result.Output.Chunks)
        { var block = document.Blocks[chunk.BlockSequence]; Assert.IsTrue(chunk.Offset >= block.Offset && chunk.Offset + chunk.Length <= block.Offset + block.Length); Assert.AreEqual(chunk.Text, document.Text.Substring(chunk.Offset, chunk.Length)); }
    }

    internal static AiKnowledgeParsedDocx Document(params string[] texts)
    {
        var blocks = new List<AiKnowledgeDocxBlock>(); var text = new StringBuilder();
        foreach (var value in texts) { blocks.Add(new(blocks.Count, "paragraph", text.Length, value.Length, blocks.Count + 1, null, null, null, 0, false)); text.Append(value).Append('\n'); }
        return new(new string('a', 64), 97, text.ToString(), blocks.AsReadOnly());
    }
    internal static byte[] Wire(AiKnowledgeParsedDocx document)
    {
        var root = new JsonObject { ["protocolVersion"] = 1, ["parserVersion"] = AiKnowledgeParsedDocx.ParserVersion, ["contentHash"] = document.ContentHash, ["sizeBytes"] = document.SizeBytes, ["text"] = document.Text };
        var blocks = new JsonArray(); root["blocks"] = blocks;
        foreach (var block in document.Blocks) blocks.Add(new JsonObject { ["sequence"] = block.Sequence, ["kind"] = block.Kind, ["offset"] = block.Offset, ["length"] = block.Length, ["paragraphNumber"] = block.ParagraphNumber, ["tableNumber"] = block.TableNumber, ["rowNumber"] = block.RowNumber, ["cellNumber"] = block.CellNumber, ["cellParagraphCount"] = block.CellParagraphCount, ["isHeader"] = block.IsHeader });
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
    internal static async Task<AiKnowledgeDocxReadResult> Read(byte[] wire, string? hash = null, int size = 97)
    { using var stream = new MemoryStream(wire); var result = await AiKnowledgeDocxOutputReader.ReadAsync(stream, hash ?? new string('a', 64), size, default); Assert.IsTrue(stream.CanRead); return result; }
}

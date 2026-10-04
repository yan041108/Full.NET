using System.Text;
using System.Text.Json;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgePdfPagesTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly Guid ModelId = Guid.CreateVersion7();
    private static readonly Guid KnowledgeId = Guid.CreateVersion7();

    [TestMethod]
    public async Task Complete_pages_preserve_unicode_newlines_and_raw_pdf_identity()
    {
        using var stream = new MemoryStream(Wire("中文\r\n😀 e\u0301", "# title\n| table |"));
        var result = await AiKnowledgePdfOutputReader.ReadAsync(stream, Hash.ToUpperInvariant(), 97, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Hash, result.Document!.ContentHash);
        Assert.AreEqual(97, result.Document.SizeBytes);
        Assert.AreEqual(AiKnowledgePdfReadFailure.None, result.Failure);
        CollectionAssert.AreEqual(new[] { "中文\r\n😀 e\u0301", "# title\n| table |" }, result.Document.Pages.Select(p => p.Text).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2 }, result.Document.Pages.Select(p => p.PageNumber).ToArray());
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("[]")]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"protocolVersion\":1,\"protocolVersion\":1}")]
    [DataRow("{\"protocolVersion\":\"1\"}")]
    [DataRow("{\"protocolVersion\":1.0}")]
    [DataRow("{\"url\":\"https://example.invalid\"}")]
    [DataRow("{\"pages\":null}")]
    [DataRow("{\"pages\":[null]}")]
    [DataRow("{\"pages\":[{\"pageNumber\":1,\"text\":[],\"extra\":true}]}")]
    public async Task Invalid_or_ambiguous_wire_has_no_partial_pages(string json)
        => Rejected(await Read(Encoding.UTF8.GetBytes(json)), AiKnowledgePdfReadFailure.InvalidOutput);

    [TestMethod]
    [DataRow(" \n ")]
    [DataRow("\r\n\t")]
    [DataRow("")]
    public async Task Any_blank_page_rejects_entire_document(string blank)
        => Rejected(await Read(Wire("valid first page", blank)), AiKnowledgePdfReadFailure.EmptyPage);

    [TestMethod]
    public async Task Unknown_parser_is_rejected()
        => Rejected(await Read(Replace(Wire("text"), AiKnowledgeParsedPdf.ParserVersion, "unknown-v1")), AiKnowledgePdfReadFailure.UnsupportedParser);

    [TestMethod]
    public async Task Unknown_protocol_is_rejected()
        => Rejected(await Read(Replace(Wire("text"), "\"protocolVersion\":1", "\"protocolVersion\":2")), AiKnowledgePdfReadFailure.UnsupportedParser);

    [TestMethod]
    public async Task Parser_hash_must_match_independently_supplied_source()
        => Rejected(await Read(Replace(Wire("text"), Hash, new string('b', 64))), AiKnowledgePdfReadFailure.SourceMismatch);

    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    [DataRow(-1)]
    public async Task Pages_must_start_at_one(int number)
        => Rejected(await Read(Replace(Wire("text"), "\"pageNumber\":1", $"\"pageNumber\":{number}")), AiKnowledgePdfReadFailure.InvalidPage);

    [TestMethod]
    public async Task Duplicate_or_gapped_page_number_is_rejected()
    {
        Rejected(await Read(Replace(Wire("a", "b"), "\"pageNumber\":2", "\"pageNumber\":1")), AiKnowledgePdfReadFailure.InvalidPage);
        Rejected(await Read(Replace(Wire("a", "b"), "\"pageNumber\":2", "\"pageNumber\":3")), AiKnowledgePdfReadFailure.InvalidPage);
    }

    [TestMethod]
    public async Task Duplicate_page_text_unknown_fields_and_trailing_json_are_rejected()
    {
        Rejected(await Read(Replace(Wire("text"), "\"text\":\"text\"", "\"text\":\"text\",\"text\":\"other\"")), AiKnowledgePdfReadFailure.InvalidOutput);
        Rejected(await Read(Replace(Wire("text"), "\"text\":\"text\"", "\"text\":\"text\",\"tenantId\":null")), AiKnowledgePdfReadFailure.InvalidOutput);
        Rejected(await Read([.. Wire("text"), .. Encoding.UTF8.GetBytes("{}")]), AiKnowledgePdfReadFailure.InvalidOutput);
    }

    [TestMethod]
    public async Task Invalid_utf8_utf16_and_nul_are_rejected()
    {
        Rejected(await Read([0xff]), AiKnowledgePdfReadFailure.InvalidOutput);
        var wire = Wire("text");
        wire[Encoding.UTF8.GetString(wire).IndexOf("text\"}", StringComparison.Ordinal)] = 0xff;
        Rejected(await Read(wire), AiKnowledgePdfReadFailure.InvalidOutput);
        Rejected(await Read(Replace(Wire("text"), "\"text\":\"text\"", "\"text\":\"\\uD800\"")), AiKnowledgePdfReadFailure.InvalidOutput);
        Rejected(await Read(Wire("valid", "a\0b")), AiKnowledgePdfReadFailure.InvalidPage);
    }

    [TestMethod]
    public async Task Empty_pages_are_rejected()
        => Rejected(await Read(Wire()), AiKnowledgePdfReadFailure.InvalidPage);

    [TestMethod]
    public async Task Page_count_limit_is_inclusive()
    {
        Assert.IsTrue((await Read(Wire(Enumerable.Repeat("a", 128).ToArray()))).IsSuccess);
        Rejected(await Read(Wire(Enumerable.Repeat("a", 129).ToArray())), AiKnowledgePdfReadFailure.TooManyPages);
    }

    [TestMethod]
    public async Task Total_extracted_character_limit_is_inclusive_across_pages()
    {
        var page = new string('中', AiKnowledgeTextParser.MaxOutputCharacters / 2);
        Assert.IsTrue((await Read(Wire(page, page))).IsSuccess);
        Rejected(await Read(Wire(page, page + "x")), AiKnowledgePdfReadFailure.OutputTooLarge);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("xyz")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaag")]
    public async Task Source_hash_is_validated_before_reading(string hash)
    {
        using var stream = new ObservedStream(Wire("text"), () => Assert.Fail("无效来源不应读取输出。"));
        Rejected(await AiKnowledgePdfOutputReader.ReadAsync(stream, hash, 97, CancellationToken.None), AiKnowledgePdfReadFailure.InvalidSource);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(5242881)]
    public async Task Source_byte_limit_is_checked_before_reading(int size)
    {
        using var stream = new ObservedStream(Wire("text"), () => Assert.Fail("无效来源不应读取输出。"));
        Rejected(await AiKnowledgePdfOutputReader.ReadAsync(stream, Hash, size, CancellationToken.None), AiKnowledgePdfReadFailure.InvalidSource);
    }

    [TestMethod]
    public async Task Non_seekable_wire_reads_at_most_limit_plus_one()
    {
        using var stream = new EndlessStream();
        Rejected(await AiKnowledgePdfOutputReader.ReadAsync(stream, Hash, 97, CancellationToken.None), AiKnowledgePdfReadFailure.OutputTooLarge);
        Assert.AreEqual(AiKnowledgePdfOutputReader.MaxWireBytes + 1, stream.ReadBytes);
        Assert.IsTrue(stream.CanRead);
    }

    [TestMethod]
    public async Task Cancellation_and_io_failure_propagate_without_owning_stream()
    {
        using var cancel = new CancellationTokenSource();
        using var stream = new ObservedStream(Wire("text"), () => cancel.Cancel());
        await Assert.ThrowsAsync<OperationCanceledException>(() => AiKnowledgePdfOutputReader.ReadAsync(stream, Hash, 97, cancel.Token));
        Assert.IsTrue(stream.CanRead);
        var error = new IOException("synthetic read failure");
        using var broken = new ObservedStream(Wire("text"), () => throw error);
        Assert.AreSame(error, await Assert.ThrowsExactlyAsync<IOException>(() => AiKnowledgePdfOutputReader.ReadAsync(broken, Hash, 97, CancellationToken.None)));
    }

    [TestMethod]
    public void Approved_model_pdf_chunks_have_global_sequences_and_page_positions()
    {
        var document = Document("hello world", "中文\r\n😀 e\u0301 # 标题\n| 原始表格 |");
        var result = Chunk(document, new(MaxTokens: 8, OverlapTokens: 2));
        Assert.IsTrue(result.IsSuccess);
        var output = result.Output!;
        Assert.AreEqual(Hash, output.ContentHash);
        Assert.AreEqual(AiKnowledgeParsedPdf.ParserVersion, output.ParserVersion);
        Assert.AreEqual(AiKnowledgeCl100kTokenizer.Version, output.TokenizerVersion);
        Assert.AreEqual("pdf-page-window-v1", output.ChunkingVersion);
        Assert.AreEqual(KnowledgeId, output.KnowledgeBaseId);
        Assert.AreEqual(2, output.KnowledgeBaseVersion);
        Assert.AreEqual(ModelId, output.ModelConfigId);
        Assert.AreEqual(3, output.ModelConfigVersion);
        Assert.AreEqual("openai_compatible", output.ProviderKey);
        Assert.AreEqual("text-embedding-3-small", output.ModelId);
        Assert.IsNull(output.TenantId);
        CollectionAssert.AreEqual(Enumerable.Range(0, output.Chunks.Count).ToArray(), output.Chunks.Select(c => c.Sequence).ToArray());
        Assert.AreEqual(2, output.Chunks.First().TokenCount);
        foreach (var page in document.Pages)
        {
            var covered = new bool[page.Text.Length];
            foreach (var chunk in output.Chunks.Where(c => c.PageNumber == page.PageNumber))
            {
                Assert.AreEqual(page.Text.Substring(chunk.Offset, chunk.Length), chunk.Text);
                Assert.AreEqual(chunk.Text, chunk.EmbeddingText);
                Assert.IsTrue(chunk.TokenCount <= 8);
                Assert.IsFalse(char.IsLowSurrogate(chunk.Text[0]) || char.IsHighSurrogate(chunk.Text[^1]));
                Assert.IsFalse(chunk.Offset > 0 && page.Text[chunk.Offset - 1] == '\r' && chunk.Text[0] == '\n');
                for (var i = chunk.Offset; i < chunk.Offset + chunk.Length; i++) covered[i] = true;
            }
            for (var i = 0; i < covered.Length; i++) if (!char.IsWhiteSpace(page.Text[i])) Assert.IsTrue(covered[i]);
        }
    }

    [TestMethod]
    [DataRow(1, 100, (int)AiKnowledgeChunkFailure.TooManyChunks)]
    [DataRow(100, 3, (int)AiKnowledgeChunkFailure.TotalTokenBudgetExceeded)]
    public void Whole_pdf_budget_cannot_reset_per_page(int chunks, int tokens, int failure)
        => ChunkRejected(Chunk(Document("hello world", "hello world"), new(MaxChunks: chunks, MaxTotalTokens: tokens)), (AiKnowledgeChunkFailure)failure);

    [TestMethod]
    public void Every_page_revalidates_input_and_no_partial_chunks_escape()
    {
        ChunkRejected(Chunk(Document("valid", "")), AiKnowledgeChunkFailure.InvalidDocument);
        ChunkRejected(Chunk(Document("valid", "a\0b")), AiKnowledgeChunkFailure.InvalidDocument);
        ChunkRejected(Chunk(Document("valid", "\ud800")), AiKnowledgeChunkFailure.InvalidDocument);
        ChunkRejected(Chunk(new(Hash, 97, [new(1, "valid"), new(3, "text")])), AiKnowledgeChunkFailure.InvalidDocument);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Exact_model_approval_and_scope_cannot_be_chosen_by_output(int scenario)
    {
        var knowledge = Knowledge();
        var model = Model();
        if (scenario == 0) knowledge = Knowledge(approvedVersion: 2);
        if (scenario == 1) model = Model(tenant: Guid.CreateVersion7());
        if (scenario == 2) model = Model(modelName: "unknown-model");
        var result = AiKnowledgePdfModelChunker.Chunk(knowledge, model, Document("hello world"), new(), CancellationToken.None);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Output);
        Assert.AreEqual(scenario switch { 0 => AiKnowledgeModelChunkFailure.ModelNotApproved, 1 => AiKnowledgeModelChunkFailure.ModelScopeMismatch, _ => AiKnowledgeModelChunkFailure.UnsupportedModel }, result.Failure);
    }

    [TestMethod]
    public void Chunk_cancellation_is_observed_before_output()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgePdfModelChunker.Chunk(Knowledge(), Model(), Document("hello world"), new(), cancel.Token));
    }

    [TestMethod]
    public async Task Source_byte_boundary_and_fragmented_wire_are_accepted()
    {
        using var stream = new FragmentedStream(Wire("中文 😀\r\n保留换行"));
        var result = await AiKnowledgePdfOutputReader.ReadAsync(stream, Hash, 5 * 1024 * 1024, CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("中文 😀\r\n保留换行", result.Document!.Pages.Single().Text);
    }

    [TestMethod]
    public async Task Field_order_does_not_change_identity_and_escaped_duplicate_fields_are_rejected()
    {
        var json = $"{{\"pages\":[{{\"text\":\"hello world\",\"pageNumber\":1}}],\"contentHash\":\"{Hash}\",\"parserVersion\":\"{AiKnowledgeParsedPdf.ParserVersion}\",\"protocolVersion\":1}}";
        Assert.IsTrue((await Read(Encoding.UTF8.GetBytes(json))).IsSuccess);
        Rejected(await Read(Encoding.UTF8.GetBytes(json.Replace("\"protocolVersion\":1", "\"protocolVersion\":1,\"pro\\u0074ocolVersion\":1", StringComparison.Ordinal))), AiKnowledgePdfReadFailure.InvalidOutput);
    }

    [TestMethod]
    public async Task Nonfinite_numbers_comments_and_trailing_commas_are_rejected()
    {
        Rejected(await Read(Replace(Wire("text"), "\"protocolVersion\":1", "\"protocolVersion\":NaN")), AiKnowledgePdfReadFailure.InvalidOutput);
        Rejected(await Read([.. Encoding.UTF8.GetBytes("/*comment*/"), .. Wire("text")]), AiKnowledgePdfReadFailure.InvalidOutput);
        Rejected(await Read(Replace(Wire("text"), "\"text\":\"text\"", "\"text\":\"text\",")), AiKnowledgePdfReadFailure.InvalidOutput);
    }

    [TestMethod]
    public void Invalid_pdf_records_and_invalid_options_have_no_partial_output()
    {
        ChunkRejected(Chunk(new("bad-hash", 97, [new(1, "text")])), AiKnowledgeChunkFailure.InvalidDocument);
        ChunkRejected(Chunk(new(Hash, 0, [new(1, "text")])), AiKnowledgeChunkFailure.InvalidDocument);
        ChunkRejected(Chunk(Document()), AiKnowledgeChunkFailure.InvalidDocument);
        ChunkRejected(Chunk(Document("text"), new(MaxChunks: 0)), AiKnowledgeChunkFailure.InvalidOptions);
    }

    [TestMethod]
    public void Pdf_page_adapter_keeps_pdf_parser_identity_and_unknown_origins_are_rejected()
    {
        var page = new AiKnowledgeParsedText("hello world", Hash, 97, AiKnowledgeTextSource.PdfPage);
        var result = AiKnowledgeModelChunker.Chunk(Knowledge(), Model(), page, AiKnowledgeTextFormat.PlainText, new(), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(AiKnowledgeParsedPdf.ParserVersion, result.Output!.Chunks.ParserVersion);
        Assert.AreEqual("utf8-text-v1", new AiKnowledgeParsedText("text", Hash, 97).ParserVersion);
        var invalid = AiKnowledgeModelChunker.Chunk(Knowledge(), Model(), page with { Source = (AiKnowledgeTextSource)99 }, AiKnowledgeTextFormat.PlainText, new(), CancellationToken.None);
        Assert.IsFalse(invalid.IsSuccess);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidDocument, invalid.ChunkFailure);
    }

    private static AiKnowledgeBaseRecord Knowledge(int approvedVersion = 3) => new() { Id = KnowledgeId, Version = 2, IsEnabled = true, DataClassification = "internal", EmbeddingModelConfigId = ModelId, EmbeddingModelVersion = approvedVersion };
    private static AiModelConfigRecord Model(Guid? tenant = null, string modelName = "text-embedding-3-small") => new() { Id = ModelId, TenantId = tenant, Version = 3, IsEnabled = true, ProviderKey = "openai_compatible", ModelId = modelName, EndpointBaseUrl = "https://api.openai.com/v1" };
    private static AiKnowledgeParsedPdf Document(params string[] pages) => new(Hash, 97, Array.AsReadOnly(pages.Select((text, i) => new AiKnowledgePdfPage(i + 1, text)).ToArray()));
    private static AiKnowledgePdfModelChunkResult Chunk(AiKnowledgeParsedPdf document, AiKnowledgeChunkOptions? options = null) => AiKnowledgePdfModelChunker.Chunk(Knowledge(), Model(), document, options ?? new(), CancellationToken.None);
    private static void ChunkRejected(AiKnowledgePdfModelChunkResult result, AiKnowledgeChunkFailure failure)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Output);
        Assert.AreEqual(AiKnowledgeModelChunkFailure.ChunkingFailed, result.Failure);
        Assert.AreEqual(failure, result.ChunkFailure);
    }
    private static byte[] Replace(byte[] bytes, string oldValue, string newValue) => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace(oldValue, newValue, StringComparison.Ordinal));
    private static byte[] Wire(params string[] pages)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", 1);
            writer.WriteString("parserVersion", AiKnowledgeParsedPdf.ParserVersion);
            writer.WriteString("contentHash", Hash);
            writer.WriteStartArray("pages");
            foreach (var (text, index) in pages.Select((text, i) => (text, i)))
            {
                writer.WriteStartObject();
                writer.WriteNumber("pageNumber", index + 1);
                writer.WriteString("text", text);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
    private static async Task<AiKnowledgePdfReadResult> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await AiKnowledgePdfOutputReader.ReadAsync(stream, Hash, 97, CancellationToken.None);
    }
    private static void Rejected(AiKnowledgePdfReadResult result, AiKnowledgePdfReadFailure failure)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Document);
        Assert.AreEqual(failure, result.Failure);
    }
    private sealed class ObservedStream(byte[] bytes, Action observe) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { observe(); return base.ReadAsync(buffer, cancellationToken); }
    }
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(7, buffer.Length)], cancellationToken);
    }
    private sealed class EndlessStream : Stream
    {
        internal int ReadBytes { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)'x', offset, count); ReadBytes += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { buffer.Span.Fill((byte)'x'); ReadBytes += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

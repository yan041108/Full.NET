using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Ai.PdfParser;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using UglyToad.PdfPig.Tokens;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Pdf Parser。</summary>
[TestClass]
public sealed class AiPdfParserTests
{
    private static readonly DictionaryToken Empty = new(new Dictionary<NameToken, IToken>());

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Real_pdf_pages_are_consumed_with_independent_raw_digest(bool compressed)
    {
        var bytes = PdfFixtures.CreatePdf("合成制度 FN-PDF-042：三个工作日。", 2, compressed);
        var result = await Run(bytes);
        Assert.AreEqual(0, result.Exit);
        Assert.AreEqual("", result.Errors);
        using var output = new MemoryStream(result.Output);
        var parsed = await AiKnowledgePdfOutputReader.ReadAsync(output, Hash(bytes), bytes.Length, CancellationToken.None);
        Assert.IsTrue(parsed.IsSuccess);
        Assert.AreEqual(2, parsed.Document!.Pages.Count);
        Assert.AreEqual(Hash(bytes), parsed.Document.ContentHash);
        CollectionAssert.AreEqual(new[] { 1, 2 }, parsed.Document.Pages.Select(p => p.PageNumber).ToArray());
        foreach (var page in parsed.Document.Pages) Assert.AreEqual("合成制度 FN-PDF-042：三个工作日。", page.Text);
        using var wire = System.Text.Json.JsonDocument.Parse(result.Output);
        Assert.AreEqual(AiKnowledgeParsedPdf.ParserVersion, wire.RootElement.GetProperty("parserVersion").GetString());
        Assert.IsTrue(output.CanRead);
    }

    [TestMethod]
    public async Task Real_parser_output_reaches_approved_pdf_model_chunker()
    {
        var bytes = PdfFixtures.CreatePdf("中文检索依据 FN-101。", 2, true);
        var result = await Run(bytes);
        Assert.AreEqual(0, result.Exit);
        using var output = new MemoryStream(result.Output);
        var parsed = await AiKnowledgePdfOutputReader.ReadAsync(output, Hash(bytes), bytes.Length, CancellationToken.None);
        var id = Guid.CreateVersion7();
        var knowledge = new AiKnowledgeBaseRecord { Id = Guid.CreateVersion7(), Version = 2, IsEnabled = true, DataClassification = "internal", EmbeddingModelConfigId = id, EmbeddingModelVersion = 3 };
        var model = new AiModelConfigRecord { Id = id, Version = 3, IsEnabled = true, ProviderKey = "openai_compatible", ModelId = "text-embedding-3-small", EndpointBaseUrl = "https://api.openai.com/v1" };
        var chunks = AiKnowledgePdfModelChunker.Chunk(knowledge, model, parsed.Document!, new(), CancellationToken.None);
        Assert.IsTrue(chunks.IsSuccess);
        Assert.AreEqual(Hash(bytes), chunks.Output!.ContentHash);
        Assert.AreEqual(id, chunks.Output.ModelConfigId);
        CollectionAssert.AreEqual(new[] { 1, 2 }, chunks.Output.Chunks.Select(c => c.PageNumber).Distinct().ToArray());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("ordinary text")]
    [DataRow(" %PDF-1.7\n")]
    [DataRow("%PDF-9.0\n")]
    [DataRow("%PDF-1.X\n")]
    public async Task Invalid_signature_has_no_output(string source)
        => AssertFailed(await Run(Encoding.UTF8.GetBytes(source)), "fn_ai_pdf_bad_signature");

    [TestMethod]
    public async Task Oversized_input_is_rejected_before_parsing()
        => AssertFailed(await Run(new byte[PdfParser.MaxInputBytes + 1]), "fn_ai_pdf_input_too_large");

    [TestMethod]
    public async Task Corrupt_pdf_has_only_a_fixed_error_code()
        => AssertFailed(await Run(Encoding.UTF8.GetBytes("%PDF-1.7\nprivate source and credentials\n%%EOF")), "fn_ai_pdf_parse_failed");

    [TestMethod]
    public async Task Any_blank_page_rejects_every_page()
        => AssertFailed(await Run(PdfFixtures.CreatePdf("", 2)), "fn_ai_pdf_empty_page");

    [TestMethod]
    public async Task Page_limit_rejects_without_reading_partial_pages()
        => AssertFailed(await Run(PdfFixtures.CreatePdf("正文", 129)), "fn_ai_pdf_page_limit");

    [TestMethod]
    public void Collector_accepts_character_boundary_and_fails_closed_after_overflow()
    {
        var collector = new PdfOutput();
        collector.Add(1, new string('中', AiKnowledgeTextParser.MaxOutputCharacters));
        using var output = new MemoryStream();
        collector.Write(new string('a', 64), output);
        Assert.IsTrue(output.Length > 0);
        var error = Assert.ThrowsExactly<PdfRejectedException>(() => collector.Add(2, "x"));
        Assert.AreEqual("fn_ai_pdf_output_limit", error.Code);
        using var failed = new MemoryStream();
        Assert.ThrowsExactly<PdfRejectedException>(() => collector.Write(new string('a', 64), failed));
        Assert.AreEqual(0L, failed.Length);
    }

    [TestMethod]
    public void Collector_enforces_page_order_and_utf16_without_partial_output()
    {
        foreach (var text in new[] { "\0", "\ud800", "\udc00" })
        {
            var collector = new PdfOutput();
            Assert.AreEqual("fn_ai_pdf_invalid_text", Assert.ThrowsExactly<PdfRejectedException>(() => collector.Add(1, text)).Code);
        }
        var ordered = new PdfOutput();
        Assert.AreEqual("fn_ai_pdf_page_order", Assert.ThrowsExactly<PdfRejectedException>(() => ordered.Add(2, "text")).Code);
        var pages = new PdfOutput();
        for (var number = 1; number <= 128; number++) pages.Add(number, "text");
        Assert.AreEqual("fn_ai_pdf_page_limit", Assert.ThrowsExactly<PdfRejectedException>(() => pages.Add(129, "text")).Code);
    }

    [TestMethod]
    [DataRow("FlateDecode")]
    [DataRow("Fl")]
    public void Bounded_zlib_decoder_preserves_bytes(string name)
    {
        var provider = new BoundedFilterProvider();
        var plain = Encoding.UTF8.GetBytes("真实有界解压\r\n中文 e\u0301 😀");
        CollectionAssert.AreEqual(plain, Decode(provider, name, Compress(plain)).ToArray());
        provider.EnsureHealthy();
    }

    [TestMethod]
    [DataRow("ASCIIHexDecode")]
    [DataRow("AHx")]
    public void Ascii_hex_odd_nibble_and_whitespace_are_preserved(string name)
        => CollectionAssert.AreEqual(new byte[] { 0x41, 0x42, 0xf0 }, Decode(new(), name, Encoding.ASCII.GetBytes("41 \n42 F>\r\n")).ToArray());

    [TestMethod]
    public void Decompression_limit_is_checked_before_appending_and_remains_failed()
    {
        var provider = new BoundedFilterProvider();
        var exact = new byte[BoundedFilterProvider.MaxDecodedBytes];
        Assert.AreEqual(exact.Length, Decode(provider, "FlateDecode", Compress(exact)).Length);
        Assert.AreEqual("fn_ai_pdf_decode_limit", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(provider, "FlateDecode", Compress([0]))).Code);
        Assert.AreEqual("fn_ai_pdf_decode_limit", Assert.ThrowsExactly<PdfRejectedException>(provider.EnsureHealthy).Code);
        var bomb = Compress(new byte[BoundedFilterProvider.MaxDecodedBytes + 1]);
        Assert.AreEqual("fn_ai_pdf_decode_limit", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "FlateDecode", bomb)).Code);
    }

    [TestMethod]
    public void Decoded_budget_is_shared_by_all_streams_and_filter_stages()
    {
        var provider = new BoundedFilterProvider();
        var compressed = Compress(new byte[20 * 1024 * 1024]);
        Assert.AreEqual(20 * 1024 * 1024, Decode(provider, "FlateDecode", compressed).Length);
        Assert.AreEqual("fn_ai_pdf_decode_limit", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(provider, "FlateDecode", compressed)).Code);
    }

    [TestMethod]
    [DataRow("LZWDecode")]
    [DataRow("ASCII85Decode")]
    [DataRow("unknown")]
    public void Unbounded_or_unknown_filters_are_rejected_and_sticky(string name)
    {
        var provider = new BoundedFilterProvider();
        Assert.AreEqual("fn_ai_pdf_unsupported_filter", Assert.ThrowsExactly<PdfRejectedException>(() => provider.GetNamedFilters([NameToken.Create(name)])).Code);
        Assert.AreEqual("fn_ai_pdf_unsupported_filter", Assert.ThrowsExactly<PdfRejectedException>(provider.EnsureHealthy).Code);
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(12)]
    public void Unsupported_predictors_are_rejected_before_decoding(int predictor)
    {
        var parameters = new DictionaryToken(new Dictionary<NameToken, IToken> { [NameToken.Predictor] = new NumericToken(predictor) });
        var dictionary = new DictionaryToken(new Dictionary<NameToken, IToken> { [NameToken.Filter] = NameToken.Create("FlateDecode"), [NameToken.DecodeParms] = parameters });
        Assert.AreEqual("fn_ai_pdf_unsupported_predictor", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "FlateDecode", Compress([1, 2]), dictionary)).Code);
    }

    [TestMethod]
    public void Corrupt_compressed_bytes_and_bad_hex_never_fall_back_to_input()
    {
        var compressed = Compress([1, 2, 3]);
        compressed[^1] ^= 1;
        Assert.AreEqual("fn_ai_pdf_corrupt_stream", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "FlateDecode", compressed)).Code);
        Assert.AreEqual("fn_ai_pdf_corrupt_stream", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "ASCIIHexDecode", Encoding.ASCII.GetBytes("xyz>"))).Code);
    }

    [TestMethod]
    public void Non_final_deflate_with_a_matching_checksum_is_rejected()
        => Assert.AreEqual("fn_ai_pdf_corrupt_stream", Assert.ThrowsExactly<PdfRejectedException>(() =>
            Decode(new(), "FlateDecode", Convert.FromHexString("7801000300FCFF010203000000FFFF000D0007"))).Code);

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void Unresolvable_or_malformed_decode_parameters_fail_closed(int shape)
    {
        IToken parameters = shape switch
        {
            0 => new IndirectReferenceToken(new UglyToad.PdfPig.Core.IndirectReference(9, 0)),
            1 => new NumericToken(12),
            _ => new ArrayToken([new IndirectReferenceToken(new UglyToad.PdfPig.Core.IndirectReference(9, 0))])
        };
        var dictionary = new DictionaryToken(new Dictionary<NameToken, IToken>
        {
            [NameToken.Filter] = shape == 2 ? new ArrayToken([NameToken.Create("FlateDecode")]) : NameToken.Create("FlateDecode"),
            [NameToken.DecodeParms] = parameters
        });
        Assert.AreEqual("fn_ai_pdf_unsupported_predictor", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "FlateDecode", Compress([1, 2]), dictionary)).Code);
    }

    [TestMethod]
    [DataRow(CompressionLevel.Optimal)]
    [DataRow(CompressionLevel.Fastest)]
    [DataRow(CompressionLevel.NoCompression)]
    [DataRow(CompressionLevel.SmallestSize)]
    public void Stored_fixed_and_dynamic_deflate_framing_preserves_valid_data(CompressionLevel level)
    {
        var bytes = new byte[300000];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index % 13);
        new Random(42).NextBytes(bytes.AsSpan(bytes.Length - 1000));
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, level, true)) compressor.Write(bytes);
        CollectionAssert.AreEqual(bytes, Decode(new(), "FlateDecode", output.ToArray()).ToArray());
    }

    [TestMethod]
    public void Truncated_deflate_and_trailing_compressed_bytes_are_rejected()
    {
        var bytes = Compress(Encoding.UTF8.GetBytes("不得接受不完整的压缩流。"));
        for (var remove = 1; remove <= 6; remove++)
        {
            var truncated = bytes[..^(4 + remove)].Concat(bytes[^4..]).ToArray();
            Assert.AreEqual("fn_ai_pdf_corrupt_stream", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "FlateDecode", truncated)).Code);
        }
        var trailing = bytes[..^4].Concat(new byte[] { 0 }).Concat(bytes[^4..]).ToArray();
        Assert.AreEqual("fn_ai_pdf_corrupt_stream", Assert.ThrowsExactly<PdfRejectedException>(() => Decode(new(), "FlateDecode", trailing)).Code);
    }

    [TestMethod]
    public async Task Unknown_real_pdf_filter_cannot_be_silently_ignored()
    {
        var bytes = PdfFixtures.CreatePdf("不得静默跳过", 1, true);
        bytes = Encoding.ASCII.GetBytes(Encoding.ASCII.GetString(bytes).Replace("/FlateDecode", "/LZWDecode", StringComparison.Ordinal));
        AssertFailed(await Run(bytes), "fn_ai_pdf_unsupported_filter");
    }

    private static Memory<byte> Decode(BoundedFilterProvider provider, string name, byte[] bytes, DictionaryToken? dictionary = null)
        => provider.GetNamedFilters([NameToken.Create(name)]).Single().Decode(bytes, dictionary ?? Empty, provider, 0);
    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.SmallestSize, true)) compressor.Write(bytes);
        return output.ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task<(int Exit, byte[] Output, string Errors)> Run(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var output = new MemoryStream();
        using var errors = new StringWriter();
        var exit = await PdfParser.RunAsync(input, output, errors);
        Assert.IsTrue(input.CanRead && output.CanWrite);
        return (exit, output.ToArray(), errors.ToString());
    }
    private static void AssertFailed((int Exit, byte[] Output, string Errors) result, string code)
    {
        Assert.AreEqual(2, result.Exit);
        Assert.AreEqual(0, result.Output.Length);
        Assert.AreEqual(code + Environment.NewLine, result.Errors);
    }
}

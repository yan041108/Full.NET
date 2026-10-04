using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.ML.Tokenizers;

namespace Full.NET.UnitTests.Ai;

/// <summary>用自建真实 BPE 词表验证预算、覆盖、原文位置和取消；夹具词表不代表任何生产模型。</summary>
[TestClass]
public sealed class AiKnowledgeChunkingTests
{
    [TestMethod]
    [DataRow("hello hello hello hello 中文编号 FN-DEMO-042")]
    [DataRow("😀😃 中文跨块\r\n末尾 e\u0301 与 é")]
    [DataRow("# 制度\r\n\r\n|名称|数值|\r\n|---|---|\r\n|方案|80|\r\n```js\nalert('原文');\n```\n[外链](https://example.invalid)")]
    [DataRow("<|endoftext|> 与 <script> 均是文档原文，不作为控制指令")]
    public void Original_text_is_fully_covered_with_real_token_counts_and_versioned_metadata(string text)
    {
        var tokenizer = Fixture();
        var document = Document(text);
        var options = new AiKnowledgeChunkOptions(24, 4);
        var result = AiKnowledgeTextChunker.Chunk(document, tokenizer, "fixture-bpe-v1", options, CancellationToken.None);
        AssertCovered(document, tokenizer, options, result);
        Assert.AreEqual(document.ContentHash, result.Output!.ContentHash);
        Assert.AreEqual(document.ParserVersion, result.Output.ParserVersion);
        Assert.AreEqual("fixture-bpe-v1", result.Output.TokenizerVersion);
        Assert.AreEqual("token-window-v1", result.Output.ChunkingVersion);
        Assert.AreEqual(options, result.Output.Options);
        var replay = AiKnowledgeTextChunker.Chunk(document, tokenizer, "fixture-bpe-v1", options, CancellationToken.None);
        CollectionAssert.AreEqual(result.Output.Chunks.ToArray(), replay.Output!.Chunks.ToArray());
    }

    [TestMethod]
    public void Bpe_merge_is_counted_as_one_token_instead_of_five_characters()
    {
        var tokenizer = Fixture();
        Assert.AreEqual(1, tokenizer.CountTokens("hello"));
        var result = AiKnowledgeTextChunker.Chunk(Document("hello"), tokenizer, "fixture-bpe-v1", new(1, 0), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        Assert.AreEqual("hello", result.Output!.Chunks.Single().Text);
        Assert.AreEqual(1, result.Output.Chunks.Single().TokenCount);
    }

    [TestMethod]
    public void Cross_character_byte_merge_does_not_reject_independently_encodable_prefixes()
    {
        // 整窗 a+é 先合并 a+C3，SDK 索引无法在预算 1 内跨重叠 UTF-16 区间；独立 a、é 均能合法编码。
        var tokenizer = Fixture(crossingMerge: 1);
        Assert.AreEqual(1, tokenizer.CountTokens("a"));
        Assert.AreEqual(1, tokenizer.CountTokens("é"));
        Assert.AreEqual(2, tokenizer.CountTokens("aé"));
        var result = AiKnowledgeTextChunker.Chunk(Document("aé"), tokenizer, "fixture-cross-byte-v1", new(1, 0), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        CollectionAssert.AreEqual(new[] { "a", "é" }, result.Output!.Chunks.Select(chunk => chunk.Text).ToArray());
    }

    [TestMethod]
    public void More_expensive_first_character_does_not_hide_a_cheaper_longer_prefix()
    {
        var tokenizer = Fixture(crossingMerge: 2);
        Assert.AreEqual(2, tokenizer.CountTokens("é"));
        Assert.AreEqual(1, tokenizer.CountTokens("éa"));
        Assert.AreEqual(1, tokenizer.CountTokens("éb"));
        var result = AiKnowledgeTextChunker.Chunk(Document("éaéb"), tokenizer, "fixture-nonmonotonic-v1", new(1, 0), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        CollectionAssert.AreEqual(new[] { "éa", "éb" }, result.Output!.Chunks.Select(chunk => chunk.Text).ToArray());
    }

    [TestMethod]
    public void Exhausted_boundary_search_is_distinct_from_an_impossible_token_budget()
    {
        var tokenizer = Fixture(crossingMerge: 3);
        var prefix = "é" + new string('a', 128);
        Assert.AreEqual(1, tokenizer.CountTokens(prefix));
        Assert.AreEqual(1, tokenizer.CountTokens("éb"));
        var result = AiKnowledgeTextChunker.Chunk(Document(prefix + "éb"), tokenizer, "fixture-long-boundary-v1", new(1, 0), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.BoundarySearchLimitExceeded, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public async Task Parser_bom_digest_and_utf16_offsets_flow_into_chunks_without_rewriting_text()
    {
        const string text = "😀 原文\r\n中文段落\n最后一行";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        using var stream = new MemoryStream(bytes);
        var parsed = await AiKnowledgeTextParser.ParseAsync(stream, "source.md", "text/markdown", Hash(bytes), CancellationToken.None);
        Assert.IsTrue(parsed.IsSuccess);
        var tokenizer = Fixture();
        var options = new AiKnowledgeChunkOptions(12, 2);
        var result = AiKnowledgeTextChunker.Chunk(parsed.Document!, tokenizer, "fixture-bpe-v1", options, CancellationToken.None);
        AssertCovered(parsed.Document!, tokenizer, options, result);
        Assert.AreEqual(Hash(bytes), result.Output!.ContentHash);
    }

    [TestMethod]
    public void Overlap_is_a_bounded_original_suffix_and_each_chunk_advances()
    {
        const string text = "bcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOP";
        var tokenizer = Fixture();
        var document = Document(text);
        var options = new AiKnowledgeChunkOptions(16, 4);
        var result = AiKnowledgeTextChunker.Chunk(document, tokenizer, "fixture-bpe-v1", options, CancellationToken.None);
        AssertCovered(document, tokenizer, options, result);
        Assert.IsTrue(result.Output!.Chunks.Count > 1);
        Assert.AreEqual(12, result.Output.Chunks[1].Offset);
    }

    [TestMethod]
    public void Zero_overlap_reconstructs_exact_text_including_all_line_endings()
    {
        const string text = "abcdefghijklmno\r\n😀 中文\r单独回车\n最后段落";
        var tokenizer = Fixture();
        var result = AiKnowledgeTextChunker.Chunk(Document(text), tokenizer, "fixture-bpe-v1", new(16, 0), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        Assert.AreEqual(text, string.Concat(result.Output!.Chunks.Select(chunk => chunk.Text)));
    }

    [TestMethod]
    public void Later_line_boundary_is_preferred_within_the_token_budget()
    {
        const string text = "bcdefghijk\nABCDEFGHIJ123456789";
        var result = AiKnowledgeTextChunker.Chunk(Document(text), Fixture(), "fixture-bpe-v1", new(16, 0), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        Assert.AreEqual("bcdefghijk\n", result.Output!.Chunks[0].Text);
    }

    [TestMethod]
    public void Large_low_token_text_is_split_by_bounded_windows_even_when_the_token_budget_has_space()
    {
        var text = new string('a', AiKnowledgeTextChunker.MaxWindowCharacters * 2 + 1);
        var observed = new List<int>();
        var tokenizer = Fixture(preTokenizer: new ProbePreTokenizer(observed.Add));
        var options = new AiKnowledgeChunkOptions(4096, 0);
        var result = AiKnowledgeTextChunker.Chunk(Document(text), tokenizer, "fixture-bpe-v1", options, CancellationToken.None);
        AssertCovered(Document(text), tokenizer, options, result);
        Assert.AreEqual(3, result.Output!.Chunks.Count);
        Assert.AreEqual(AiKnowledgeTextChunker.MaxWindowCharacters, result.Output.Chunks[0].Length);
        Assert.IsTrue(observed.Count > 0 && observed.All(length => length <= AiKnowledgeTextChunker.MaxWindowCharacters));
    }

    [TestMethod]
    [DataRow("中", 2)]
    [DataRow("😀", 3)]
    [DataRow("\r\nx", 1)]
    public void Budget_too_small_for_complete_unicode_or_crlf_returns_no_partial_output(string text, int limit)
    {
        var result = AiKnowledgeTextChunker.Chunk(Document(text), Fixture(), "fixture-bpe-v1", new(limit, 0), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.TokenBudgetTooSmall, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Chunk_count_limit_accepts_exact_boundary_and_rejects_overflow_without_partial_output()
    {
        var tokenizer = Fixture();
        Assert.IsTrue(AiKnowledgeTextChunker.Chunk(Document(new string('b', 32)), tokenizer, "fixture-bpe-v1", new(16, 0, 2), CancellationToken.None).IsSuccess);
        var rejected = AiKnowledgeTextChunker.Chunk(Document(new string('b', 33)), tokenizer, "fixture-bpe-v1", new(16, 0, 2), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.TooManyChunks, rejected.Failure);
        Assert.IsNull(rejected.Output);
    }

    [TestMethod]
    [DataRow(0, 32)]
    [DataRow(4, 40)]
    public void Total_token_budget_includes_overlap_and_rejects_overflow(int overlap, int expectedTotal)
    {
        var tokenizer = Fixture();
        var document = Document(new string('b', 32));
        var accepted = AiKnowledgeTextChunker.Chunk(document, tokenizer, "fixture-bpe-v1", new(16, overlap, 10, expectedTotal), CancellationToken.None);
        Assert.IsTrue(accepted.IsSuccess, accepted.Failure.ToString());
        Assert.AreEqual(expectedTotal, accepted.Output!.Chunks.Sum(chunk => chunk.TokenCount));
        var rejected = AiKnowledgeTextChunker.Chunk(document, tokenizer, "fixture-bpe-v1", new(16, overlap, 10, expectedTotal - 1), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded, rejected.Failure);
        Assert.IsNull(rejected.Output);
    }

    [TestMethod]
    [DataRow(0, 0, 10, 100)]
    [DataRow(4097, 0, 10, 100)]
    [DataRow(16, -1, 10, 100)]
    [DataRow(4096, 257, 10, 100)]
    [DataRow(16, 5, 10, 100)]
    [DataRow(16, 0, 0, 100)]
    [DataRow(16, 0, 4097, 100)]
    [DataRow(16, 0, 10, 0)]
    [DataRow(16, 0, 10, 4194305)]
    public void Invalid_options_are_rejected_without_output(int tokens, int overlap, int chunks, int total)
    {
        var result = AiKnowledgeTextChunker.Chunk(Document("有效正文"), Fixture(), "fixture-bpe-v1", new(tokens, overlap, chunks, total), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidOptions, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("whitespace")]
    [DataRow("nul")]
    [DataRow("high-surrogate")]
    [DataRow("low-surrogate")]
    [DataRow("too-long")]
    [DataRow("digest")]
    [DataRow("size")]
    public void Invalid_document_never_produces_chunks(string scenario)
    {
        var document = scenario switch
        {
            "empty" => Document(""), "whitespace" => Document(" \t\r\n"), "nul" => Document("原文\0"),
            "high-surrogate" => Document("\uD800"), "low-surrogate" => Document("\uDC00"),
            "too-long" => Document(new string('x', AiKnowledgeTextParser.MaxOutputCharacters + 1)),
            "digest" => Document("原文") with { ContentHash = "invalid" },
            _ => Document("原文") with { SizeBytes = 0 }
        };
        var result = AiKnowledgeTextChunker.Chunk(document, Fixture(), "fixture-bpe-v1", new(), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidDocument, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("unknown version")]
    [DataRow("version\n")]
    public void Invalid_tokenizer_identity_is_rejected(string version)
    {
        var result = AiKnowledgeTextChunker.Chunk(Document("有效正文"), Fixture(), version, new(), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidTokenProfile, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Normalization_or_special_control_tokens_are_rejected(bool specialTokens)
    {
        var tokenizer = Fixture(normalizer: specialTokens ? null : LowerCaseNormalizer.Instance,
            special: specialTokens ? new Dictionary<string, int> { ["<x>"] = 100000 } : null);
        var result = AiKnowledgeTextChunker.Chunk(Document("<x> HELLO 原文"), tokenizer, "fixture-bpe-v1", new(), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidTokenProfile, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Precancelled_request_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeTextChunker.Chunk(Document("取消"), Fixture(), "fixture-bpe-v1", new(), cancellation.Token));
    }

    [TestMethod]
    public void Cancellation_during_tokenizer_call_is_observed_after_the_call_returns()
    {
        using var cancellation = new CancellationTokenSource();
        var tokenizer = Fixture(preTokenizer: new ProbePreTokenizer(_ => cancellation.Cancel()));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeTextChunker.Chunk(Document("取消解析"), tokenizer, "fixture-bpe-v1", new(), cancellation.Token));
    }

    [TestMethod]
    public void Tokenizer_exception_is_propagated_instead_of_becoming_partial_success()
    {
        var failure = new InvalidOperationException("Synthetic tokenizer failure");
        var tokenizer = Fixture(preTokenizer: new ProbePreTokenizer(_ => throw failure));
        var actual = Assert.ThrowsExactly<InvalidOperationException>(() => AiKnowledgeTextChunker.Chunk(Document("有效正文"), tokenizer, "fixture-bpe-v1", new(), CancellationToken.None));
        Assert.AreSame(failure, actual);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static AiKnowledgeParsedText Document(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new(text, Hash(bytes), bytes.Length);
    }

    private static TiktokenTokenizer Fixture(Normalizer? normalizer = null, IReadOnlyDictionary<string, int>? special = null, PreTokenizer? preTokenizer = null, int crossingMerge = 0)
    {
        // 自建字节词表及少量合并，既能验证多字节字符，也能证明 Token 数不同于字符数；不下载或复制生产词表。
        var vocabulary = new StringBuilder();
        for (var value = 0; value < 256; value++) vocabulary.Append(Convert.ToBase64String([(byte)value])).Append(' ').Append(value).Append('\n');
        var rank = 256;
        var additions = new List<byte[]>();
        if (crossingMerge == 1) additions.AddRange([[0x61, 0xc3], [0xc3, 0xa9]]);
        if (crossingMerge == 2) additions.AddRange([[0xa9, 0x61], [0xc3, 0xa9, 0x61], [0xc3, 0xa9, 0x61, 0xc3], [0xa9, 0x62], [0xc3, 0xa9, 0x62]]);
        if (crossingMerge == 3)
        {
            for (var length = 2; length <= 128; length *= 2) additions.Add(Encoding.UTF8.GetBytes(new string('a', length)));
            var suffix = new byte[] { 0xa9 }.Concat(Encoding.UTF8.GetBytes(new string('a', 128))).ToArray();
            additions.Add(suffix);
            additions.Add(new byte[] { 0xc3 }.Concat(suffix).ToArray());
            additions.Add(new byte[] { 0xc3 }.Concat(suffix).Concat(new byte[] { 0xc3 }).ToArray());
            additions.AddRange([[0xa9, 0x62], [0xc3, 0xa9, 0x62]]);
        }
        additions.AddRange(new[] { "he", "ll", "hell", "hello", "aa", "aaaa", "aaaaaaaa", "aaaaaaaaaaaaaaaa", new string('a', 32), new string('a', 64) }.Select(Encoding.UTF8.GetBytes));
        foreach (var encoded in additions.Select(Convert.ToBase64String).Distinct())
            vocabulary.Append(encoded).Append(' ').Append(rank++).Append('\n');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(vocabulary.ToString()));
        return TiktokenTokenizer.Create(stream, preTokenizer, normalizer, special, cacheSize: 32);
    }

    // 观察真实 SDK 收到的窗口，在调用中触发取消或失败，但不替代 BPE 计数算法。
    private sealed class ProbePreTokenizer(Action<int> observe) : PreTokenizer
    {
        public override IEnumerable<(int Offset, int Length)> PreTokenize(string text) => Observe(text.Length);
        public override IEnumerable<(int Offset, int Length)> PreTokenize(ReadOnlySpan<char> text) => Observe(text.Length);
        private IEnumerable<(int Offset, int Length)> Observe(int length)
        {
            observe(length);
            return [(0, length)];
        }
    }

    private static void AssertCovered(AiKnowledgeParsedText document, TiktokenTokenizer tokenizer, AiKnowledgeChunkOptions options, AiKnowledgeChunkResult result)
    {
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        Assert.AreEqual(AiKnowledgeChunkFailure.None, result.Failure);
        Assert.IsNotNull(result.Output);
        var covered = new bool[document.Text.Length];
        var previousEnd = 0;
        var previousOffset = -1;
        var sequence = 0;
        foreach (var chunk in result.Output.Chunks)
        {
            Assert.AreEqual(sequence++, chunk.Sequence);
            Assert.IsTrue(chunk.Offset > previousOffset && chunk.Offset <= previousEnd);
            Assert.IsTrue(chunk.Offset + chunk.Length > previousEnd);
            Assert.IsTrue(chunk.Length > 0 && chunk.Length <= AiKnowledgeTextChunker.MaxWindowCharacters);
            Assert.AreEqual(document.Text.Substring(chunk.Offset, chunk.Length), chunk.Text);
            Assert.IsFalse(char.IsLowSurrogate(chunk.Text[0]) || char.IsHighSurrogate(chunk.Text[^1]));
            Assert.IsFalse(chunk.Offset > 0 && document.Text[chunk.Offset - 1] == '\r' && chunk.Text[0] == '\n');
            var end = chunk.Offset + chunk.Length;
            Assert.IsFalse(end < document.Text.Length && document.Text[end - 1] == '\r' && document.Text[end] == '\n');
            Assert.AreEqual(tokenizer.CountTokens(chunk.Text, considerNormalization: false), chunk.TokenCount);
            Assert.IsTrue(chunk.TokenCount > 0 && chunk.TokenCount <= options.MaxTokens);
            if (chunk.Offset < previousEnd) Assert.IsTrue(tokenizer.CountTokens(document.Text.AsSpan(chunk.Offset, previousEnd - chunk.Offset), considerNormalization: false) <= options.OverlapTokens);
            Array.Fill(covered, true, chunk.Offset, chunk.Length);
            previousOffset = chunk.Offset;
            previousEnd = end;
        }
        Assert.IsTrue(covered.All(value => value));
        Assert.IsTrue(result.Output.Chunks.Count <= options.MaxChunks);
        Assert.IsTrue(result.Output.Chunks.Sum(chunk => chunk.TokenCount) <= options.MaxTotalTokens);
        Assert.IsTrue(result.Output.Chunks.Sum(chunk => chunk.Length) <= AiKnowledgeTextChunker.MaxTotalCharacters);
    }
}

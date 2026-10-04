using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;
using Microsoft.ML.Tokenizers;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeStructuredChunkingTests
{
    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    [DataRow("\r")]
    public void Short_paragraphs_keep_exact_blank_lines_and_source_positions(string newline)
    {
        var text = "first" + newline + newline + "second" + newline + "line" + newline + newline + "third";
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0), AiKnowledgeTextFormat.PlainText);
        AssertCovered(text, tokenizer, new(128, 0), result);
        CollectionAssert.AreEqual(new[] { "first" + newline + newline, "second" + newline + "line" + newline + newline, "third" }, result.Output!.Chunks.Select(chunk => chunk.Text).ToArray());
        Assert.IsTrue(result.Output.Chunks.All(chunk => chunk.Context.Count == 0));
    }

    [TestMethod]
    public void Heading_hierarchy_follows_source_order_and_does_not_leak_into_sibling_sections()
    {
        const string text = "# A\none\n\n## B\n中文\n\n## C\nthree\n\n# D\nfour";
        var tokenizer = Fixture();
        var options = new AiKnowledgeChunkOptions(256, 0);
        var result = Chunk(text, tokenizer, options);
        AssertCovered(text, tokenizer, options, result);
        Assert.AreEqual(4, result.Output!.Chunks.Count);
        var chunks = result.Output.Chunks;
        CollectionAssert.AreEqual(new[] { 1, 2 }, chunks[1].Context.Select(context => context.Level).ToArray());
        Assert.AreEqual("# A\n", ContextText(text, chunks[1].Context[0]));
        Assert.AreEqual("## B\n", ContextText(text, chunks[1].Context[1]));
        Assert.AreEqual("# A\n## B\n中文\n\n", chunks[1].EmbeddingText);
        CollectionAssert.AreEqual(new[] { "# A\n", "## C\n" }, chunks[2].Context.Select(context => ContextText(text, context)).ToArray());
        CollectionAssert.AreEqual(new[] { "# D\n" }, chunks[3].Context.Select(context => ContextText(text, context)).ToArray());
        Assert.AreEqual(0, chunks[3].RepeatedContext.Count);
        Assert.AreEqual("structure-window-v1", result.Output.ChunkingVersion);
        Assert.AreEqual("fixture-bpe-v1", result.Output.TokenizerVersion);
        Assert.AreEqual(Document(text).ContentHash, result.Output.ContentHash);
        Assert.AreEqual("utf8-text-v1", result.Output.ParserVersion);
        Assert.AreEqual(AiKnowledgeTextFormat.Markdown, result.Output.Format);
    }

    [TestMethod]
    public void Heading_stays_with_its_first_short_paragraph_across_blank_lines()
    {
        const string text = "# Title\r\n\r\nfirst\r\n\r\nsecond";
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0));
        AssertCovered(text, tokenizer, new(128, 0), result);
        Assert.AreEqual(2, result.Output!.Chunks.Count);
        Assert.AreEqual("# Title\r\n\r\nfirst\r\n\r\n", result.Output.Chunks[0].Text);
        Assert.AreEqual("# Title\r\nsecond", result.Output.Chunks[1].EmbeddingText);
    }

    [TestMethod]
    public void Plain_text_does_not_interpret_heading_or_table_syntax()
    {
        const string text = "# Literal\n\n| N | V |\n| --- | --- |\n| x | y |";
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(256, 0), AiKnowledgeTextFormat.PlainText);
        AssertCovered(text, tokenizer, new(256, 0), result);
        Assert.IsTrue(result.Output!.Chunks.All(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Paragraph && chunk.Context.Count == 0 && chunk.EmbeddingText == chunk.Text));
    }

    [TestMethod]
    public void Long_table_continuations_repeat_original_header_and_charge_complete_embedding_text()
    {
        const string header = "| N | V |\r\n| --- | --- |\r\n";
        var text = "# T\r\n\r\n" + header + string.Concat(Enumerable.Repeat("| bbb | ccc |\r\n", 20));
        var tokenizer = Fixture();
        var options = new AiKnowledgeChunkOptions(96, 0);
        var result = Chunk(text, tokenizer, options);
        AssertCovered(text, tokenizer, options, result);
        var tables = result.Output!.Chunks.Where(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Table).ToArray();
        Assert.IsTrue(tables.Length > 1);
        Assert.IsTrue(tables.All(chunk => chunk.Text.EndsWith("\r\n", StringComparison.Ordinal)));
        Assert.AreEqual(header, ContextText(text, tables[0].Context.Single(context => context.Kind == AiKnowledgeContextKind.TableHeader)));
        Assert.IsFalse(tables[0].RepeatedContext.Any(context => context.Kind == AiKnowledgeContextKind.TableHeader));
        foreach (var chunk in tables.Skip(1))
        {
            Assert.AreEqual("# T\r\n" + header + chunk.Text, chunk.EmbeddingText);
            Assert.IsTrue(chunk.RepeatedContext.Any(context => context.Kind == AiKnowledgeContextKind.TableHeader));
        }
    }

    [TestMethod]
    [DataRow("| f\\|oo | b |\n| :-: | -: |\n| x\\|y | z |\n")]
    [DataRow("f\\|oo | b\n--- | :---:\nx | y | extra\n")]
    public void Escaped_pipes_and_alignment_do_not_change_source_or_header_columns(string text)
    {
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0));
        AssertCovered(text, tokenizer, new(128, 0), result);
        Assert.IsTrue(result.Output!.Chunks.All(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Table));
    }

    [TestMethod]
    [DataRow("| a | b |\n| --- |\n| x | y |")]
    [DataRow("| a | b |\n| --x | --- |\n| x | y |")]
    [DataRow("    | a | b |\n    | --- | --- |\n    | x | y |")]
    public void Unsupported_table_shape_stays_raw_without_inventing_a_header(string text)
    {
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0));
        AssertCovered(text, tokenizer, new(128, 0), result);
        Assert.IsFalse(result.Output!.Chunks.Any(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Table || chunk.Context.Any(context => context.Kind == AiKnowledgeContextKind.TableHeader)));
    }

    [TestMethod]
    [DataRow("```", "```")]
    [DataRow("~~~~", "~~~~~")]
    [DataRow("````", "")]
    public void Fenced_code_keeps_fake_headings_and_tables_out_of_context(string opening, string closing)
    {
        var text = "# Real\n\n" + opening + "\n# Fake\n\n| a | b |\n| --- | --- |\n| x | y |\n" + closing;
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0));
        AssertCovered(text, tokenizer, new(128, 0), result);
        Assert.IsTrue(result.Output!.Chunks.Any(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Code));
        Assert.IsFalse(result.Output.Chunks.Any(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Table));
        Assert.IsTrue(result.Output.Chunks.SelectMany(chunk => chunk.Context).All(context => context.Kind == AiKnowledgeContextKind.Heading && ContextText(text, context) == "# Real\n"));
    }

    [TestMethod]
    [DataRow("    ")]
    [DataRow("\t")]
    public void Indented_code_does_not_create_heading_context(string indent)
    {
        var text = indent + "# Fake\n" + indent + "| x | y |\n" + indent + "| --- | --- |";
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0));
        AssertCovered(text, tokenizer, new(128, 0), result);
        Assert.IsTrue(result.Output!.Chunks.All(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Code && chunk.Context.Count == 0));
    }

    [TestMethod]
    public void Long_unicode_paragraph_splits_without_normalization_or_gaps_and_keeps_bounded_overlap()
    {
        var text = "# A\r\n" + string.Concat(Enumerable.Repeat("😀中文 e\u0301 é\r\n", 40));
        var tokenizer = Fixture();
        var options = new AiKnowledgeChunkOptions(48, 8);
        var result = Chunk(text, tokenizer, options);
        AssertCovered(text, tokenizer, options, result);
        Assert.IsTrue(result.Output!.Chunks.Count > 1);
        var replay = Chunk(text, tokenizer, options);
        CollectionAssert.AreEqual(result.Output.Chunks.Select(chunk => (chunk.Offset, chunk.Text, chunk.EmbeddingText, chunk.TokenCount)).ToArray(), replay.Output!.Chunks.Select(chunk => (chunk.Offset, chunk.Text, chunk.EmbeddingText, chunk.TokenCount)).ToArray());
    }

    [TestMethod]
    public void Long_table_row_can_split_but_every_continuation_keeps_exact_original_header()
    {
        const string header = "| N | V |\n| - | - |\n";
        var text = header + "| " + new string('b', 160) + " | 😀 |\n";
        var tokenizer = Fixture();
        var options = new AiKnowledgeChunkOptions(48, 0);
        var result = Chunk(text, tokenizer, options);
        AssertCovered(text, tokenizer, options, result);
        Assert.IsTrue(result.Output!.Chunks.Count > 1);
        Assert.IsTrue(result.Output.Chunks.Skip(1).All(chunk => chunk.EmbeddingText == header + chunk.Text));
    }

    [TestMethod]
    public void Heading_only_and_header_only_documents_are_preserved()
    {
        foreach (var text in new[] { "# A", "| N | V |\n| - | - |", "\n\n# A\n\n" })
        {
            var tokenizer = Fixture();
            var result = Chunk(text, tokenizer, new(128, 0));
            AssertCovered(text, tokenizer, new(128, 0), result);
        }
    }

    [TestMethod]
    public void Total_token_budget_charges_repeated_context_and_fails_without_partial_output()
    {
        const string text = "# A\nfirst\n\nsecond\n\nthird";
        var tokenizer = Fixture();
        var accepted = Chunk(text, tokenizer, new(128, 0));
        Assert.IsTrue(accepted.IsSuccess);
        var total = accepted.Output!.Chunks.Sum(chunk => chunk.TokenCount);
        Assert.IsTrue(Chunk(text, tokenizer, new(128, 0, 10, total)).IsSuccess);
        var rejected = Chunk(text, tokenizer, new(128, 0, 10, total - 1));
        Assert.AreEqual(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded, rejected.Failure);
        Assert.IsNull(rejected.Output);
    }

    [TestMethod]
    public void Complete_embedding_token_count_cannot_be_derived_by_subtracting_context_tokens()
    {
        var vocabulary = new StringBuilder();
        for (var value = 0; value < 256; value++) vocabulary.Append(Convert.ToBase64String([(byte)value])).Append(' ').Append(value).Append('\n');
        vocabulary.Append("CsM= 256\nw6k= 257\nw6lh 258\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(vocabulary.ToString()));
        var tokenizer = TiktokenTokenizer.Create(stream, null, null, null, cacheSize: 32);
        Assert.AreEqual(5, tokenizer.CountTokens("# h\n") + tokenizer.CountTokens("éa"));
        Assert.AreEqual(6, tokenizer.CountTokens("# h\néa"));
        const string text = "# h\n\néa";
        var result = Chunk(text, tokenizer, new(5, 0));
        AssertCovered(text, tokenizer, new(5, 0), result);
    }

    [TestMethod]
    public void Tokenizer_never_receives_more_than_the_window_limit_including_context()
    {
        var observed = new List<int>();
        var tokenizer = Fixture(preTokenizer: new AiKnowledgeChunkingTests.ProbePreTokenizer(observed.Add));
        var text = "# A\n" + new string('a', 100000);
        var options = new AiKnowledgeChunkOptions(4096, 0);
        var result = Chunk(text, tokenizer, options);
        AssertCovered(text, tokenizer, options, result);
        Assert.IsTrue(observed.Count > 0 && observed.All(length => length <= AiKnowledgeTextChunker.MaxWindowCharacters));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Oversize_heading_or_table_context_is_rejected_without_truncation(bool table)
    {
        var text = table ? "| " + new string('b', 4100) + " |\n| --- |\n| x |\n" : "# " + new string('b', 4100) + "\ntext";
        var result = Chunk(text, Fixture(), new());
        Assert.AreEqual(AiKnowledgeChunkFailure.ContextTooLarge, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Structure_line_or_block_limit_rejects_all_output(bool blocks)
    {
        var text = blocks ? string.Concat(Enumerable.Repeat("x\n\n", AiKnowledgeStructuredChunker.MaxBlocks + 1)) : new string('\n', AiKnowledgeStructuredChunker.MaxLines) + "x";
        var result = Chunk(text, Fixture(), new(4096, 0, 4096, 4194304));
        Assert.AreEqual(AiKnowledgeChunkFailure.StructureLimitExceeded, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Repeated_context_and_body_allocations_cannot_exceed_the_total_character_budget()
    {
        // 多个短段落触发真实上下文累计放大；避免以反复编码整段大 BPE 文本的耗时替代预算断言。
        var text = "# " + new string('a', 3990) + "\n" + string.Concat(Enumerable.Repeat("b\n\n", 2000));
        var result = Chunk(text, Fixture(), new(100, 0, 4096, 4194304));
        Assert.AreEqual(AiKnowledgeChunkFailure.OutputTooLarge, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Invalid_format_is_rejected_without_guessing_from_text()
    {
        var result = Chunk("# text", Fixture(), new(), (AiKnowledgeTextFormat)42);
        Assert.AreEqual(AiKnowledgeChunkFailure.UnsupportedFormat, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Invalid_document_options_and_token_profile_reuse_original_fail_closed_rules()
    {
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidDocument, AiKnowledgeStructuredChunker.Chunk(Document("x") with { ContentHash = "bad" }, Fixture(), "fixture-bpe-v1", AiKnowledgeTextFormat.Markdown, new(), CancellationToken.None).Failure);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidOptions, Chunk("text", Fixture(), new(16, 5)).Failure);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidTokenProfile, AiKnowledgeStructuredChunker.Chunk(Document("text"), Fixture(normalizer: LowerCaseNormalizer.Instance), "fixture-bpe-v1", AiKnowledgeTextFormat.Markdown, new(), CancellationToken.None).Failure);
    }

    [TestMethod]
    public void Cancellation_before_or_during_tokenization_is_propagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeStructuredChunker.Chunk(Document("text"), Fixture(), "fixture-bpe-v1", AiKnowledgeTextFormat.Markdown, new(), cancellation.Token));
        using var during = new CancellationTokenSource();
        var tokenizer = Fixture(preTokenizer: new AiKnowledgeChunkingTests.ProbePreTokenizer(_ => during.Cancel()));
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeStructuredChunker.Chunk(Document("# A\ntext"), tokenizer, "fixture-bpe-v1", AiKnowledgeTextFormat.Markdown, new(), during.Token));
    }

    [TestMethod]
    public void Tokenizer_exception_is_propagated_without_partial_output()
    {
        var error = new InvalidOperationException("Synthetic failure");
        var tokenizer = Fixture(preTokenizer: new AiKnowledgeChunkingTests.ProbePreTokenizer(_ => throw error));
        Assert.AreSame(error, Assert.ThrowsExactly<InvalidOperationException>(() => Chunk("# A\ntext", tokenizer, new())));
    }

    [TestMethod]
    [DataRow("a | b\\\n--- | ---\nx | y", true)]
    [DataRow("| a | b\\\n| --- |\n| x |", false)]
    public void Trailing_backslash_keeps_the_final_header_cell_in_column_validation(string text, bool table)
    {
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(128, 0));
        AssertCovered(text, tokenizer, new(128, 0), result);
        Assert.AreEqual(table, result.Output!.Chunks.Any(chunk => chunk.Kind == AiKnowledgeTextBlockKind.Table));
    }

    [TestMethod]
    public void Consecutive_same_level_headings_are_bound_to_each_chunks_actual_source()
    {
        const string text = "# A\n# B";
        var tokenizer = Fixture();
        var result = Chunk(text, tokenizer, new(4, 0));
        AssertCovered(text, tokenizer, new(4, 0), result);
        Assert.AreEqual(2, result.Output!.Chunks.Count);
        CollectionAssert.AreEqual(new[] { "# A\n" }, result.Output.Chunks[0].Context.Select(context => ContextText(text, context)).ToArray());
        CollectionAssert.AreEqual(new[] { "# B" }, result.Output.Chunks[1].Context.Select(context => ContextText(text, context)).ToArray());
    }

    [TestMethod]
    public void Context_limit_applies_to_complete_heading_path_not_only_each_heading()
    {
        var text = "# " + new string('b', 2045) + "\n## " + new string('b', 2045) + "\nx";
        var result = Chunk(text, Fixture(), new());
        Assert.AreEqual(AiKnowledgeChunkFailure.ContextTooLarge, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Global_chunk_limit_rejects_semantic_blocks_without_partial_output()
    {
        Assert.IsTrue(Chunk("one\n\ntwo", Fixture(), new(16, 0, 2)).IsSuccess);
        var result = Chunk("one\n\ntwo", Fixture(), new(16, 0, 1));
        Assert.AreEqual(AiKnowledgeChunkFailure.TooManyChunks, result.Failure);
        Assert.IsNull(result.Output);
    }

    [TestMethod]
    public void Exact_line_limit_is_accepted_without_losing_leading_blank_source()
    {
        var text = new string('\n', AiKnowledgeStructuredChunker.MaxLines - 1) + "x";
        var tokenizer = Fixture();
        var options = new AiKnowledgeChunkOptions(4096, 0);
        AssertCovered(text, tokenizer, options, Chunk(text, tokenizer, options));
    }

    private static TiktokenTokenizer Fixture(Normalizer? normalizer = null, PreTokenizer? preTokenizer = null)
        => AiKnowledgeChunkingTests.Fixture(normalizer: normalizer, preTokenizer: preTokenizer);
    private static AiKnowledgeParsedText Document(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new(text, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    }
    private static AiKnowledgeStructuredChunkResult Chunk(string text, TiktokenTokenizer tokenizer, AiKnowledgeChunkOptions options, AiKnowledgeTextFormat format = AiKnowledgeTextFormat.Markdown)
        => AiKnowledgeStructuredChunker.Chunk(Document(text), tokenizer, "fixture-bpe-v1", format, options, CancellationToken.None);
    private static string ContextText(string text, AiKnowledgeSourceContext context) => text.Substring(context.Offset, context.Length);

    private static void AssertCovered(string text, TiktokenTokenizer tokenizer, AiKnowledgeChunkOptions options, AiKnowledgeStructuredChunkResult result)
    {
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        Assert.IsNotNull(result.Output);
        Assert.AreEqual(AiKnowledgeChunkFailure.None, result.Failure);
        var coveredEnd = 0;
        var previousOffset = -1;
        var sequence = 0;
        var chargedCharacters = 0;
        foreach (var chunk in result.Output.Chunks)
        {
            Assert.AreEqual(sequence++, chunk.Sequence);
            Assert.IsTrue(chunk.Offset > previousOffset && chunk.Offset <= coveredEnd && chunk.Offset + chunk.Length > coveredEnd);
            Assert.AreEqual(text.Substring(chunk.Offset, chunk.Length), chunk.Text);
            Assert.AreEqual(string.Concat(chunk.RepeatedContext.Select(context => ContextText(text, context))) + chunk.Text, chunk.EmbeddingText);
            Assert.IsTrue(chunk.RepeatedContext.All(context => context.Offset + context.Length <= chunk.Offset && chunk.Context.Contains(context)));
            Assert.IsTrue(chunk.Context.Sum(context => context.Length) <= AiKnowledgeStructuredChunker.MaxContextCharacters);
            Assert.AreEqual(tokenizer.CountTokens(chunk.EmbeddingText, considerNormalization: false), chunk.TokenCount);
            Assert.IsTrue(chunk.TokenCount > 0 && chunk.TokenCount <= options.MaxTokens);
            Assert.IsTrue(chunk.EmbeddingText.Length <= AiKnowledgeTextChunker.MaxWindowCharacters);
            Assert.IsFalse(char.IsLowSurrogate(chunk.Text[0]) || char.IsHighSurrogate(chunk.Text[^1]));
            Assert.IsFalse(chunk.Offset > 0 && text[chunk.Offset - 1] == '\r' && chunk.Text[0] == '\n');
            var end = chunk.Offset + chunk.Length;
            Assert.IsFalse(end < text.Length && text[end - 1] == '\r' && text[end] == '\n');
            if (chunk.Offset < coveredEnd) Assert.IsTrue(tokenizer.CountTokens(text.AsSpan(chunk.Offset, coveredEnd - chunk.Offset), considerNormalization: false) <= options.OverlapTokens);
            chargedCharacters += chunk.Text.Length + (ReferenceEquals(chunk.Text, chunk.EmbeddingText) ? 0 : chunk.EmbeddingText.Length);
            previousOffset = chunk.Offset;
            coveredEnd = end;
        }
        Assert.AreEqual(text.Length, coveredEnd);
        Assert.IsTrue(result.Output.Chunks.Count <= options.MaxChunks);
        Assert.IsTrue(result.Output.Chunks.Sum(chunk => chunk.TokenCount) <= options.MaxTotalTokens);
        Assert.IsTrue(chargedCharacters <= AiKnowledgeTextChunker.MaxTotalCharacters);
    }
}

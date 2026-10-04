using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Microsoft.ML.Tokenizers;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiKnowledgeModelChunkingTests
{
    private static readonly Guid ModelId = Guid.CreateVersion7();
    private static readonly Guid KnowledgeId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Lazy<TiktokenTokenizer> Tokenizer = new(() =>
    {
        using var source = Resource();
        return AiKnowledgeCl100kTokenizer.Create(source, CancellationToken.None);
    });

    // 独立 tiktoken 0.12.0 的 encode_ordinary 参考值；不能由被测 SDK 在测试中生成期望。
    [TestMethod]
    [DataRow("hello world", new int[] {15339, 1917})]
    [DataRow("tiktoken is great!", new int[] {83, 1609, 5963, 374, 2294, 0})]
    [DataRow("中文制度编号 FN-DEMO-042", new int[] {16325, 17161, 44416, 27479, 73740, 50587, 12, 1170, 10754, 12, 22349})]
    [DataRow("😀😃 e\u0301 é", new int[] {76460, 222, 76460, 225, 384, 54939, 4046})]
    [DataRow("I'm WE'LL they're isn't", new int[] {40, 2846, 20255, 6, 4178, 814, 2351, 4536, 956})]
    [DataRow("a\r\nb\rc\nd", new int[] {64, 319, 65, 201, 66, 198, 67})]
    [DataRow("<|endoftext|><|fim_prefix|>", new int[] {27, 91, 8862, 728, 428, 91, 1822, 91, 69, 318, 14301, 91, 29})]
    [DataRow("1234567890  00001", new int[] {4513, 10961, 16474, 15, 220, 220, 931, 1721})]
    [DataRow("العربية Ελληνικά हिन्दी", new int[] {32482, 24102, 11318, 22071, 74541, 8008, 243, 34586, 34586, 42524, 34369, 30862, 68437, 75234, 85410, 43411, 101, 31584, 99, 44747})]
    [DataRow("日本語 한국어", new int[] {9080, 22656, 45918, 252, 62398, 89059, 255, 32179})]
    [DataRow("İ ı I i", new int[] {48880, 220, 3862, 358, 602})]
    [DataRow("a\t  b\n\n", new int[] {64, 3762, 293, 271})]
    [DataRow("# 标题\n\n正文", new int[] {2, 52561, 229, 34972, 271, 37656, 17161})]
    [DataRow("| A | B |\n| --- | --- |\n| 中文 | 😀 |", new int[] {91, 362, 765, 426, 9432, 91, 12730, 765, 12730, 9432, 91, 73958, 17161, 765, 91416, 765})]
    [DataRow("café naïve résumé", new int[] {936, 59958, 95980, 588, 9517, 1264, 978})]
    [DataRow("https://example.invalid/path?a=b#c", new int[] {2485, 1129, 8858, 56821, 52076, 99561, 22861, 93353})]
    public void Offline_tokenizer_matches_independent_ordinary_token_ids(string text, int[] expected)
    {
        CollectionAssert.AreEqual(expected, Tokenizer.Value.EncodeToIds(text).ToArray());
        Assert.AreEqual(expected.Length, Tokenizer.Value.CountTokens(text));
        Assert.IsNull(Tokenizer.Value.Normalizer);
        Assert.IsTrue(Tokenizer.Value.SpecialTokens is null or { Count: 0 });
    }

    [TestMethod]
    [DataRow("text-embedding-3-small")]
    [DataRow("text-embedding-3-large")]
    public void Approved_exact_model_uses_real_tokens_and_preserves_configuration_identity(string modelName)
    {
        var result = Chunk(model: Model(modelName: modelName));
        Assert.IsTrue(result.IsSuccess);
        var output = result.Output!;
        Assert.AreEqual(KnowledgeId, output.KnowledgeBaseId);
        Assert.AreEqual(2, output.KnowledgeBaseVersion);
        Assert.IsNull(output.TenantId);
        Assert.AreEqual(ModelId, output.ModelConfigId);
        Assert.AreEqual(3, output.ModelConfigVersion);
        Assert.AreEqual("openai_compatible", output.ProviderKey);
        Assert.AreEqual(modelName, output.ModelId);
        Assert.AreEqual("hello world", output.Chunks.Chunks.Single().Text);
        Assert.AreEqual(2, output.Chunks.Chunks.Single().TokenCount);
        Assert.AreEqual(AiKnowledgeCl100kTokenizer.Version, output.Chunks.TokenizerVersion);
        StringAssert.Contains(output.Chunks.TokenizerVersion, "91423dd5a6f4f288c81534b7608e52cc6d54bc7a4bb522b31e9ed9a9b87061be");
        Assert.AreEqual("structure-window-v1", output.Chunks.ChunkingVersion);
        Assert.AreEqual(Document("hello world").ContentHash, output.Chunks.ContentHash);
    }

    [TestMethod]
    [DataRow("gpt-4o")]
    [DataRow("text-embedding-ada-002")]
    [DataRow("Text-embedding-3-small")]
    [DataRow("text-embedding-3-small-custom")]
    [DataRow("my-embedding-deployment")]
    [DataRow("")]
    [DataRow(" text-embedding-3-small")]
    [DataRow("text-embedding-3-large ")]
    public void Unknown_or_aliased_model_is_rejected_without_a_default(string name)
        => AssertRejected(Chunk(model: Model(modelName: name)), AiKnowledgeModelChunkFailure.UnsupportedModel);

    [TestMethod]
    [DataRow("ollama")]
    [DataRow("azure_openai")]
    [DataRow("Openai_compatible")]
    public void Provider_protocol_name_does_not_imply_a_tokenizer_mapping(string provider)
        => AssertRejected(Chunk(model: Model(provider: provider)), AiKnowledgeModelChunkFailure.UnsupportedModel);

    [TestMethod]
    [DataRow("https://example.invalid/v1")]
    [DataRow("http://api.openai.com/v1")]
    [DataRow("https://api.openai.com:444/v1")]
    [DataRow("https://api.openai.com.evil.invalid/v1")]
    [DataRow("https://user@api.openai.com/v1")]
    [DataRow("https://api.openai.com/v1?q=1")]
    [DataRow("https://api.openai.com/v1#fragment")]
    [DataRow("https://api.openai.com/v1/other")]
    [DataRow("https://api.openai.com/%76%31")]
    [DataRow("https://api.openai.com/v1/../v1")]
    [DataRow(@"https://api.openai.com\v1")]
    [DataRow("not-a-uri")]
    public void Gateway_or_noncanonical_destination_is_not_treated_as_the_official_model(string endpoint)
        => AssertRejected(Chunk(model: Model(endpoint: endpoint)), AiKnowledgeModelChunkFailure.UnsupportedModel);

    [TestMethod]
    [DataRow("https://api.openai.com/v1/")]
    [DataRow("https://API.OPENAI.COM:443/v1")]
    public void Equivalent_official_destination_is_supported(string endpoint)
        => Assert.IsTrue(Chunk(model: Model(endpoint: endpoint)).IsSuccess);

    [TestMethod]
    public void Disabled_stale_or_unapproved_configuration_does_not_produce_chunks()
    {
        foreach (var knowledge in new[] { Knowledge(enabled: false), Knowledge(approvedId: Guid.Empty), Knowledge(approvedId: Guid.CreateVersion7()), Knowledge(approvedVersion: 2), Knowledge(approvedVersion: 0), Knowledge(approved: false) })
            AssertRejected(Chunk(knowledge: knowledge), AiKnowledgeModelChunkFailure.ModelNotApproved);
        AssertRejected(Chunk(model: Model(enabled: false)), AiKnowledgeModelChunkFailure.ModelNotApproved);
        AssertRejected(Chunk(model: Model(version: 0)), AiKnowledgeModelChunkFailure.ModelNotApproved);
        AssertRejected(Chunk(model: Model(id: Guid.Empty)), AiKnowledgeModelChunkFailure.ModelNotApproved);
    }

    [TestMethod]
    public void Tenant_configuration_cannot_cross_scope_but_host_configuration_can_be_explicitly_approved()
    {
        AssertRejected(Chunk(model: Model(tenant: TenantId)), AiKnowledgeModelChunkFailure.ModelScopeMismatch);
        AssertRejected(Chunk(knowledge: Knowledge(tenant: TenantId), model: Model(tenant: Guid.CreateVersion7())), AiKnowledgeModelChunkFailure.ModelScopeMismatch);
        AssertRejected(Chunk(knowledge: Knowledge(tenant: Guid.Empty)), AiKnowledgeModelChunkFailure.ModelScopeMismatch);
        Assert.IsTrue(Chunk(knowledge: Knowledge(tenant: TenantId), model: Model(tenant: TenantId)).IsSuccess);
        var hostModel = Chunk(knowledge: Knowledge(tenant: TenantId), model: Model());
        Assert.IsTrue(hostModel.IsSuccess);
        Assert.AreEqual(TenantId, hostModel.Output!.TenantId);
    }

    [TestMethod]
    public void Binding_does_not_capture_credentials_or_display_metadata()
    {
        var model = new AiModelConfigRecord { Id = ModelId, Version = 3, IsEnabled = true, ModelId = "text-embedding-3-small", ProviderKey = "openai_compatible", EndpointBaseUrl = "https://api.openai.com/v1", ApiKeyProtected = "private-credential-sentinel", Name = "private-display-sentinel" };
        var result = Chunk(model: model);
        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Output!.ToString().Contains("private-", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Real_tokenizer_chunking_covers_unicode_and_charges_the_complete_repeated_context()
    {
        var text = "# 标题\r\n" + string.Concat(Enumerable.Repeat("中文制度 😀 e\u0301 编号042\r\n", 30));
        var result = Chunk(document: Document(text), options: new(32, 4));
        Assert.IsTrue(result.IsSuccess);
        var chunks = result.Output!.Chunks.Chunks;
        Assert.IsTrue(chunks.Count > 1);
        var coveredEnd = 0;
        foreach (var chunk in chunks)
        {
            Assert.IsTrue(chunk.Offset <= coveredEnd && chunk.Offset + chunk.Length > coveredEnd);
            Assert.AreEqual(text.Substring(chunk.Offset, chunk.Length), chunk.Text);
            Assert.AreEqual(Tokenizer.Value.CountTokens(chunk.EmbeddingText), chunk.TokenCount);
            Assert.IsTrue(chunk.TokenCount <= 32);
            Assert.IsFalse(char.IsLowSurrogate(chunk.Text[0]) || char.IsHighSurrogate(chunk.Text[^1]));
            coveredEnd = chunk.Offset + chunk.Length;
        }
        Assert.AreEqual(text.Length, coveredEnd);
        var failed = Chunk(document: Document(text), options: new(32, 4, 4096, 1));
        AssertRejected(failed, AiKnowledgeModelChunkFailure.ChunkingFailed);
        Assert.AreEqual(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded, failed.ChunkFailure);
    }

    [TestMethod]
    public void Bad_document_or_options_preserve_underlying_fail_closed_result()
    {
        var failed = Chunk(document: Document("hello") with { ContentHash = "bad" });
        AssertRejected(failed, AiKnowledgeModelChunkFailure.ChunkingFailed);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidDocument, failed.ChunkFailure);
        Assert.AreEqual(AiKnowledgeChunkFailure.InvalidOptions, Chunk(options: new(0)).ChunkFailure);
    }

    [TestMethod]
    public void Pre_cancelled_call_never_returns_model_chunks()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeModelChunker.Chunk(Knowledge(), Model(), Document("hello"), AiKnowledgeTextFormat.PlainText, new(), cancel.Token));
    }

    [TestMethod]
    public void Regex_model_count_is_independent_of_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.AreEqual(9, Chunk(document: Document("I'm WE'LL they're isn't")).Output!.Chunks.Chunks.Single().TokenCount);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(0)]
    [DataRow(1)]
    public void Truncated_tampered_or_extended_resource_is_rejected(int change)
    {
        using var source = Resource();
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        var data = copy.ToArray();
        if (change < 0) data = data[..^1];
        else if (change > 0) data = [.. data, 0];
        else data[100] ^= 1;
        using var changed = new MemoryStream(data);
        Assert.ThrowsExactly<InvalidDataException>(() => AiKnowledgeCl100kTokenizer.Create(changed, CancellationToken.None));
        Assert.IsTrue(changed.CanRead);
    }

    [TestMethod]
    public void Resource_read_cancellation_and_io_failure_propagate_without_taking_stream_ownership()
    {
        using var cancel = new CancellationTokenSource();
        using var source = Resource();
        using var data = new MemoryStream();
        source.CopyTo(data);
        using var cancelled = new ObservedStream(data.ToArray(), () => cancel.Cancel());
        Assert.ThrowsExactly<OperationCanceledException>(() => AiKnowledgeCl100kTokenizer.Create(cancelled, cancel.Token));
        Assert.IsTrue(cancelled.CanRead);
        var error = new IOException("synthetic resource failure");
        using var broken = new ObservedStream(data.ToArray(), () => throw error);
        Assert.AreSame(error, Assert.ThrowsExactly<IOException>(() => AiKnowledgeCl100kTokenizer.Create(broken, CancellationToken.None)));
    }

    private sealed class ObservedStream(byte[] bytes, Action observe) : MemoryStream(bytes)
    {
        public override int Read(Span<byte> buffer) { observe(); return base.Read(buffer); }
    }
    private static Stream Resource() => typeof(AiKnowledgeParsedText).Assembly.GetManifestResourceStream("Full.NET.Modules.Ai.Knowledge.Tokenizers.cl100k_base.tiktoken.deflate") ?? throw new AssertFailedException("缺少固定词表资源。");
    private static AiKnowledgeModelChunkResult Chunk(AiKnowledgeBaseRecord? knowledge = null, AiModelConfigRecord? model = null, AiKnowledgeParsedText? document = null, AiKnowledgeChunkOptions? options = null)
        => AiKnowledgeModelChunker.Chunk(knowledge ?? Knowledge(), model ?? Model(), document ?? Document("hello world"), AiKnowledgeTextFormat.Markdown, options ?? new(), CancellationToken.None);
    private static AiKnowledgeBaseRecord Knowledge(Guid? tenant = null, bool enabled = true, bool approved = true, Guid? approvedId = null, int approvedVersion = 3)
        => new() { Id = KnowledgeId, TenantId = tenant, Version = 2, IsEnabled = enabled, DataClassification = "internal", EmbeddingModelConfigId = approved ? approvedId ?? ModelId : null, EmbeddingModelVersion = approved ? approvedVersion : null };
    private static AiModelConfigRecord Model(Guid? tenant = null, bool enabled = true, Guid? id = null, int version = 3, string modelName = "text-embedding-3-small", string provider = "openai_compatible", string endpoint = "https://api.openai.com/v1")
        => new() { Id = id ?? ModelId, TenantId = tenant, IsEnabled = enabled, Version = version, ModelId = modelName, ProviderKey = provider, EndpointBaseUrl = endpoint };
    private static AiKnowledgeParsedText Document(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new(text, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    }
    private static void AssertRejected(AiKnowledgeModelChunkResult result, AiKnowledgeModelChunkFailure failure)
    {
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Output);
        Assert.AreEqual(failure, result.Failure);
    }
}

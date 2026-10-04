using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>用真实流验证摘要绑定、有界 UTF-8 文本、原文位置及取消和资源所有权。</summary>
[TestClass]
public sealed class AiKnowledgeTextParserTests
{
    [TestMethod]
    [DataRow("source.txt", "text/plain", "中文制度 FN-DEMO-042：三个工作日。")]
    [DataRow("source.md", "text/markdown", "# 手册\r\n|套餐|上限|\r\n|专业|80|\n```js\nalert('text only');\n```\n[链接](https://example.invalid)")]
    [DataRow("SOURCE.TXT", "text/plain", "😀 首行\r\n第二行\n最后一行")]
    [DataRow("source.txt", "text/plain", "e\u0301 与 é 不归一化\r独立回车")]
    public async Task Text_and_markdown_preserve_original_text_and_digest(string name, string type, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var result = await ParseAsync(bytes, name, type);
        Assert.IsTrue(result.IsSuccess, result.Failure.ToString());
        Assert.IsNotNull(result.Document);
        Assert.AreEqual(text, result.Document.Text);
        Assert.AreEqual(Hash(bytes), result.Document.ContentHash);
        Assert.AreEqual(bytes.Length, result.Document.SizeBytes);
        Assert.AreEqual("utf8-text-v1", result.Document.ParserVersion);
    }

    [TestMethod]
    public async Task Leading_bom_is_removed_but_original_digest_and_utf16_positions_remain_stable()
    {
        const string text = "😀 标题\r\n|中文|值|\n正文\uFEFF内嵌标记";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        var result = await ParseAsync(bytes);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(text, result.Document!.Text);
        Assert.AreEqual(Hash(bytes), result.Document.ContentHash);
        Assert.AreEqual(bytes.Length, result.Document.SizeBytes);
        var offset = text.IndexOf("正文", StringComparison.Ordinal);
        Assert.AreEqual("正文", result.Document.Text.Substring(offset, 2));
        Assert.AreEqual(offset, result.Document.Text.IndexOf("正文", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow("source.pdf", "application/pdf")]
    [DataRow("source.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [DataRow("source.html", "text/html")]
    [DataRow("source.md", "text/plain")]
    [DataRow("source.txt", "text/markdown")]
    [DataRow("../source.txt", "text/plain")]
    [DataRow("C:source.txt", "text/plain")]
    [DataRow("source\n.txt", "text/plain")]
    [DataRow(" source.txt", "text/plain")]
    [DataRow("", "text/plain")]
    public async Task Unsupported_or_unsafe_metadata_is_rejected_before_reading(string name, string type)
    {
        using var source = new ControlledStream(Encoding.UTF8.GetBytes("不能误入文本解析"));
        var result = await AiKnowledgeTextParser.ParseAsync(source, name, type, Hash(source.Bytes), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeTextParseFailure.UnsupportedFormat, result.Failure);
        Assert.IsNull(result.Document);
        Assert.AreEqual(0, source.BytesRead);
        Assert.IsFalse(source.IsDisposed);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("whitespace")]
    [DataRow("bom")]
    [DataRow("utf16")]
    [DataRow("overlong")]
    [DataRow("surrogate")]
    [DataRow("truncated")]
    [DataRow("nul")]
    public async Task Empty_binary_and_invalid_utf8_never_produce_a_document(string scenario)
    {
        byte[] bytes = scenario switch
        {
            "empty" => [], "whitespace" => Encoding.UTF8.GetBytes(" \t\r\n\u00A0"), "bom" => [0xef, 0xbb, 0xbf],
            "utf16" => [0xff, 0xfe, 0x41, 0], "overlong" => [0xc0, 0xaf], "surrogate" => [0xed, 0xa0, 0x80],
            "truncated" => [0xe4, 0xb8], _ => Encoding.UTF8.GetBytes("文本\0二进制")
        };
        var result = await ParseAsync(bytes);
        Assert.AreEqual(scenario is "empty" or "whitespace" or "bom" ? AiKnowledgeTextParseFailure.EmptyText : AiKnowledgeTextParseFailure.InvalidText, result.Failure);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Document);
    }

    [TestMethod]
    [DataRow("different")]
    [DataRow("malformed")]
    [DataRow("missing")]
    public async Task Source_digest_mismatch_or_invalid_digest_never_produces_text(string scenario)
    {
        var bytes = Encoding.UTF8.GetBytes("原文已改变");
        var digest = scenario switch { "different" => Hash(Encoding.UTF8.GetBytes("旧原文")), "malformed" => new string('X', 64), _ => "" };
        using var source = new ControlledStream(bytes);
        var result = await AiKnowledgeTextParser.ParseAsync(source, "source.txt", "text/plain", digest, CancellationToken.None);
        Assert.AreEqual(AiKnowledgeTextParseFailure.SourceMismatch, result.Failure);
        Assert.IsNull(result.Document);
        if (scenario != "different") Assert.AreEqual(0, source.BytesRead);
    }

    [TestMethod]
    public async Task Lowercase_digest_is_accepted_and_output_digest_is_canonical()
    {
        var bytes = Encoding.UTF8.GetBytes("摘要大小写");
        using var source = new MemoryStream(bytes);
        var result = await AiKnowledgeTextParser.ParseAsync(source, "source.txt", "text/plain", Hash(bytes).ToLowerInvariant(), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Hash(bytes), result.Document!.ContentHash);
    }

    [TestMethod]
    public async Task Output_limit_accepts_exact_boundary_and_rejects_overflow_without_truncation()
    {
        var exact = Encoding.UTF8.GetBytes(new string('中', AiKnowledgeTextParser.MaxOutputCharacters));
        var accepted = await ParseAsync(exact);
        Assert.IsTrue(accepted.IsSuccess);
        Assert.AreEqual(AiKnowledgeTextParser.MaxOutputCharacters, accepted.Document!.Text.Length);
        var rejected = await ParseAsync(exact.Concat(new byte[] { (byte)'x' }).ToArray());
        Assert.AreEqual(AiKnowledgeTextParseFailure.OutputTooLarge, rejected.Failure);
        Assert.IsNull(rejected.Document);
    }

    [TestMethod]
    public async Task Nonseekable_actual_input_limit_reads_only_limit_plus_one_and_never_truncates()
    {
        var bytes = new byte[checked((int)AiKnowledgeFilePolicy.MaxBytes + 81920)];
        Array.Fill(bytes, (byte)'a');
        using var source = new ControlledStream(bytes, 81920);
        var result = await AiKnowledgeTextParser.ParseAsync(source, "source.txt", "text/plain", Hash(bytes), CancellationToken.None);
        Assert.AreEqual(AiKnowledgeTextParseFailure.InputTooLarge, result.Failure);
        Assert.AreEqual(AiKnowledgeFilePolicy.MaxBytes + 1, (long)source.BytesRead);
        Assert.IsNull(result.Document);
        Assert.IsFalse(source.IsDisposed);
        var exact = await ParseAsync(bytes.AsSpan(0, (int)AiKnowledgeFilePolicy.MaxBytes).ToArray());
        Assert.AreEqual(AiKnowledgeTextParseFailure.OutputTooLarge, exact.Failure);
    }

    [TestMethod]
    public async Task Short_reads_across_utf8_boundaries_keep_caller_stream_open()
    {
        var bytes = Encoding.UTF8.GetBytes("😀 中文跨读取边界\r\n");
        using var source = new ControlledStream(bytes, 1);
        var result = await AiKnowledgeTextParser.ParseAsync(source, "source.txt", "text/plain", Hash(bytes), CancellationToken.None);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(Encoding.UTF8.GetString(bytes), result.Document!.Text);
        Assert.AreEqual(bytes.Length, source.BytesRead);
        Assert.IsFalse(source.IsDisposed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_before_or_during_read_is_propagated_without_disposing_input(bool duringRead)
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new ControlledStream(Encoding.UTF8.GetBytes("取消解析"), 1);
        if (duringRead) source.AfterRead = cancellation.Cancel;
        else cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => AiKnowledgeTextParser.ParseAsync(
            source, "source.txt", "text/plain", Hash(source.Bytes), cancellation.Token));
        Assert.AreEqual(duringRead ? 1 : 0, source.BytesRead);
        Assert.IsFalse(source.IsDisposed);
    }

    [TestMethod]
    public async Task Io_failure_is_propagated_instead_of_being_reported_as_an_empty_document()
    {
        using var source = new ControlledStream(Encoding.UTF8.GetBytes("读取失败"), 1);
        var failure = new IOException("Synthetic fixture read failure");
        source.ReadFailure = failure;
        var actual = await Assert.ThrowsExactlyAsync<IOException>(() => AiKnowledgeTextParser.ParseAsync(
            source, "source.txt", "text/plain", Hash(source.Bytes), CancellationToken.None));
        Assert.AreSame(failure, actual);
        Assert.IsFalse(source.IsDisposed);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static async Task<AiKnowledgeTextParseResult> ParseAsync(byte[] bytes, string name = "source.txt", string type = "text/plain")
    {
        using var source = new MemoryStream(bytes);
        return await AiKnowledgeTextParser.ParseAsync(source, name, type, Hash(bytes), CancellationToken.None);
    }

    // Length 故意返回错误值且不可 Seek，强制算法检查真实流；取消替身不帮算法自动抛错。
    private sealed class ControlledStream(byte[] bytes, int maximumRead = 7) : Stream
    {
        internal byte[] Bytes => bytes;
        internal int BytesRead { get; private set; }
        internal bool IsDisposed { get; private set; }
        internal Action? AfterRead { get; set; }
        internal Exception? ReadFailure { get; set; }
        public override bool CanRead => !IsDisposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (ReadFailure is { } failure) return ValueTask.FromException<int>(failure);
            var count = Math.Min(Math.Min(maximumRead, buffer.Length), bytes.Length - BytesRead);
            bytes.AsMemory(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            AfterRead?.Invoke();
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

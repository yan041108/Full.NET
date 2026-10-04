using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部文本解析失败分类，尚未作为 HTTP 或持久化协议发布。</summary>
internal enum AiKnowledgeTextParseFailure { None, UnsupportedFormat, SourceMismatch, InputTooLarge, InvalidText, EmptyText, OutputTooLarge }

/// <summary>原始 UTF-8 字节摘要及去首个 BOM 的文本；位置使用该文本的 UTF-16 偏移，不改变换行或 Unicode。</summary>
internal sealed record AiKnowledgeParsedText(string Text, string ContentHash, int SizeBytes)
{
    internal string ParserVersion => "utf8-text-v1";
}

/// <summary>成功结果才持有正文；强类型失败不包含正文、路径或底层异常。</summary>
internal sealed class AiKnowledgeTextParseResult
{
    private AiKnowledgeTextParseResult(AiKnowledgeParsedText? document, AiKnowledgeTextParseFailure failure)
    { Document = document; Failure = failure; }
    internal AiKnowledgeParsedText? Document { get; }
    internal AiKnowledgeTextParseFailure Failure { get; }
    internal bool IsSuccess => Document is not null;
    internal static AiKnowledgeTextParseResult Parsed(AiKnowledgeParsedText document) => new(document, AiKnowledgeTextParseFailure.None);
    internal static AiKnowledgeTextParseResult Rejected(AiKnowledgeTextParseFailure failure) => new(null, failure);
}

/// <summary>只解析摘要匹配的有界 TXT/Markdown 字节；流归调用者，不执行文件名、Markdown 或外链。</summary>
internal static class AiKnowledgeTextParser
{
    internal const int MaxOutputCharacters = 2 * 1024 * 1024;

    /// <summary>保留原文及 UTF-16 位置，失败不返回部分正文；取消与 I/O 异常由调度边界处理。</summary>
    /// <remarks>本算法不授予文件访问或模型外发资格；调用者须先复核权威授权、审批及版本，并负责流读取的时限。</remarks>
    /// <param name="source">来自已授权 Files 引用的流，所有权仍归调用者。</param>
    /// <param name="originalFileName">权威版本保存的原文件名，仅用于格式校验，不用于打开路径。</param>
    /// <param name="contentType">权威版本保存的精确文本媒体类型。</param>
    /// <param name="expectedContentHash">来自不可变来源版本的 SHA256，而非客户端自行声明的摘要。</param>
    /// <param name="cancellationToken">停止有界读取与解析的令牌。</param>
    internal static async Task<AiKnowledgeTextParseResult> ParseAsync(Stream source, string originalFileName, string contentType,
        string expectedContentHash, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        // 文件名只作元数据复核；PDF 不得误入 UTF-8 解码，须由独立隔离进程接入。
        if (string.IsNullOrWhiteSpace(originalFileName) || originalFileName.Length > 255
            || originalFileName != originalFileName.Trim() || originalFileName.Any(char.IsControl)
            || originalFileName.IndexOfAny(['/', '\\', ':']) >= 0
            || (Path.GetExtension(originalFileName).ToLowerInvariant(), contentType) is not
                ((".txt", "text/plain") or (".md", "text/markdown")))
            return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.UnsupportedFormat);
        if (expectedContentHash is null || expectedContentHash.Length != 64 || !expectedContentHash.All(char.IsAsciiHexDigit))
            return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.SourceMismatch);

        using var output = new MemoryStream();
        var bytes = new byte[81920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 最多读取上限加一，不信任 Length；超限不能截断后冒充完整来源。
            var remaining = checked((int)(AiKnowledgeFilePolicy.MaxBytes - output.Length + 1));
            var count = await source.ReadAsync(bytes.AsMemory(0, Math.Min(bytes.Length, remaining)), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (count == 0) break;
            if (count > AiKnowledgeFilePolicy.MaxBytes - output.Length)
                return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.InputTooLarge);
            output.Write(bytes, 0, count);
        }
        var length = checked((int)output.Length);
        var data = output.GetBuffer().AsSpan(0, length);
        var actualHash = Convert.ToHexString(SHA256.HashData(data));
        if (!string.Equals(expectedContentHash, actualHash, StringComparison.OrdinalIgnoreCase))
            return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.SourceMismatch);

        // 摘要覆盖原始字节，只有文本中的首个 BOM 被移除；其余位置、换行和字符不归一化。
        if (data.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) data = data[3..];
        var encoding = new UTF8Encoding(false, true);
        string text;
        try
        {
            // 先检查输出长度再分配字符串，避免合法 UTF-8 绕过文本输出预算。
            if (encoding.GetCharCount(data) > MaxOutputCharacters)
                return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.OutputTooLarge);
            cancellationToken.ThrowIfCancellationRequested();
            text = encoding.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.InvalidText);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (text.Contains('\0')) return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.InvalidText);
        if (string.IsNullOrWhiteSpace(text)) return AiKnowledgeTextParseResult.Rejected(AiKnowledgeTextParseFailure.EmptyText);
        return AiKnowledgeTextParseResult.Parsed(new(text, actualHash, length));
    }
}

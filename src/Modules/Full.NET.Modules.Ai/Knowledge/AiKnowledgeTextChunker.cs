using System.Buffers;
using System.Text;
using Full.NET.Modules.Ai.Domain;
using Microsoft.ML.Tokenizers;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>内部 Token 分块预算；重叠及所有输出均计入总预算。</summary>
internal sealed record AiKnowledgeChunkOptions(int MaxTokens = 512, int OverlapTokens = 64, int MaxChunks = 2048, int MaxTotalTokens = 1048576);

/// <summary>原文中连续 UTF-16 区间及按所选 Tokenizer 重新计算的正文 Token 数。</summary>
internal sealed record AiKnowledgeTextChunk(int Sequence, int Offset, int Length, string Text, int TokenCount);

/// <summary>绑定来源和全部算法/预算输入；尚未作为持久化或 HTTP 协议发布。</summary>
internal sealed record AiKnowledgeTextChunks(string ContentHash, string ParserVersion, string TokenizerVersion,
    AiKnowledgeChunkOptions Options, IReadOnlyList<AiKnowledgeTextChunk> Chunks)
{
    internal string ChunkingVersion => "token-window-v1";
}

/// <summary>内部失败分类；失败不得留下可被误用的部分分块。</summary>
internal enum AiKnowledgeChunkFailure { None, InvalidOptions, InvalidDocument, InvalidTokenProfile, TokenBudgetTooSmall, TooManyChunks, TotalTokenBudgetExceeded, OutputTooLarge, BoundarySearchLimitExceeded }

/// <summary>只有完整成功的分块集合才可供后续索引使用。</summary>
internal sealed record AiKnowledgeChunkResult(AiKnowledgeTextChunks? Output, AiKnowledgeChunkFailure Failure)
{
    internal bool IsSuccess => Output is not null;
    internal static AiKnowledgeChunkResult Rejected(AiKnowledgeChunkFailure failure) => new(null, failure);
}

/// <summary>使用可信词表逐块计数，输出有界且保留原文；不选择模型或授予文件、模型外发权限。</summary>
internal static class AiKnowledgeTextChunker
{
    internal const int MaxWindowCharacters = 32768;
    internal const int MaxTotalCharacters = 3 * AiKnowledgeTextParser.MaxOutputCharacters;

    /// <summary>调用方须绑定已核准的真实模型词表及版本；算法不归一化，不解释特殊控制 Token。</summary>
    /// <remarks>同步 Tokenizer 调用只在调用前后检查取消，不能视为可强制终止。词表可信且窗口有界，不访问词表路径或网络。</remarks>
    /// <param name="document">已授权来源解析出的原文、原始字节摘要和解析版本；分块不重新授予来源资格。</param>
    /// <param name="tokenizer">匹配已核准模型的可信普通文本词表，不得携带归一化或控制 Token 映射。</param>
    /// <param name="tokenizerVersion">须标识实际词表和算法版本，后续索引代次及模型空间应共同保存并复核。</param>
    /// <param name="options">含重叠计费的单块、块数和总 Token 预算，不能在输出后再静默截断。</param>
    /// <param name="cancellationToken">在算法迭代和同步 Tokenizer 调用前后停止处理的令牌。</param>
    internal static AiKnowledgeChunkResult Chunk(AiKnowledgeParsedText document, TiktokenTokenizer tokenizer,
        string tokenizerVersion, AiKnowledgeChunkOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxTokens is < 1 or > 4096 || options.OverlapTokens is < 0 or > 256
            || options.OverlapTokens > options.MaxTokens / 4 || options.MaxChunks is < 1 or > 4096
            || options.MaxTotalTokens is < 1 or > 4194304)
            return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.InvalidOptions);
        // 词表只来自可信调用者，且必须按普通正文处理；不能让归一化破坏位置或让文档触发控制 Token。
        if (string.IsNullOrEmpty(tokenizerVersion) || tokenizerVersion.Length > 128
            || tokenizerVersion.Any(value => !char.IsAsciiLetterOrDigit(value) && value is not ('.' or '-' or '_' or ':' or '/'))
            || tokenizer.Normalizer is not null || tokenizer.SpecialTokens is { Count: > 0 })
            return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.InvalidTokenProfile);
        if (!ValidDocument(document, cancellationToken))
            return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.InvalidDocument);

        var text = document.Text;
        var chunks = new List<AiKnowledgeTextChunk>();
        var offset = 0;
        var coveredEnd = 0;
        var totalTokens = 0;
        var totalCharacters = 0;
        while (offset < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunks.Count >= options.MaxChunks)
                return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.TooManyChunks);
            var end = ChunkEnd(text, offset, tokenizer, options.MaxTokens, cancellationToken, out var tokenCount, out var boundaryFailure);
            // 重叠可能占满短窗口；丢弃该次重叠后重算，禁止重复输出或漏掉新字符。
            if (end <= coveredEnd)
            {
                if (offset < coveredEnd) { offset = coveredEnd; continue; }
                return AiKnowledgeChunkResult.Rejected(boundaryFailure);
            }
            if (tokenCount <= 0)
                return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.InvalidTokenProfile);
            if (tokenCount > options.MaxTotalTokens - totalTokens)
                return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.TotalTokenBudgetExceeded);
            var length = end - offset;
            // 总正文单独有界，防止少量超长 Token 或重复后缀绕过 Token 预算造成放大分配。
            if (length > MaxTotalCharacters - totalCharacters)
                return AiKnowledgeChunkResult.Rejected(AiKnowledgeChunkFailure.OutputTooLarge);
            totalTokens += tokenCount;
            totalCharacters += length;
            chunks.Add(new(chunks.Count, offset, length, text.Substring(offset, length), tokenCount));
            coveredEnd = end;
            if (end == text.Length) break;
            offset = OverlapStart(text, offset, end, tokenizer, options.OverlapTokens, cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(document.ContentHash, document.ParserVersion, tokenizerVersion, options,
            Array.AsReadOnly(chunks.ToArray())), AiKnowledgeChunkFailure.None);
    }

    private static bool ValidDocument(AiKnowledgeParsedText document, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(document.Text) || document.Text.Length > AiKnowledgeTextParser.MaxOutputCharacters
            || document.Text.Contains('\0') || document.ContentHash is null || document.ContentHash.Length != 64
            || !document.ContentHash.All(char.IsAsciiHexDigit) || document.SizeBytes <= 0 || document.SizeBytes > AiKnowledgeFilePolicy.MaxBytes)
            return false;
        // 内部类型也不能带非法 UTF-16；模型计数器的替换字符不得伪装成来源原文。
        for (var position = 0; position < document.Text.Length;)
        {
            if (position % 4096 == 0) token.ThrowIfCancellationRequested();
            if (Rune.DecodeFromUtf16(document.Text.AsSpan(position), out _, out var consumed) != OperationStatus.Done) return false;
            position += consumed;
        }
        token.ThrowIfCancellationRequested();
        return true;
    }

    private static int ChunkEnd(string text, int offset, TiktokenTokenizer tokenizer, int budget, CancellationToken token,
        out int count, out AiKnowledgeChunkFailure failure)
    {
        count = 0;
        failure = AiKnowledgeChunkFailure.TokenBudgetTooSmall;
        var windowEnd = FloorBoundary(text, Math.Min(text.Length, offset + MaxWindowCharacters));
        token.ThrowIfCancellationRequested();
        var relative = tokenizer.GetIndexByTokenCount(text.AsSpan(offset, windowEnd - offset), budget, out _, out _, considerNormalization: false);
        token.ThrowIfCancellationRequested();
        if (relative < 0 || relative > windowEnd - offset) { failure = AiKnowledgeChunkFailure.InvalidTokenProfile; return offset; }
        var end = FloorBoundary(text, offset + relative);
        if (end < text.Length) end = PreferredLineEnd(text, offset, end);
        // 前缀索引、换行及 Unicode 修边都会改变 BPE 合并；逐块重算，不能假定计数随字符长度单调。
        for (var attempt = 0; end > offset && attempt < 128; attempt++)
        {
            token.ThrowIfCancellationRequested();
            count = tokenizer.CountTokens(text.AsSpan(offset, end - offset), considerNormalization: false);
            token.ThrowIfCancellationRequested();
            if (count <= budget) { failure = AiKnowledgeChunkFailure.None; return end; }
            end = FloorBoundary(text, end - 1);
        }
        // SDK 整窗 Token 可跨字节边界，首单位超限也不能推导更长前缀超限；只探测有界完整前缀并逐个实测。
        var candidate = offset;
        for (var attempt = 0; candidate < windowEnd && attempt < 128; attempt++)
        {
            candidate = CeilingBoundary(text, candidate + 1);
            token.ThrowIfCancellationRequested();
            count = tokenizer.CountTokens(text.AsSpan(offset, candidate - offset), considerNormalization: false);
            token.ThrowIfCancellationRequested();
            if (count <= budget) { failure = AiKnowledgeChunkFailure.None; return candidate; }
        }
        // 搜索预算耗尽属于算法资源限制，不得误报实际不存在合法分块，也不返回已有部分输出。
        if (candidate < windowEnd) failure = AiKnowledgeChunkFailure.BoundarySearchLimitExceeded;
        return offset;
    }

    private static int OverlapStart(string text, int start, int end, TiktokenTokenizer tokenizer, int budget, CancellationToken token)
    {
        if (budget == 0) return end;
        token.ThrowIfCancellationRequested();
        var relative = tokenizer.GetIndexByTokenCountFromEnd(text.AsSpan(start, end - start), budget, out _, out _, considerNormalization: false);
        token.ThrowIfCancellationRequested();
        if (relative <= 0 || relative > end - start) return end;
        var next = CeilingBoundary(text, start + relative);
        // 后缀也独立重新计数；允许减少重叠，不能超过上限或为了重叠停止前进。
        for (var attempt = 0; next < end && attempt < 128; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var count = tokenizer.CountTokens(text.AsSpan(next, end - next), considerNormalization: false);
            token.ThrowIfCancellationRequested();
            if (count <= budget) return next;
            next = CeilingBoundary(text, next + 1);
        }
        return end;
    }

    private static int PreferredLineEnd(string text, int start, int end)
    {
        for (var position = end - 1; position >= start + (end - start) / 2; position--)
            if (text[position] == '\n' || (text[position] == '\r' && (position + 1 == text.Length || text[position + 1] != '\n')))
                return position + 1;
        return end;
    }

    // UTF-16 对与 CRLF 均作为不可拆边界；组合字符保留原码位，不进行 Unicode 归一化。
    private static int FloorBoundary(string text, int position)
    {
        if (position > 0 && position < text.Length && ((char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position]))
            || (text[position - 1] == '\r' && text[position] == '\n'))) return position - 1;
        return position;
    }

    private static int CeilingBoundary(string text, int position)
    {
        if (position > 0 && position < text.Length && ((char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position]))
            || (text[position - 1] == '\r' && text[position] == '\n'))) return position + 1;
        return position;
    }
}

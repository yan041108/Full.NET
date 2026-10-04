using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace Full.NET.Modules.Ai.Knowledge;

/// <summary>固定离线 cl100k 普通文本词表；长度、内容和预分词规则共同决定计费版本。</summary>
internal static partial class AiKnowledgeCl100kTokenizer
{
    internal const string Version = "cl100k_base:ml-2.0.0:regex-v1:91423dd5a6f4f288c81534b7608e52cc6d54bc7a4bb522b31e9ed9a9b87061be";
    private const string ResourceName = "Full.NET.Modules.Ai.Knowledge.Tokenizers.cl100k_base.tiktoken.deflate";
    private const string CompressedHash = "05dd7477a7feeda8b71eb826b7e4c702e5df6f44c35d6ce4d1ef3f7bfc8a0f36";
    private const string VocabularyHash = "91423dd5a6f4f288c81534b7608e52cc6d54bc7a4bb522b31e9ed9a9b87061be";
    // 只保留一个可信固定词表实例；SDK 的有界线程安全缓存不按模型配置或用户不断扩张。
    private static readonly Lazy<TiktokenTokenizer> Shared = new(() =>
    {
        using var source = typeof(AiKnowledgeCl100kTokenizer).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("缺少固定 cl100k 词表资源。");
        return Create(source, CancellationToken.None);
    });

    /// <summary>同步可信初始化不能硬中断；调用者在取用前后观察取消，不把此缓存作为授权缓存。</summary>
    internal static TiktokenTokenizer Get(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var tokenizer = Shared.Value;
        token.ThrowIfCancellationRequested();
        return tokenizer;
    }

    /// <summary>仅从已固定的资源流构造词表；流归调用者，部署缺失或漂移抛出错误，不回退为字符估算。</summary>
    /// <param name="source">定位在固定压缩资源起点的可信流，不接受文档声明的路径或地址。</param>
    /// <param name="token">在有界读取、摘要和同步 SDK 初始化前后观察取消。</param>
    internal static TiktokenTokenizer Create(Stream source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compressed = ReadFixed(source, 522929, CompressedHash, token);
        using var input = new MemoryStream(compressed, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        // 解压预算独立于压缩长度；先完整验哈希再把词表交给 SDK，禁止解析未验证内容。
        var vocabulary = ReadFixed(deflate, 1190718, VocabularyHash, token);
        using var data = new MemoryStream(vocabulary, writable: false);
        token.ThrowIfCancellationRequested();
        var tokenizer = TiktokenTokenizer.Create(data, new RegexPreTokenizer(Cl100kRegex(), specialTokens: null), normalizer: null,
            specialTokens: null, cacheSize: 1024);
        token.ThrowIfCancellationRequested();
        return tokenizer;
    }

    private static byte[] ReadFixed(Stream source, int length, string hash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var bytes = new byte[length];
        var position = 0;
        while (position < length)
        {
            token.ThrowIfCancellationRequested();
            var count = source.Read(bytes.AsSpan(position, Math.Min(65536, length - position)));
            token.ThrowIfCancellationRequested();
            if (count == 0) throw new InvalidDataException("固定词表资源长度不匹配。");
            position += count;
        }
        var extra = source.ReadByte();
        token.ThrowIfCancellationRequested();
        if (extra != -1 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != hash)
            throw new InvalidDataException("固定词表资源摘要或长度不匹配。");
        token.ThrowIfCancellationRequested();
        return bytes;
    }

    // 来自 ML.Tokenizers 2.0.0 固定提交的 cl100k 规则，完整 MIT 通知随资源嵌入；不发现第三方类型或生成运行时代码。
    // CultureInvariant 防止英文缩写的忽略大小写受宿主文化影响；1 秒是单次正则匹配时限，不是任务总时限。
    [GeneratedRegex(@"'(?i:[sdmt]|ll|ve|re)|(?>[^\r\n\p{L}\p{N}]?)(?>\p{L}+)|(?>\p{N}{1,3})| ?(?>[^\s\p{L}\p{N}]+)(?>[\r\n]*)|(?>\s+)$|\s*[\r\n]|\s+(?!\S)|\s", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Cl100kRegex();
}

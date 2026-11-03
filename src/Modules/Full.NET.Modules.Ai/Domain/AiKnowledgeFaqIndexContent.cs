using System.Security.Cryptography;
using System.Text;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.Modules.Ai.Domain;

/// <summary>索引只读取服务器冻结修订的确定性文本；这是数据库修订来源，不生成或伪造文件。</summary>
internal sealed record AiKnowledgeFaqIndexContent(string Text, string Hash, long SizeBytes)
{
    internal const string FormatKey = "faq-revision-text-v1";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static AiKnowledgeFaqIndexContent Create(AiKnowledgeFaqContent content)
    {
        // 字段标签及换行是协议的一部分；不将维护者输入当作 Markdown 或模型指令执行。
        var text = "Question\n" + content.Question + "\nAliases\n" + string.Join('\n', content.Aliases)
            + "\nAnswer\n" + content.Answer + "\n";
        var bytes = Utf8.GetBytes(text);
        if (bytes.Length is < 1 or > 131072) throw new InvalidDataException("FAQ index input exceeds its budget.");
        // 与既有 UTF-8 解析器及清单使用同一规范摘要，避免字节相同却无法绑定完整计划。
        return new(text, Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength);
    }

    internal MemoryStream Open() => new(Utf8.GetBytes(Text), false);
}

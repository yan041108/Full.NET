using System.Text;
using System.Xml;

namespace Full.NET.Abstractions.Text;

/// <summary>删除 XML 1.0 不允许的字符，保留完整代理对；只处理 XML 文本，不负责 HTML 净化。</summary>
public static class XmlTextSanitizer
{
    /// <summary>保留合法 Unicode 文本；无非法字符时返回原字符串，长扫描定期检查取消。</summary>
    /// <param name="value">待写入 XML 文本节点的字符串。</param>
    /// <param name="cancellationToken">调用方的执行预算或取消令牌。</param>
    /// <returns>不含孤立代理项和 XML 非法控制字符的文本。</returns>
    public static string Sanitize(string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        StringBuilder? output = null;
        for (var index = 0; index < value.Length; index++)
        {
            // 代理对可能跳过精确边界，连续两个检查位置避免长文本丢失取消检查。
            if ((index & 1023) < 2) cancellationToken.ThrowIfCancellationRequested();
            var paired = char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]);
            if (paired || XmlConvert.IsXmlChar(value[index]))
            {
                output?.Append(value.AsSpan(index, paired ? 2 : 1));
                if (paired) index++;
            }
            else
            {
                output ??= new StringBuilder(value.Length).Append(value.AsSpan(0, index));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output?.ToString() ?? value;
    }
}

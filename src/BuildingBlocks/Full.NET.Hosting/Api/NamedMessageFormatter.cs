using System.Globalization;
using System.Text;

namespace Full.NET.Hosting.Api;

/// <summary>
/// 只替换精确命名占位符的安全消息格式器。
/// </summary>
/// <remarks>
/// 该格式器不执行复合格式代码、表达式、HTML 或任意对象格式化逻辑；
/// 任一占位符缺少参数或语法不完整时，调用方必须回退到安全默认消息。
/// </remarks>
public sealed class NamedMessageFormatter
{
    /// <summary>
    /// 尝试使用允许的简单值替换模板中的精确 <c>{Name}</c> 占位符。
    /// </summary>
    /// <param name="template">来自受控资源文件的消息模板。</param>
    /// <param name="arguments">稳定命名参数；多余参数不会进入输出。</param>
    /// <param name="culture">简单数字和时间值的显示文化。</param>
    /// <param name="message">成功时返回格式化后的消息。</param>
    /// <returns>模板语法和所有占位符参数均有效时返回 <see langword="true"/>。</returns>
    public bool TryFormat(
        string template,
        IReadOnlyDictionary<string, object?>? arguments,
        CultureInfo culture,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(culture);

        var builder = new StringBuilder(template.Length);
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character == '}')
            {
                message = string.Empty;
                return false;
            }

            if (character != '{')
            {
                builder.Append(character);
                continue;
            }

            var closingIndex = template.IndexOf('}', index + 1);
            if (closingIndex < 0)
            {
                message = string.Empty;
                return false;
            }

            var name = template[(index + 1)..closingIndex];
            if (name.Length == 0
                || name.Contains('{', StringComparison.Ordinal)
                || name.Contains(':', StringComparison.Ordinal)
                || arguments is null
                || !arguments.TryGetValue(name, out var value)
                || !TryFormatSimpleValue(value, culture, out var formattedValue))
            {
                message = string.Empty;
                return false;
            }

            builder.Append(formattedValue);
            index = closingIndex;
        }

        message = builder.ToString();
        return true;
    }

    private static bool TryFormatSimpleValue(
        object? value,
        CultureInfo culture,
        out string formattedValue)
    {
        formattedValue = value switch
        {
            null => string.Empty,
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean.ToString(culture),
            byte number => number.ToString(culture),
            sbyte number => number.ToString(culture),
            short number => number.ToString(culture),
            ushort number => number.ToString(culture),
            int number => number.ToString(culture),
            uint number => number.ToString(culture),
            long number => number.ToString(culture),
            ulong number => number.ToString(culture),
            float number => number.ToString(culture),
            double number => number.ToString(culture),
            decimal number => number.ToString(culture),
            DateTime valueDateTime => valueDateTime.ToString(culture),
            DateTimeOffset valueDateTimeOffset => valueDateTimeOffset.ToString(culture),
            TimeSpan duration => duration.ToString(null, culture),
            Guid identifier => identifier.ToString("D"),
            _ => string.Empty,
        };
        return value is null
            || value is string
            || value is char
            || value is bool
            || value is byte
            || value is sbyte
            || value is short
            || value is ushort
            || value is int
            || value is uint
            || value is long
            || value is ulong
            || value is float
            || value is double
            || value is decimal
            || value is DateTime
            || value is DateTimeOffset
            || value is TimeSpan
            || value is Guid;
    }
}

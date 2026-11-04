using System.Globalization;

namespace Full.NET.Data.Dapper;

/// <summary>外部查询单元格保持既有不变区域性文本，截断时不能切断合法代理对。</summary>
internal static class ExternalDatabaseCellText
{
    /// <summary>格式化单元格原值，并在最大 UTF-16 长度内保留完整 Unicode 字符。</summary>
    /// <param name="value">驱动已读取的单元格原值；数据库空值返回空引用。</param>
    /// <param name="maximum">最大文本长度，允许零但不允许负数。</param>
    internal static string? Format(object? value, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var text = value switch
        {
            null or DBNull => null,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
        if (text is null || text.Length <= maximum) return text;
        var length = maximum;
        if (length > 0 && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length])) length--;
        return text[..length];
    }
}

namespace Full.NET.Data.CodeGeneration.Schema;

/// <summary>
/// 定义首个 CRUD 生成切片支持的跨数据库逻辑标量类型。
/// </summary>
public enum FullNetScalarType
{
    /// <summary>UUID v7，对应 C# Guid。</summary>
    Uuid = 1,

    /// <summary>有界字符串。</summary>
    String = 2,

    /// <summary>32 位有符号整数。</summary>
    Int32 = 3,

    /// <summary>64 位有符号整数。</summary>
    Int64 = 4,

    /// <summary>布尔值。</summary>
    Boolean = 5,

    /// <summary>UTC 时间线瞬间。</summary>
    DateTimeUtc = 6,

    /// <summary>定点十进制数。</summary>
    Decimal = 7,
}

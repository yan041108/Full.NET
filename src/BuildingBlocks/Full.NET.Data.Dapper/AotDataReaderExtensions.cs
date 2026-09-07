#if FULLNET_AOT_COMPILE
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 读取辅助；MySQL DATETIME 在 shim 之外仍可能以 <see cref="DateTime"/> 出现。
/// </summary>
public static class AotDataReaderExtensions
{
    /// <summary>按数据库实际返回类型读取统一 UTC 时间。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
            ? reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime()
            : new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>读取允许为空的 UTC 时间。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : ReadDateTimeOffset(reader, ordinal);

    /// <summary>读取允许为空的业务唯一标识。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static Guid? ReadNullableGuid(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    /// <summary>读取允许为空的文本投影。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static string? ReadNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>兼容双库数值或布尔物理返回类型。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static bool ReadBoolean(DbDataReader reader, int ordinal) =>
        Convert.ToBoolean(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>兼容双库整数投影。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static int ReadInt32(DbDataReader reader, int ordinal) =>
        Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    /// <remarks>
    /// SQL Server bigint 与 MySQL BIGINT 可能以 Int64 或 Decimal 返回，禁止直接 GetInt64。
    /// </remarks>
    /// <summary>兼容双库大整数或十进制投影。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    public static long ReadInt64(DbDataReader reader, int ordinal) =>
        Convert.ToInt64(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>读取允许为空的小数，保持双库经纬度等数值投影的精度。</summary>
    /// <param name="reader">当前投影的数据读取器。</param>
    /// <param name="ordinal">经投影合同确定的列序号。</param>
    /// <returns>小数值；数据库空值返回 null。</returns>
    public static decimal? ReadNullableDecimal(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : Convert.ToDecimal(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
}
#endif

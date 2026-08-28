#if FULLNET_AOT_COMPILE
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Native AOT 读取辅助；MySQL DATETIME 在 shim 之外仍可能以 <see cref="DateTime"/> 出现。
/// </summary>
internal static class AotDataReaderExtensions
{
    public static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
            ? reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime()
            : new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    public static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : ReadDateTimeOffset(reader, ordinal);

    public static Guid? ReadNullableGuid(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    public static string? ReadNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static bool ReadBoolean(DbDataReader reader, int ordinal) =>
        Convert.ToBoolean(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    public static int ReadInt32(DbDataReader reader, int ordinal) =>
        Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);

    /// <remarks>
    /// SQL Server bigint 与 MySQL BIGINT 可能以 Int64 或 Decimal 返回，禁止直接 GetInt64。
    /// </remarks>
    public static long ReadInt64(DbDataReader reader, int ordinal) =>
        Convert.ToInt64(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
}
#endif

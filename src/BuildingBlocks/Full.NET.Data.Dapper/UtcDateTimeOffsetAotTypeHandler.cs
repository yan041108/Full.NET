#if FULLNET_AOT_COMPILE
using System.Data;
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper.AOT 参数侧 DateTimeOffset 处理；写入 UTC DateTime，读取标准化为 UTC Offset。
/// </summary>
internal sealed class UtcDateTimeOffsetAotTypeHandler : global::Dapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(DbParameter parameter) =>
        parameter.Value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
            DateTime dateTime => new DateTimeOffset(
                DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            _ => throw new DataException(
                $"Cannot convert {parameter.Value?.GetType().FullName} to DateTimeOffset."),
        };

    public override void SetValue(DbParameter parameter, DateTimeOffset value) =>
        parameter.Value = value.UtcDateTime;
}
#endif

using System.Data;
using global::Dapper;

namespace Full.NET.Data.Dapper;

internal sealed class UtcDateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override DateTimeOffset Parse(object value) => value switch
    {
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        DateTime dateTime => new DateTimeOffset(
            DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
        _ => throw new DataException(
            $"Cannot convert {value.GetType().FullName} to DateTimeOffset."),
    };

    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value)
    {
        parameter.Value = value.UtcDateTime;
    }
}

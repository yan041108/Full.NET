using System.Data;
using global::Dapper;

namespace Full.NET.Data.Dapper;

internal sealed class AssignedGuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public override Guid Parse(object value) => value switch
    {
        Guid guid => guid,
        string text when Guid.TryParse(text, out var guid) => guid,
        byte[] bytes when bytes.Length == 16 => new Guid(bytes, bigEndian: true),
        _ => throw new DataException(
            $"Cannot convert {value.GetType().FullName} to Guid."),
    };

    public override void SetValue(IDbDataParameter parameter, Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("持久化标识必须由应用预先分配。", nameof(value));
        }

        parameter.DbType = DbType.Guid;
        parameter.Value = value;
    }
}

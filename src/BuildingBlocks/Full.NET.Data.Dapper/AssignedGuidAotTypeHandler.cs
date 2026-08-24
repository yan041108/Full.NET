#if FULLNET_AOT_COMPILE
using System.Data;
using System.Data.Common;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper.AOT 参数侧 Guid 处理；与 <see cref="AssignedGuidTypeHandler"/> 语义对齐，禁止 Empty 入库。
/// </summary>
internal sealed class AssignedGuidAotTypeHandler : global::Dapper.TypeHandler<Guid>
{
    public override Guid Parse(DbParameter parameter) =>
        parameter.Value switch
        {
            Guid guid => guid,
            string text when Guid.TryParse(text, out var guid) => guid,
            byte[] bytes when bytes.Length == 16 => new Guid(bytes, bigEndian: true),
            _ => throw new DataException(
                $"Cannot convert {parameter.Value?.GetType().FullName} to Guid."),
        };

    public override void SetValue(DbParameter parameter, Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("持久化标识必须由应用预先分配。", nameof(value));
        }

        parameter.DbType = DbType.Guid;
        parameter.Value = value;
    }
}
#endif

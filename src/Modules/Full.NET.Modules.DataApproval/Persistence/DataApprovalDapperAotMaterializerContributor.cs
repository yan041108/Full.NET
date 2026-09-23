#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.DataApproval.Persistence;

/// <summary>按 DataApprovalSql 的固定投影列序注册 Native AOT 行物化器。</summary>
internal sealed class DataApprovalDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<DataApprovalRequestRecord>(ReadRequest);
        registrar.Register<DataApprovalScenarioRecord>(ReadScenario);
    }

    private static DataApprovalRequestRecord ReadRequest(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetGuid(5),
            reader.GetString(6),
            AotDataReaderExtensions.ReadNullableString(reader, 7),
            reader.GetString(8),
            AotDataReaderExtensions.ReadNullableGuid(reader, 9),
            reader.IsDBNull(10) ? null : AotDataReaderExtensions.ReadInt64(reader, 10),
            reader.GetGuid(11),
            reader.GetGuid(12),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 14),
            reader.GetString(15),
            reader.GetString(16),
            AotDataReaderExtensions.ReadNullableString(reader, 17),
            AotDataReaderExtensions.ReadNullableString(reader, 18),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 19),
            AotDataReaderExtensions.ReadInt32(reader, 20),
            reader.GetString(21),
            AotDataReaderExtensions.ReadNullableString(reader, 22),
            AotDataReaderExtensions.ReadNullableString(reader, 23),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 24),
            AotDataReaderExtensions.ReadInt32(reader, 25),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 26),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 27),
            AotDataReaderExtensions.ReadInt64(reader, 28));

    private static DataApprovalScenarioRecord ReadScenario(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadInt64(reader, 10));
}
#endif

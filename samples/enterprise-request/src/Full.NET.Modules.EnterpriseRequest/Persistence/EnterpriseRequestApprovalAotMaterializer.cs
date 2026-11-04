#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>提交日志使用静态投影，兼容双库 UUID 与 UTC 时间读取。</summary>
internal static class EnterpriseRequestApprovalAotMaterializer
{
    internal static void Register() => new DapperAotMaterializerRegistrar().Register<EnterpriseRequestApprovalSubmission>(
        reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            AotDataReaderExtensions.ReadInt64(reader, 3), reader.GetGuid(4), reader.GetGuid(5),
            reader.GetGuid(6), reader.GetGuid(7), reader.GetString(8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadNullableString(reader, 11),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadNullableGuid(reader, 13)));
}
#endif

#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>附件固定列序与双库 UUID、UTC 类型使用受控 Reader 适配。</summary>
internal static class EnterpriseRequestAttachmentAotMaterializer
{
    internal static void Register() => new DapperAotMaterializerRegistrar().Register<EnterpriseRequestAttachmentRow>(
        reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3),
            reader.GetString(4), AotDataReaderExtensions.ReadInt64(reader, 5), AotDataReaderExtensions.ReadDateTimeOffset(reader, 6)));
}
#endif

#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>固定回执投影顺序；使用 Provider 感知的 Guid 读取避免原生反射物化。</summary>
internal static class EnterpriseRequestImportAotMaterializer
{
    internal static void Register() => new DapperAotMaterializerRegistrar().Register<EnterpriseRequestImportReceiptRecord>(
        reader => new(AotDataReaderExtensions.ReadNullableGuid(reader, 0), reader.GetString(1)));
}
#endif

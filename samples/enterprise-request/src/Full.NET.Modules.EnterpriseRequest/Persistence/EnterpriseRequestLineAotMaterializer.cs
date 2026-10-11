#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.EnterpriseRequest.Persistence;

/// <summary>明细投影的固定列序，UUID 经提供程序 Reader 适配，金额直接读 decimal。</summary>
internal static class EnterpriseRequestLineAotMaterializer
{
    internal static void Register() => new DapperAotMaterializerRegistrar().Register<EnterpriseRequestLineRow>(
        reader => new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetInt32(3),
            reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7)));
}
#endif

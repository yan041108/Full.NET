#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper.Inbox;

namespace Full.NET.Data.Dapper;

/// <summary>
/// Dapper 基础设施自身的 Native AOT 行物化注册，避免依赖业务模块启动顺序。
/// </summary>
internal static class DapperAotInfrastructureRegistration
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (RegistrationLock)
        {
            if (_registered)
            {
                return;
            }

            DapperAotMaterializerRegistry.Register<InboxClaimRow>(reader =>
                new InboxClaimRow(
                    reader.GetString(0),
                    reader.GetFieldValue<byte[]>(1)));
            DapperAotMaterializerRegistry.Register<InboxBatchPrecheckRow>(reader =>
                new InboxBatchPrecheckRow(
                    reader.GetInt32(0),
                    AotDataReaderExtensions.ReadNullableString(reader, 1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2)));
            _registered = true;
        }
    }
}
#endif

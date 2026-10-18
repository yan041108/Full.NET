#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Notifications.Features.ReadIntentDelivery;

/// <summary>通知摘要的静态行物化闭包，列序与双库通用 SQL 保持一致。</summary>
internal static class NotificationIntentDeliveryAotMaterializer
{
    internal static void Register(DapperAotMaterializerRegistrar registrar) =>
        registrar.Register<NotificationIntentDeliveryRecord>(Read);

    internal static NotificationIntentDeliveryRecord Read(DbDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            AotDataReaderExtensions.ReadInt32(reader, 6), AotDataReaderExtensions.ReadInt32(reader, 7),
            AotDataReaderExtensions.ReadInt32(reader, 8), AotDataReaderExtensions.ReadInt32(reader, 9),
            AotDataReaderExtensions.ReadInt32(reader, 10), AotDataReaderExtensions.ReadInt32(reader, 11),
            AotDataReaderExtensions.ReadInt32(reader, 12), AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            AotDataReaderExtensions.ReadInt32(reader, 14), AotDataReaderExtensions.ReadInt32(reader, 15),
            AotDataReaderExtensions.ReadInt32(reader, 16), AotDataReaderExtensions.ReadInt32(reader, 17));
}
#endif

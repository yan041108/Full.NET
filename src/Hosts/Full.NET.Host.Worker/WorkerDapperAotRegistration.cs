#if FULLNET_AOT_COMPILE
using Full.NET.Data.Dapper;

namespace Full.NET.Host.Worker;

/// <summary>在 Worker 首次数据库访问前注册宿主自身的 Native AOT 行物化器。</summary>
internal static class WorkerDapperAotRegistration
{
    public static void Register() =>
        DapperAotMaterializerRegistry.Register<
            ShadowEventComparisonProcessor.OutboxFingerprintRow>(reader =>
                new ShadowEventComparisonProcessor.OutboxFingerprintRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    Convert.ToInt32(
                        reader.GetValue(2),
                        System.Globalization.CultureInfo.InvariantCulture),
                    reader.GetString(3),
                    reader.GetFieldValue<byte[]>(4),
                    ReadDateTimeOffset(reader, 5)));

    private static DateTimeOffset ReadDateTimeOffset(
        System.Data.Common.DbDataReader reader,
        int ordinal) =>
        reader.GetFieldType(ordinal) == typeof(DateTimeOffset)
            ? reader.GetFieldValue<DateTimeOffset>(ordinal).ToUniversalTime()
            : new DateTimeOffset(
                DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));
}
#endif

#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;
using Full.NET.Modules.Auditing.Features.QueryHostExceptionLogs;
using Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;

namespace Full.NET.Modules.Auditing.Persistence;

/// <summary>Auditing Native AOT 行物化器。所有当前显式投影列缺失时均失败关闭。</summary>
internal sealed class AuditingDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<HostAccessLogQueryService.AccessLogRecord>(ReadAccessLog);
        registrar.Register<HostOperationLogQueryService.OperationLogRecord>(ReadOperationLog);
        registrar.Register<HostExceptionLogQueryService.ExceptionLogRecord>(ReadExceptionLog);
        registrar.Register<OutboundCallLogRecord>(ReadOutboundCallLog);
        registrar.Register<HostDashboardAccessMetricsRecord>(ReadDashboardMetrics);
        registrar.Register<HostDashboardActivityRecord>(ReadDashboardActivity);
    }

    private static HostAccessLogQueryService.AccessLogRecord ReadAccessLog(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        OccurredAtUtc = ReadDateTimeOffset(reader, "OccurredAtUtc"),
        HttpMethod = ReadString(reader, "HttpMethod"),
        RequestPath = ReadString(reader, "RequestPath"),
        StatusCode = ReadInt32(reader, "StatusCode"),
        DurationMs = ReadInt32(reader, "DurationMs"),
        UserId = ReadNullableGuid(reader, "UserId"),
        TenantId = ReadNullableGuid(reader, "TenantId"),
        TraceId = ReadNullableString(reader, "TraceId"),
        ClientIpFingerprint = ReadNullableString(reader, "ClientIpFingerprint"),
        IsAuthenticated = ReadBoolean(reader, "IsAuthenticated"),
    };

    private static HostOperationLogQueryService.OperationLogRecord ReadOperationLog(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        OccurredAtUtc = ReadDateTimeOffset(reader, "OccurredAtUtc"),
        ActionKey = ReadString(reader, "ActionKey"),
        HttpMethod = ReadString(reader, "HttpMethod"),
        RequestPath = ReadString(reader, "RequestPath"),
        StatusCode = ReadInt32(reader, "StatusCode"),
        DurationMs = ReadInt32(reader, "DurationMs"),
        Succeeded = ReadBoolean(reader, "Succeeded"),
        UserId = ReadNullableGuid(reader, "UserId"),
        TenantId = ReadNullableGuid(reader, "TenantId"),
        TraceId = ReadNullableString(reader, "TraceId"),
        ClientIpFingerprint = ReadNullableString(reader, "ClientIpFingerprint"),
        PermissionCode = ReadNullableString(reader, "PermissionCode"),
    };

    private static HostExceptionLogQueryService.ExceptionLogRecord ReadExceptionLog(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        OccurredAtUtc = ReadDateTimeOffset(reader, "OccurredAtUtc"),
        ExceptionType = ReadString(reader, "ExceptionType"),
        Message = ReadString(reader, "Message"),
        StackTrace = ReadNullableString(reader, "StackTrace"),
        HttpMethod = ReadNullableString(reader, "HttpMethod"),
        RequestPath = ReadNullableString(reader, "RequestPath"),
        UserId = ReadNullableGuid(reader, "UserId"),
        TenantId = ReadNullableGuid(reader, "TenantId"),
        TraceId = ReadNullableString(reader, "TraceId"),
        ClientIpFingerprint = ReadNullableString(reader, "ClientIpFingerprint"),
    };

    private static OutboundCallLogRecord ReadOutboundCallLog(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        OccurredAtUtc = ReadDateTimeOffset(reader, "OccurredAtUtc"),
        ProviderKey = ReadString(reader, "ProviderKey"),
        OperationKey = ReadString(reader, "OperationKey"),
        DestinationHostCategory = ReadString(reader, "DestinationHostCategory"),
        StatusCode = ReadInt32(reader, "StatusCode"),
        Succeeded = ReadBoolean(reader, "Succeeded"),
        DurationMs = ReadInt32(reader, "DurationMs"),
        RetryCount = ReadInt32(reader, "RetryCount"),
        TraceId = ReadNullableString(reader, "TraceId"),
        SafeErrorCode = ReadNullableString(reader, "SafeErrorCode"),
        TenantId = ReadNullableGuid(reader, "TenantId"),
        UserId = ReadNullableGuid(reader, "UserId"),
    };

    private static HostDashboardAccessMetricsRecord ReadDashboardMetrics(DbDataReader reader) => new()
    {
        TodayRequestCount = ReadInt64(reader, "TodayRequestCount"),
        TodayErrorRate = ReadDecimal(reader, "TodayErrorRate"),
    };

    private static HostDashboardActivityRecord ReadDashboardActivity(DbDataReader reader) => new()
    {
        ActionKey = ReadString(reader, "ActionKey"),
        HttpMethod = ReadString(reader, "HttpMethod"),
        RequestPath = ReadString(reader, "RequestPath"),
        Succeeded = ReadBoolean(reader, "Succeeded"),
        OccurredAtUtc = ReadDateTimeOffset(reader, "OccurredAtUtc"),
    };

    private static Guid ReadGuid(DbDataReader reader, string name) => reader.GetGuid(reader.GetOrdinal(name));

    private static Guid? ReadNullableGuid(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadNullableGuid(reader, reader.GetOrdinal(name));

    private static string ReadString(DbDataReader reader, string name) => reader.GetString(reader.GetOrdinal(name));

    private static string? ReadNullableString(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadNullableString(reader, reader.GetOrdinal(name));

    private static int ReadInt32(DbDataReader reader, string name) =>
        Convert.ToInt32(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static long ReadInt64(DbDataReader reader, string name) =>
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static decimal ReadDecimal(DbDataReader reader, string name) =>
        Convert.ToDecimal(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static bool ReadBoolean(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadBoolean(reader, reader.GetOrdinal(name));

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadDateTimeOffset(reader, reader.GetOrdinal(name));
}
#endif

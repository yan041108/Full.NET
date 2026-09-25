using System.Globalization;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Persistence;

namespace Full.NET.Modules.Auditing.Features.WriteAccessLogs;

/// <summary>复用已登记的 Global SQL 声明，将一个有界批次写成单条参数化 INSERT。</summary>
internal static class AccessLogBatchSql
{
    public static (SqlStatement Statement, Dictionary<string, object?> Parameters) Build(
        IReadOnlyList<(Guid Id, AccessLogWriteModel Model)> rows)
    {
        if (rows.Count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(rows));
        }

        var sql = new StringBuilder(
            """
            INSERT INTO fn_auditing_access_log
                (Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode, DurationMs,
                 UserId, TenantId, TraceId, ClientIpFingerprint, IsAuthenticated)
            VALUES
            """);
        var parameters = new Dictionary<string, object?>(rows.Count * 11, StringComparer.Ordinal);
        for (var index = 0; index < rows.Count; index++)
        {
            if (index > 0)
            {
                sql.Append(',');
            }

            var prefix = "a" + index.ToString(CultureInfo.InvariantCulture);
            sql.AppendLine()
                .Append("(@").Append(prefix).Append("_Id, @")
                .Append(prefix).Append("_OccurredAtUtc, @")
                .Append(prefix).Append("_HttpMethod, @")
                .Append(prefix).Append("_RequestPath, @")
                .Append(prefix).Append("_StatusCode, @")
                .Append(prefix).Append("_DurationMs, @")
                .Append(prefix).Append("_UserId, @")
                .Append(prefix).Append("_TenantId, @")
                .Append(prefix).Append("_TraceId, @")
                .Append(prefix).Append("_ClientIpFingerprint, @")
                .Append(prefix).Append("_IsAuthenticated)");

            var (id, model) = rows[index];
            parameters[prefix + "_Id"] = id;
            parameters[prefix + "_OccurredAtUtc"] = model.OccurredAtUtc;
            parameters[prefix + "_HttpMethod"] = model.HttpMethod;
            parameters[prefix + "_RequestPath"] = model.RequestPath;
            parameters[prefix + "_StatusCode"] = model.StatusCode;
            parameters[prefix + "_DurationMs"] = model.DurationMs;
            parameters[prefix + "_UserId"] = model.UserId;
            parameters[prefix + "_TenantId"] = model.TenantId;
            parameters[prefix + "_TraceId"] = model.TraceId;
            parameters[prefix + "_ClientIpFingerprint"] = model.ClientIpFingerprint;
            parameters[prefix + "_IsAuthenticated"] = model.IsAuthenticated;
        }

        return (AccessLogSql.Insert with { Text = sql.ToString() }, parameters);
    }
}

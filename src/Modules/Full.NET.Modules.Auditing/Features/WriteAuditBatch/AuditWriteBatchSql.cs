using System.Globalization;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Features.WriteExceptionLogs;
using Full.NET.Modules.Auditing.Features.WriteOperationLogs;
using Full.NET.Modules.Auditing.Persistence;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

/// <summary>
/// B1 多行 INSERT 的 SQL 构造。Access 写入已迁出，生产默认不再逐请求写业务主库。
/// 动态 VALUES 只能克隆固定 Global 原型，禁止运行时 new SqlStatement 改变作用域元数据。
/// </summary>
internal static class AuditWriteBatchSql
{
    // SQL Server 参数上限约 2100；预留下限避免单批撑破。
    public const int MaxSqlParameters = 2000;

    public static readonly SqlStatement OperationPrototype = new(
        "auditing.microbatch.insert_operation_log",
        """
        INSERT INTO fn_auditing_operation_log
            (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode,
             DurationMs, Succeeded, UserId, TenantId, TraceId,
             ClientIpFingerprint, PermissionCode)
        VALUES
        (@o0_Id, @OccurredAtUtc, @o0_ActionKey, @o0_HttpMethod, @o0_RequestPath, @o0_StatusCode,
         @o0_DurationMs, @o0_Succeeded, @o0_UserId, @o0_TenantId, @o0_TraceId,
         @o0_ClientIpFingerprint, @o0_PermissionCode)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement ExceptionPrototype = new(
        "auditing.microbatch.insert_exception_log",
        """
        INSERT INTO fn_auditing_exception_log
            (Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
             HttpMethod, RequestPath, UserId, TenantId, TraceId,
             ClientIpFingerprint)
        VALUES
        (@e0_Id, @OccurredAtUtc, @e0_ExceptionType, @e0_Message, @e0_StackTrace,
         @e0_HttpMethod, @e0_RequestPath, @e0_UserId, @e0_TenantId, @e0_TraceId,
         @e0_ClientIpFingerprint)
        """,
        SqlDataScope.Global);

    public static readonly SqlStatement OutboundPrototype = new(
        "auditing.microbatch.insert_outbound_call",
        """
        INSERT INTO fn_auditing_outbound_call
            (Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
             StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
             TenantId, UserId)
        VALUES
        (@b0_Id, @b0_OccurredAtUtc, @b0_ProviderKey, @b0_OperationKey, @b0_DestinationHostCategory,
         @b0_StatusCode, @b0_Succeeded, @b0_DurationMs, @b0_RetryCount, @b0_TraceId, @b0_SafeErrorCode,
         @b0_TenantId, @b0_UserId)
        """,
        SqlDataScope.Global);

    public static (SqlStatement? Statement, Dictionary<string, object?> Parameters, int ParameterCount)
        BuildOperations(
            IReadOnlyList<(Guid Id, OperationLogWriteModel Model)> rows,
            DateTimeOffset occurredAtUtc)
    {
        if (rows.Count == 0)
        {
            return (null, [], 0);
        }

        var sql = new StringBuilder(
            """
            INSERT INTO fn_auditing_operation_log
                (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode,
                 DurationMs, Succeeded, UserId, TenantId, TraceId,
                 ClientIpFingerprint, PermissionCode)
            VALUES
            """);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(',');
            }

            var prefix = "o" + i.ToString(CultureInfo.InvariantCulture);
            sql.AppendLine()
                .Append("(@")
                .Append(prefix)
                .Append("_Id, @OccurredAtUtc, @")
                .Append(prefix)
                .Append("_ActionKey, @")
                .Append(prefix)
                .Append("_HttpMethod, @")
                .Append(prefix)
                .Append("_RequestPath, @")
                .Append(prefix)
                .Append("_StatusCode, @")
                .Append(prefix)
                .Append("_DurationMs, @")
                .Append(prefix)
                .Append("_Succeeded, @")
                .Append(prefix)
                .Append("_UserId, @")
                .Append(prefix)
                .Append("_TenantId, @")
                .Append(prefix)
                .Append("_TraceId, @")
                .Append(prefix)
                .Append("_ClientIpFingerprint, @")
                .Append(prefix)
                .Append("_PermissionCode)");

            var (id, model) = rows[i];
            parameters[prefix + "_Id"] = id;
            parameters[prefix + "_ActionKey"] = model.ActionKey;
            parameters[prefix + "_HttpMethod"] = model.HttpMethod;
            parameters[prefix + "_RequestPath"] = model.RequestPath;
            parameters[prefix + "_StatusCode"] = model.StatusCode;
            parameters[prefix + "_DurationMs"] = model.DurationMs;
            parameters[prefix + "_Succeeded"] = model.Succeeded;
            parameters[prefix + "_UserId"] = model.UserId;
            parameters[prefix + "_TenantId"] = model.TenantId;
            parameters[prefix + "_TraceId"] = model.TraceId;
            parameters[prefix + "_ClientIpFingerprint"] = model.ClientIpFingerprint;
            parameters[prefix + "_PermissionCode"] = model.PermissionCode;
        }

        parameters["OccurredAtUtc"] = occurredAtUtc;
        var statement = OperationPrototype with { Text = sql.ToString() };
        return (statement, parameters, parameters.Count);
    }

    public static (SqlStatement? Statement, Dictionary<string, object?> Parameters, int ParameterCount)
        BuildExceptions(
            IReadOnlyList<(Guid Id, ExceptionLogWriteModel Model)> rows,
            DateTimeOffset occurredAtUtc)
    {
        if (rows.Count == 0)
        {
            return (null, [], 0);
        }

        var sql = new StringBuilder(
            """
            INSERT INTO fn_auditing_exception_log
                (Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
                 HttpMethod, RequestPath, UserId, TenantId, TraceId,
                 ClientIpFingerprint)
            VALUES
            """);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(',');
            }

            var prefix = "e" + i.ToString(CultureInfo.InvariantCulture);
            sql.AppendLine()
                .Append("(@")
                .Append(prefix)
                .Append("_Id, @OccurredAtUtc, @")
                .Append(prefix)
                .Append("_ExceptionType, @")
                .Append(prefix)
                .Append("_Message, @")
                .Append(prefix)
                .Append("_StackTrace, @")
                .Append(prefix)
                .Append("_HttpMethod, @")
                .Append(prefix)
                .Append("_RequestPath, @")
                .Append(prefix)
                .Append("_UserId, @")
                .Append(prefix)
                .Append("_TenantId, @")
                .Append(prefix)
                .Append("_TraceId, @")
                .Append(prefix)
                .Append("_ClientIpFingerprint)");

            var (id, model) = rows[i];
            parameters[prefix + "_Id"] = id;
            parameters[prefix + "_ExceptionType"] = model.ExceptionType;
            parameters[prefix + "_Message"] = model.Message;
            parameters[prefix + "_StackTrace"] = model.StackTrace;
            parameters[prefix + "_HttpMethod"] = model.HttpMethod;
            parameters[prefix + "_RequestPath"] = model.RequestPath;
            parameters[prefix + "_UserId"] = model.UserId;
            parameters[prefix + "_TenantId"] = model.TenantId;
            parameters[prefix + "_TraceId"] = model.TraceId;
            parameters[prefix + "_ClientIpFingerprint"] = model.ClientIpFingerprint;
        }

        parameters["OccurredAtUtc"] = occurredAtUtc;
        var statement = ExceptionPrototype with { Text = sql.ToString() };
        return (statement, parameters, parameters.Count);
    }

    public static (SqlStatement? Statement, Dictionary<string, object?> Parameters, int ParameterCount)
        BuildOutbounds(
            IReadOnlyList<OutboundCallLogRecord> rows)
    {
        if (rows.Count == 0)
        {
            return (null, [], 0);
        }

        var sql = new StringBuilder(
            """
            INSERT INTO fn_auditing_outbound_call
                (Id, OccurredAtUtc, ProviderKey, OperationKey, DestinationHostCategory,
                 StatusCode, Succeeded, DurationMs, RetryCount, TraceId, SafeErrorCode,
                 TenantId, UserId)
            VALUES
            """);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(',');
            }

            var prefix = "b" + i.ToString(CultureInfo.InvariantCulture);
            sql.AppendLine()
                .Append("(@")
                .Append(prefix)
                .Append("_Id, @")
                .Append(prefix)
                .Append("_OccurredAtUtc, @")
                .Append(prefix)
                .Append("_ProviderKey, @")
                .Append(prefix)
                .Append("_OperationKey, @")
                .Append(prefix)
                .Append("_DestinationHostCategory, @")
                .Append(prefix)
                .Append("_StatusCode, @")
                .Append(prefix)
                .Append("_Succeeded, @")
                .Append(prefix)
                .Append("_DurationMs, @")
                .Append(prefix)
                .Append("_RetryCount, @")
                .Append(prefix)
                .Append("_TraceId, @")
                .Append(prefix)
                .Append("_SafeErrorCode, @")
                .Append(prefix)
                .Append("_TenantId, @")
                .Append(prefix)
                .Append("_UserId)");

            var model = rows[i];
            parameters[prefix + "_Id"] = model.Id;
            parameters[prefix + "_OccurredAtUtc"] = model.OccurredAtUtc;
            parameters[prefix + "_ProviderKey"] = model.ProviderKey;
            parameters[prefix + "_OperationKey"] = model.OperationKey;
            parameters[prefix + "_DestinationHostCategory"] = model.DestinationHostCategory;
            parameters[prefix + "_StatusCode"] = model.StatusCode;
            parameters[prefix + "_Succeeded"] = model.Succeeded;
            parameters[prefix + "_DurationMs"] = model.DurationMs;
            parameters[prefix + "_RetryCount"] = model.RetryCount;
            parameters[prefix + "_TraceId"] = model.TraceId;
            parameters[prefix + "_SafeErrorCode"] = model.SafeErrorCode;
            parameters[prefix + "_TenantId"] = model.TenantId;
            parameters[prefix + "_UserId"] = model.UserId;
        }

        var statement = OutboundPrototype with { Text = sql.ToString() };
        return (statement, parameters, parameters.Count);
    }
}
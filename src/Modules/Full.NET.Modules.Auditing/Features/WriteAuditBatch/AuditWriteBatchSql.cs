using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Auditing.Features.WriteAuditBatch;

internal static class AuditWriteBatchSql
{
    private const string AccessInsert =
        """
        INSERT INTO fn_auditing_access_log
            (Id, OccurredAtUtc, HttpMethod, RequestPath, StatusCode, DurationMs,
             UserId, TenantId, TraceId, ClientIpFingerprint, IsAuthenticated)
        VALUES
            (@AccessId, @OccurredAtUtc, @AccessHttpMethod, @AccessRequestPath,
             @AccessStatusCode, @AccessDurationMs, @AccessUserId, @AccessTenantId,
             @AccessTraceId, @AccessClientIpFingerprint, @AccessIsAuthenticated);
        """;

    private const string OperationInsert =
        """
        INSERT INTO fn_auditing_operation_log
            (Id, OccurredAtUtc, ActionKey, HttpMethod, RequestPath, StatusCode,
             DurationMs, Succeeded, UserId, TenantId, TraceId,
             ClientIpFingerprint, PermissionCode)
        VALUES
            (@OperationId, @OccurredAtUtc, @OperationActionKey, @OperationHttpMethod,
             @OperationRequestPath, @OperationStatusCode, @OperationDurationMs,
             @OperationSucceeded, @OperationUserId, @OperationTenantId,
             @OperationTraceId, @OperationClientIpFingerprint,
             @OperationPermissionCode);
        """;

    private const string ExceptionInsert =
        """
        INSERT INTO fn_auditing_exception_log
            (Id, OccurredAtUtc, ExceptionType, Message, StackTrace,
             HttpMethod, RequestPath, UserId, TenantId, TraceId,
             ClientIpFingerprint)
        VALUES
            (@ExceptionId, @OccurredAtUtc, @ExceptionType, @ExceptionMessage,
             @ExceptionStackTrace, @ExceptionHttpMethod, @ExceptionRequestPath,
             @ExceptionUserId, @ExceptionTenantId, @ExceptionTraceId,
             @ExceptionClientIpFingerprint);
        """;

    public static SqlStatement Get(AuditWriteKinds kinds) =>
        kinds switch
        {
            AuditWriteKinds.Access => Create("access", AccessInsert),
            AuditWriteKinds.Operation => Create("operation", OperationInsert),
            AuditWriteKinds.Exception => Create("exception", ExceptionInsert),
            AuditWriteKinds.Access | AuditWriteKinds.Operation => Create(
                "access_operation",
                $"{AccessInsert}{Environment.NewLine}{OperationInsert}"),
            AuditWriteKinds.Access | AuditWriteKinds.Exception => Create(
                "access_exception",
                $"{AccessInsert}{Environment.NewLine}{ExceptionInsert}"),
            AuditWriteKinds.Operation | AuditWriteKinds.Exception => Create(
                "operation_exception",
                $"{OperationInsert}{Environment.NewLine}{ExceptionInsert}"),
            AuditWriteKinds.Access
                | AuditWriteKinds.Operation
                | AuditWriteKinds.Exception => Create(
                    "access_operation_exception",
                    $"{AccessInsert}{Environment.NewLine}"
                    + $"{OperationInsert}{Environment.NewLine}"
                    + ExceptionInsert),
            _ => throw new ArgumentOutOfRangeException(nameof(kinds), kinds, null),
        };

    private static SqlStatement Create(string suffix, string text) =>
        new(
            $"auditing.insert_request_audit_batch.{suffix}",
            text,
            SqlDataScope.Global);
}

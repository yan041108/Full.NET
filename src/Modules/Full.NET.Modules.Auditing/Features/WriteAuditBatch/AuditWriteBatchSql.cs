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

    private static readonly SqlStatement Access = new(
        "auditing.insert_request_audit_batch.access",
        AccessInsert,
        SqlDataScope.Global);

    private static readonly SqlStatement Operation = new(
        "auditing.insert_request_audit_batch.operation",
        OperationInsert,
        SqlDataScope.Global);

    private static readonly SqlStatement Exception = new(
        "auditing.insert_request_audit_batch.exception",
        ExceptionInsert,
        SqlDataScope.Global);

    private static readonly SqlStatement AccessOperation = new(
        "auditing.insert_request_audit_batch.access_operation",
        $"{AccessInsert}{Environment.NewLine}{OperationInsert}",
        SqlDataScope.Global);

    private static readonly SqlStatement AccessException = new(
        "auditing.insert_request_audit_batch.access_exception",
        $"{AccessInsert}{Environment.NewLine}{ExceptionInsert}",
        SqlDataScope.Global);

    private static readonly SqlStatement OperationException = new(
        "auditing.insert_request_audit_batch.operation_exception",
        $"{OperationInsert}{Environment.NewLine}{ExceptionInsert}",
        SqlDataScope.Global);

    private static readonly SqlStatement AccessOperationException = new(
        "auditing.insert_request_audit_batch.access_operation_exception",
        $"{AccessInsert}{Environment.NewLine}"
        + $"{OperationInsert}{Environment.NewLine}"
        + ExceptionInsert,
        SqlDataScope.Global);

    public static SqlStatement Get(AuditWriteKinds kinds) =>
        kinds switch
        {
            AuditWriteKinds.Access => Access,
            AuditWriteKinds.Operation => Operation,
            AuditWriteKinds.Exception => Exception,
            AuditWriteKinds.Access | AuditWriteKinds.Operation => AccessOperation,
            AuditWriteKinds.Access | AuditWriteKinds.Exception => AccessException,
            AuditWriteKinds.Operation | AuditWriteKinds.Exception => OperationException,
            AuditWriteKinds.Access
                | AuditWriteKinds.Operation
                | AuditWriteKinds.Exception => AccessOperationException,
            _ => throw new ArgumentOutOfRangeException(nameof(kinds), kinds, null),
        };
}

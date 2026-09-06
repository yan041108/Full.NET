using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Features.QueryHostAccessLogs;
using Full.NET.Modules.Auditing.Features.QueryHostExceptionLogs;
using Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;
using Full.NET.Modules.Auditing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.ExportHostAuditLogs;

/// <summary>在保留、时间窗与字段授权边界内导出审计日志工作簿。</summary>
internal sealed class HostAuditLogExportService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions,
    AuditingExportTimeRangePolicy exportTimeRangePolicy)
{
    public Task<Result<AuditLogExportFileResult>> ExportAccessAsync(
        AuditLogExportRequest request,
        bool includeSensitiveFields,
        CancellationToken cancellationToken = default) =>
        ExportAsync(
            AuditLogExportKind.Access,
            request,
            includeSensitiveFields,
            cancellationToken);

    public Task<Result<AuditLogExportFileResult>> ExportOperationAsync(
        AuditLogExportRequest request,
        bool includeSensitiveFields,
        CancellationToken cancellationToken = default) =>
        ExportAsync(
            AuditLogExportKind.Operation,
            request,
            includeSensitiveFields,
            cancellationToken);

    public Task<Result<AuditLogExportFileResult>> ExportExceptionAsync(
        AuditLogExportRequest request,
        bool includeSensitiveFields,
        CancellationToken cancellationToken = default) =>
        ExportAsync(
            AuditLogExportKind.Exception,
            request,
            includeSensitiveFields,
            cancellationToken);

    private async Task<Result<AuditLogExportFileResult>> ExportAsync(
        AuditLogExportKind kind,
        AuditLogExportRequest request,
        bool includeSensitiveFields,
        CancellationToken cancellationToken)
    {
        var planResult = exportTimeRangePolicy.Plan(kind, request.FromUtc, request.ToUtc);
        if (!planResult.IsSuccess)
        {
            return Result<AuditLogExportFileResult>.Failure(planResult.Error!);
        }

        var plan = planResult.Value;
        var includeSensitive = includeSensitiveFields;
        var filter = NormalizeFilter(request);
        var parameters = AuditingSqlParameters.Create(
            ("FromUtc", plan.FromUtc),
            ("ToUtc", plan.ToUtc),
            ("HttpMethod", filter.HttpMethod),
            ("StatusCode", filter.StatusCode),
            ("Succeeded", filter.Succeeded),
            ("PathContains", filter.PathContains),
            ("ExceptionTypeContains", filter.ExceptionTypeContains),
            ("MaxRows", plan.MaximumRows));

        var (countStatement, listStatement) = ResolveStatements(kind);
        var total = await queryExecutor.QuerySingleOrDefaultAsync<long>(
                countStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);

        return kind switch
        {
            AuditLogExportKind.Access => await ExportAccessRowsAsync(
                    listStatement,
                    parameters,
                    plan,
                    includeSensitive,
                    total,
                    cancellationToken)
                .ConfigureAwait(false),
            AuditLogExportKind.Operation => await ExportOperationRowsAsync(
                    listStatement,
                    parameters,
                    plan,
                    includeSensitive,
                    total,
                    cancellationToken)
                .ConfigureAwait(false),
            AuditLogExportKind.Exception => await ExportExceptionRowsAsync(
                    listStatement,
                    parameters,
                    plan,
                    includeSensitive,
                    total,
                    cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("Unsupported audit log export kind."),
        };
    }

    private async Task<Result<AuditLogExportFileResult>> ExportAccessRowsAsync(
        SqlStatement listStatement,
        object parameters,
        AuditLogExportPlan plan,
        bool includeSensitive,
        long total,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostAccessLogQueryService.AccessLogRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var headers = BuildAccessHeaders(includeSensitive);
        var exportRows = rows.Select(row => BuildAccessRow(row, includeSensitive));
        return BuildResult(
            "access-logs",
            headers,
            exportRows,
            plan,
            includeSensitive,
            total);
    }

    private async Task<Result<AuditLogExportFileResult>> ExportOperationRowsAsync(
        SqlStatement listStatement,
        object parameters,
        AuditLogExportPlan plan,
        bool includeSensitive,
        long total,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostOperationLogQueryService.OperationLogRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var headers = BuildOperationHeaders(includeSensitive);
        var exportRows = rows.Select(row => BuildOperationRow(row, includeSensitive));
        return BuildResult(
            "operation-logs",
            headers,
            exportRows,
            plan,
            includeSensitive,
            total);
    }

    private async Task<Result<AuditLogExportFileResult>> ExportExceptionRowsAsync(
        SqlStatement listStatement,
        object parameters,
        AuditLogExportPlan plan,
        bool includeSensitive,
        long total,
        CancellationToken cancellationToken)
    {
        var rows = await queryExecutor.QueryAsync<HostExceptionLogQueryService.ExceptionLogRecord>(
                listStatement,
                parameters,
                cancellationToken)
            .ConfigureAwait(false);
        var headers = BuildExceptionHeaders(includeSensitive);
        var exportRows = rows.Select(row => BuildExceptionRow(row, includeSensitive));
        return BuildResult(
            "exception-logs",
            headers,
            exportRows,
            plan,
            includeSensitive,
            total);
    }

    private static Result<AuditLogExportFileResult> BuildResult(
        string fileStem,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string>> rows,
        AuditLogExportPlan plan,
        bool includeSensitive,
        long total)
    {
        var rowList = rows.ToArray();
        var fileBytes = AuditLogWorkbookCodec.Export(fileStem, headers, rowList);
        var metadata = new AuditLogExportMetadataResponse(
            rowList.Length,
            total > rowList.Length,
            includeSensitive,
            plan.FromUtc,
            plan.ToUtc,
            $"{fileStem}-{plan.FromUtc:yyyyMMddHHmm}-{plan.ToUtc:yyyyMMddHHmm}.xlsx");
        return Result<AuditLogExportFileResult>.Success(
            new AuditLogExportFileResult(fileBytes, metadata));
    }

    private static IReadOnlyList<string> BuildAccessHeaders(bool includeSensitive)
    {
        var headers = new List<string>
        {
            "id",
            "occurredAtUtc",
            "httpMethod",
            "requestPath",
            "statusCode",
            "durationMs",
            "userId",
            "tenantId",
            "isAuthenticated",
        };
        if (includeSensitive)
        {
            headers.Add("traceId");
            headers.Add("clientIpFingerprint");
        }

        return headers;
    }

    private static IReadOnlyList<string> BuildAccessRow(
        HostAccessLogQueryService.AccessLogRecord row,
        bool includeSensitive)
    {
        var values = new List<string>
        {
            row.Id.ToString("D"),
            row.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            AuditLogWorkbookCodec.EscapeFormulaText(row.HttpMethod),
            AuditLogWorkbookCodec.EscapeFormulaText(row.RequestPath),
            row.StatusCode.ToString(CultureInfo.InvariantCulture),
            row.DurationMs.ToString(CultureInfo.InvariantCulture),
            row.UserId?.ToString("D") ?? string.Empty,
            row.TenantId?.ToString("D") ?? string.Empty,
            row.IsAuthenticated ? "true" : "false",
        };
        if (includeSensitive)
        {
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.TraceId ?? string.Empty));
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.ClientIpFingerprint ?? string.Empty));
        }

        return values;
    }

    private static IReadOnlyList<string> BuildOperationHeaders(bool includeSensitive)
    {
        var headers = new List<string>
        {
            "id",
            "occurredAtUtc",
            "actionKey",
            "httpMethod",
            "requestPath",
            "statusCode",
            "durationMs",
            "succeeded",
            "userId",
            "tenantId",
            "permissionCode",
        };
        if (includeSensitive)
        {
            headers.Add("traceId");
            headers.Add("clientIpFingerprint");
        }

        return headers;
    }

    private static IReadOnlyList<string> BuildOperationRow(
        HostOperationLogQueryService.OperationLogRecord row,
        bool includeSensitive)
    {
        var values = new List<string>
        {
            row.Id.ToString("D"),
            row.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            AuditLogWorkbookCodec.EscapeFormulaText(row.ActionKey),
            AuditLogWorkbookCodec.EscapeFormulaText(row.HttpMethod),
            AuditLogWorkbookCodec.EscapeFormulaText(row.RequestPath),
            row.StatusCode.ToString(CultureInfo.InvariantCulture),
            row.DurationMs.ToString(CultureInfo.InvariantCulture),
            row.Succeeded ? "true" : "false",
            row.UserId?.ToString("D") ?? string.Empty,
            row.TenantId?.ToString("D") ?? string.Empty,
            AuditLogWorkbookCodec.EscapeFormulaText(row.PermissionCode ?? string.Empty),
        };
        if (includeSensitive)
        {
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.TraceId ?? string.Empty));
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.ClientIpFingerprint ?? string.Empty));
        }

        return values;
    }

    private static IReadOnlyList<string> BuildExceptionHeaders(bool includeSensitive)
    {
        var headers = new List<string>
        {
            "id",
            "occurredAtUtc",
            "exceptionType",
            "message",
            "httpMethod",
            "requestPath",
            "userId",
            "tenantId",
        };
        if (includeSensitive)
        {
            headers.Add("stackTrace");
            headers.Add("traceId");
            headers.Add("clientIpFingerprint");
        }

        return headers;
    }

    private static IReadOnlyList<string> BuildExceptionRow(
        HostExceptionLogQueryService.ExceptionLogRecord row,
        bool includeSensitive)
    {
        var values = new List<string>
        {
            row.Id.ToString("D"),
            row.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            AuditLogWorkbookCodec.EscapeFormulaText(row.ExceptionType),
            AuditLogWorkbookCodec.EscapeFormulaText(row.Message),
            AuditLogWorkbookCodec.EscapeFormulaText(row.HttpMethod ?? string.Empty),
            AuditLogWorkbookCodec.EscapeFormulaText(row.RequestPath ?? string.Empty),
            row.UserId?.ToString("D") ?? string.Empty,
            row.TenantId?.ToString("D") ?? string.Empty,
        };
        if (includeSensitive)
        {
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.StackTrace ?? string.Empty));
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.TraceId ?? string.Empty));
            values.Add(AuditLogWorkbookCodec.EscapeFormulaText(row.ClientIpFingerprint ?? string.Empty));
        }

        return values;
    }

    private static AuditLogExportFilter NormalizeFilter(AuditLogExportRequest request)
    {
        string? httpMethod = null;
        if (!string.IsNullOrWhiteSpace(request.HttpMethod))
        {
            httpMethod = request.HttpMethod.Trim().ToUpperInvariant();
            if (httpMethod.Length > 16)
            {
                httpMethod = httpMethod[..16];
            }
        }

        string? pathContains = null;
        if (!string.IsNullOrWhiteSpace(request.PathContains))
        {
            pathContains = request.PathContains.Trim();
            if (pathContains.Length > 200)
            {
                pathContains = pathContains[..200];
            }
        }

        string? exceptionTypeContains = null;
        if (!string.IsNullOrWhiteSpace(request.ExceptionTypeContains))
        {
            exceptionTypeContains = request.ExceptionTypeContains.Trim();
            if (exceptionTypeContains.Length > 128)
            {
                exceptionTypeContains = exceptionTypeContains[..128];
            }
        }

        return new AuditLogExportFilter(
            httpMethod,
            request.StatusCode,
            request.Succeeded,
            pathContains,
            exceptionTypeContains);
    }

    private (SqlStatement Count, SqlStatement List) ResolveStatements(AuditLogExportKind kind) =>
        databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => kind switch
            {
                AuditLogExportKind.Access => (
                    AuditLogExportSql.CountAccessSqlServer,
                    AuditLogExportSql.ListAccessSqlServer),
                AuditLogExportKind.Operation => (
                    AuditLogExportSql.CountOperationSqlServer,
                    AuditLogExportSql.ListOperationSqlServer),
                AuditLogExportKind.Exception => (
                    AuditLogExportSql.CountExceptionSqlServer,
                    AuditLogExportSql.ListExceptionSqlServer),
                _ => throw new InvalidOperationException("Unsupported audit log export kind."),
            },
            DatabaseProvider.MySql => kind switch
            {
                AuditLogExportKind.Access => (
                    AuditLogExportSql.CountAccessMySql,
                    AuditLogExportSql.ListAccessMySql),
                AuditLogExportKind.Operation => (
                    AuditLogExportSql.CountOperationMySql,
                    AuditLogExportSql.ListOperationMySql),
                AuditLogExportKind.Exception => (
                    AuditLogExportSql.CountExceptionMySql,
                    AuditLogExportSql.ListExceptionMySql),
                _ => throw new InvalidOperationException("Unsupported audit log export kind."),
            },
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };

    private sealed record AuditLogExportFilter(
        string? HttpMethod,
        int? StatusCode,
        bool? Succeeded,
        string? PathContains,
        string? ExceptionTypeContains);
}

/// <summary>审计日志导出文件结果。</summary>
internal sealed record AuditLogExportFileResult(
    byte[] FileBytes,
    AuditLogExportMetadataResponse Metadata);

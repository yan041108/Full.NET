using System.Net;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Auditing.Contracts;
using Full.NET.Modules.Auditing.Persistence;
using Full.NET.Modules.Auditing.Serialization;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Auditing.Features.QueryHostOperationLogs;

/// <summary>受限详情的独立 Host 查询；数据库到期过滤与响应前复核都失败关闭。</summary>
internal sealed class HostOperationLogDetailsQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<OperationLogDetailsResponse>> GetByIdAsync(
        Guid operationLogId,
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => OperationLogSql.FindDetailsSqlServer,
            DatabaseProvider.MySql => OperationLogSql.FindDetailsMySql,
            _ => throw new NotSupportedException("The configured database provider is not supported."),
        };
        var row = await queryExecutor.QuerySingleOrDefaultAsync<DetailRow>(
            statement,
            AuditingSqlParameters.Create(
                ("OperationLogId", operationLogId),
                ("NowUtc", clock.UtcNow.ToUniversalTime())),
            cancellationToken).ConfigureAwait(false);
        if (row is null || row.Id != operationLogId
            || row.DetailsExpiresAtUtc <= clock.UtcNow.ToUniversalTime()
            || row.ContextJson.Length > 8192
            || Encoding.UTF8.GetByteCount(row.ContextJson) > 8192)
        {
            return NotFound();
        }

        try
        {
            var context = JsonSerializer.Deserialize(
                row.ContextJson,
                AuditingJsonSerializerContext.Default.OperationLogDetailsContextV1);
            if (context is null || context.SchemaVersion != 1
                || !ValidIp(context.ClientIp) || !ValidIp(context.ServerIp)
                || !ValidPort(context.ClientPort) || !ValidPort(context.ServerPort)
                || !ValidProjection(context))
            {
                return NotFound();
            }

            return Result<OperationLogDetailsResponse>.Success(new OperationLogDetailsResponse(
                row.Id,
                row.DetailsExpiresAtUtc,
                context));
        }
        catch (JsonException)
        {
            return NotFound();
        }
    }

    private static bool ValidIp(string? value) =>
        value is null || value.Length <= 64 && IPAddress.TryParse(value, out _);

    private static bool ValidPort(int? value) =>
        value is null or > 0 and <= 65535;

    private static bool ValidProjection(OperationLogDetailsContextV1 context) =>
        ValidState(context.RequestCaptureState)
        && ValidState(context.ResponseCaptureState)
        && (context.RequestCaptureState == "captured") == (context.RequestSummary is not null)
        && (context.ResponseCaptureState == "captured") == (context.ResponseSummary is not null)
        && (context.RequestSummary is not { } request
            || request.FromUtc.Offset == TimeSpan.Zero
                && request.ToUtc.Offset == TimeSpan.Zero
                && request.FromUtc <= request.ToUtc)
        && (context.ResponseSummary is not { } response || response.RowCount >= 0);

    private static bool ValidState(string? state) =>
        state is null or "captured" or "not_enabled" or "not_allowed"
            or "not_applicable" or "redacted" or "truncated" or "failed"
            or "budget_exceeded";

    private static Result<OperationLogDetailsResponse> NotFound() =>
        Result<OperationLogDetailsResponse>.Failure(new Error(
            AuditingErrorCodes.OperationLogNotFound,
            "The operation log entry was not found.",
            ErrorType.NotFound));

    internal sealed class DetailRow
    {
        public Guid Id { get; init; }

        public string ContextJson { get; init; } = string.Empty;

        public DateTimeOffset DetailsExpiresAtUtc { get; init; }
    }
}

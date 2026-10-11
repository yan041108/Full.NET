using System.Globalization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

/// <summary>固定企业申请工作簿；预校验不写业务数据，执行按有效行序号恢复。</summary>
internal sealed class EnterpriseRequestStaticImportSchemaHandler(
    EnterpriseRequestImportService importService) : IStaticImportSchemaHandler
{
    public string SchemaKey => StaticImportSchemaKeys.DemoEnterpriseRequests;

    public StaticImportSchemaDefinition GetDefinition() => new(SchemaKey, "企业申请",
        StaticImportSchemaScopeKeys.Tenant, EnterpriseRequestPermissions.Create,
        [new StaticImportWorksheetDefinition("requests", "申请", EnterpriseRequestWorkbookCodec.ImportHeaders)]);

    public byte[] CreateTemplate(string worksheetKey)
    {
        if (!string.Equals(worksheetKey, "requests", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsupported enterprise request worksheet.");
        return EnterpriseRequestWorkbookCodec.CreateImportTemplate();
    }

    public async Task<Result<StaticImportPreviewResult>> PreviewAsync(Stream content, long contentLength,
        StaticImportPreviewContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await EnterpriseRequestWorkbookCodec.ParseImportAsync(content, contentLength, cancellationToken).ConfigureAwait(false);
            var results = rows.Select(row => new StaticImportRowPreviewResult(row.LineNumber,
                TryParse(row, out _, out _), null, null)).Select(row => row.IsValid ? row : row with
                { ErrorCode = "row.invalid", Message = "Invalid field values." }).ToArray();
            var valid = results.Count(row => row.IsValid);
            return Result<StaticImportPreviewResult>.Success(new(rows.Count, valid, rows.Count - valid, results));
        }
        catch (InvalidDataException)
        {
            return Result<StaticImportPreviewResult>.Failure(InvalidWorkbook());
        }
    }

    public async Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(Stream content, long contentLength,
        int startLineNumber, int batchSize, StaticImportPreviewContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.TaskId is not Guid taskId || taskId == Guid.Empty || startLineNumber < 0 || batchSize <= 0)
            return Result<StaticImportBatchExecutionResult>.Failure(new Error(ValidationErrorCodes.Failed,
                "A persisted task and valid batch boundaries are required.", ErrorType.Validation));
        try
        {
            var source = await EnterpriseRequestWorkbookCodec.ParseImportAsync(content, contentLength, cancellationToken).ConfigureAwait(false);
            var results = new List<StaticImportRowExecutionResult>();
            // 检查点是有效行的零基序号；回执保留原始 Excel 行号，空白或错误行不改变幂等身份。
            foreach (var row in source.Where(row => TryParse(row, out _, out _)).Skip(startLineNumber).Take(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = TryParse(row, out var request, out var unitId);
                var result = await importService.ImportAsync(taskId, row.LineNumber, request!, unitId,
                    context.RequestedByUserId, cancellationToken).ConfigureAwait(false);
                results.Add(result.IsSuccess ? new(row.LineNumber, true, result.Value, null, null)
                    : new(row.LineNumber, false, null, result.Error!.Code, result.Error.Message));
            }
            return Result<StaticImportBatchExecutionResult>.Success(new(results));
        }
        catch (InvalidDataException)
        {
            return Result<StaticImportBatchExecutionResult>.Failure(InvalidWorkbook());
        }
    }

    private static Error InvalidWorkbook() => new(ValidationErrorCodes.Failed,
        "The enterprise request workbook is invalid or exceeds the supported limits.", ErrorType.Validation);

    private static bool HasExactScale(string value)
    {
        // 先检查原文，避免 Decimal.TryParse 将极小非零数舍入为零后通过两位精度校验。
        var point = value.IndexOf('.');
        return point < 0 || value.Length <= point + 3 || value.AsSpan(point + 3).IndexOfAnyExcept('0') < 0;
    }

    private static bool TryParse(EnterpriseRequestWorkbookRow row, out CreateEnterpriseRequestRequest? request, out Guid unitId)
    {
        request = null; unitId = default;
        var f = row.Fields;
        if (string.IsNullOrWhiteSpace(f[0]) || f[0].Length > 64 || string.IsNullOrWhiteSpace(f[1]) || f[1].Length > 200
            || !HasExactScale(f[2])
            || !decimal.TryParse(f[2], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var amount)
            || amount is <= -10_000_000_000_000_000m or >= 10_000_000_000_000_000m
            || decimal.Round(amount, 2) != amount
            || !Guid.TryParse(f[3], out var applicantId) || applicantId == Guid.Empty
            || !Guid.TryParse(f[4], out unitId) || unitId == Guid.Empty) return false;
        request = new(f[0], f[1], EnterpriseRequestStatusKeys.Draft, amount, applicantId);
        return true;
    }
}

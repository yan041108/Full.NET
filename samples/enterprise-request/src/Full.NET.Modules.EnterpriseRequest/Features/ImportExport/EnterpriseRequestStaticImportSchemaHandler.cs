using System.Globalization;
using System.Resources;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

/// <summary>申请单使用真实 XLSX 模板、统一预校验及原业务写入边界，不接受伪装成工作簿的 CSV。</summary>
internal sealed class EnterpriseRequestStaticImportSchemaHandler(
    EnterpriseRequestImportWriter writer,
    IReportingWorkbookRenderer workbookRenderer) : IStaticImportSchemaHandler
{
    private static readonly ResourceManager Resources = new(
        "Full.NET.Modules.EnterpriseRequest.Resources.EnterpriseRequestDeliveryErrors", typeof(EnterpriseRequestStaticImportSchemaHandler).Assembly);

    public string SchemaKey => EnterpriseRequestImportSchemaKeys.EnterpriseRequests;

    public StaticImportSchemaDefinition GetDefinition() => new(SchemaKey, "企业申请", StaticImportSchemaScopeKeys.Tenant,
        EnterpriseRequestPermissions.Create, [new StaticImportWorksheetDefinition("requests", "申请", EnterpriseRequestWorkbookReader.Headers)]);

    public byte[] CreateTemplate(string worksheetKey)
    {
        if (worksheetKey != "requests") throw new InvalidOperationException("Unsupported worksheet key.");
        var workbook = workbookRenderer.Render(EnterpriseRequestWorkbookReader.Headers.Select(header =>
            new ReportingExecutionColumnDefinition(header, header)).ToArray(), []);
        if (!workbook.IsSuccess) throw new InvalidDataException("Unable to create the bounded workbook template.");
        return workbook.Value!;
    }

    public async Task<Result<StaticImportPreviewResult>> PreviewAsync(Stream content, long contentLength,
        StaticImportPreviewContext context, CancellationToken cancellationToken = default)
    {
        var rows = await EnterpriseRequestWorkbookReader.ReadAsync(content, contentLength, cancellationToken, context.ColumnMapping).ConfigureAwait(false);
        var preview = rows.Select(row => new StaticImportRowPreviewResult(row.LineNumber, row.ErrorCode is null,
            row.ErrorCode, row.ErrorCode is null ? null : Resources.GetString(row.ErrorCode, CultureInfo.CurrentUICulture))).ToArray();
        return Result<StaticImportPreviewResult>.Success(new(preview.Length,
            preview.Count(row => row.IsValid), preview.Count(row => !row.IsValid), preview));
    }

    public async Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(Stream content, long contentLength,
        int startLineNumber, int batchSize, StaticImportPreviewContext context, CancellationToken cancellationToken = default)
    {
        if (startLineNumber < 0 || batchSize is < 1 or > 200) throw new InvalidDataException("Invalid batch bounds.");
        var parsed = await EnterpriseRequestWorkbookReader.ReadAsync(content, contentLength, cancellationToken, context.ColumnMapping).ConfigureAwait(false);
        var results = new List<StaticImportRowExecutionResult>();
        // 检查点是有效行序号，不是物理行号；错误回执仍保留工作簿原始行号。
        foreach (var row in parsed.Where(row => row.ErrorCode is null).Skip(startLineNumber).Take(batchSize))
        {
            var created = await writer.WriteAsync(row, context, cancellationToken).ConfigureAwait(false);
            results.Add(created.IsSuccess ? new(row.LineNumber, true, created.Value, null, null)
                : new(row.LineNumber, false, null, created.Error!.Code,
                    Resources.GetString(created.Error.Code, CultureInfo.CurrentUICulture) ?? created.Error.Message));
        }
        return Result<StaticImportBatchExecutionResult>.Success(new(results));
    }
}

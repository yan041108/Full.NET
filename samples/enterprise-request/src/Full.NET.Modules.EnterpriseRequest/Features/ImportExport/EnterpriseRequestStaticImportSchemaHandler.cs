using System.Globalization;
using System.Text;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.EnterpriseRequest.Contracts;
using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.ImportExport.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.ImportExport;

internal sealed class EnterpriseRequestStaticImportSchemaHandler(
    EnterpriseRequestManagementService managementService) : IStaticImportSchemaHandler
{
    private static readonly string[] Headers =
    [
        "requestNumber",
        "title",
        "totalAmount",
        "applicantUserId",
        "organizationUnitId",
    ];

    public string SchemaKey => StaticImportSchemaKeys.DemoEnterpriseRequests;

    public StaticImportSchemaDefinition GetDefinition() =>
        new(
            SchemaKey,
            "企业申请",
            StaticImportSchemaScopeKeys.Tenant,
            EnterpriseRequestPermissions.Create,
            [
                new StaticImportWorksheetDefinition(
                    "requests",
                    "申请",
                    Headers),
            ]);

    public byte[] CreateTemplate(string worksheetKey) =>
        Encoding.UTF8.GetBytes(string.Join(',', Headers) + Environment.NewLine);

    public Task<Result<StaticImportPreviewResult>> PreviewAsync(
        Stream content,
        long contentLength,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<StaticImportPreviewResult>.Success(
            new StaticImportPreviewResult(0, 0, 0, [])));

    public async Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(
        Stream content,
        long contentLength,
        int startLineNumber,
        int batchSize,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var lineNumber = 1;
        var processed = 0;
        while (lineNumber < startLineNumber && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            lineNumber++;
        }

        var rows = new List<StaticImportRowExecutionResult>();
        while (processed < batchSize)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            lineNumber++;
            processed++;
            var parts = line.Split(',');
            if (parts.Length < 5)
            {
                rows.Add(new StaticImportRowExecutionResult(lineNumber, false, null, "row.invalid", "Invalid column count."));
                continue;
            }

            if (!decimal.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                || !Guid.TryParse(parts[3], out var applicantUserId)
                || !Guid.TryParse(parts[4], out var organizationUnitId))
            {
                rows.Add(new StaticImportRowExecutionResult(lineNumber, false, null, "row.invalid", "Invalid field values."));
                continue;
            }

            var create = await managementService.CreateAsync(
                    new CreateEnterpriseRequestRequest(
                        parts[0],
                        parts[1],
                        EnterpriseRequestStatusKeys.Draft,
                        amount,
                        applicantUserId),
                    context.RequestedByUserId,
                    organizationUnitId,
                    cancellationToken)
                .ConfigureAwait(false);
            rows.Add(create.IsSuccess
                ? new StaticImportRowExecutionResult(lineNumber, true, create.Value!.Id, null, null)
                : new StaticImportRowExecutionResult(lineNumber, false, null, create.Error!.Code, create.Error.Message));
        }

        return Result<StaticImportBatchExecutionResult>.Success(new StaticImportBatchExecutionResult(rows));
    }
}
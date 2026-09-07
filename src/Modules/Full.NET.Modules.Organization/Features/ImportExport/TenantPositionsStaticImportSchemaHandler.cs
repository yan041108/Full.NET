using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;

namespace Full.NET.Modules.Organization.Features.ImportExport;

/// <summary>Organization 租户职位静态导入 Schema 处理器。</summary>
internal sealed class TenantPositionsStaticImportSchemaHandler(
    TenantPositionImportPreviewService previewService,
    TenantPositionManagementService managementService) : IStaticImportSchemaHandler
{
    internal const string PositionsWorksheetKey = "positions";

    private static readonly string[] ImportHeaders =
    [
        "code",
        "name",
        "displayOrder",
        "unitCode",
        "positionLevelCode",
    ];

    public string SchemaKey => StaticImportSchemaKeys.OrganizationTenantPositions;

    public StaticImportSchemaDefinition GetDefinition() =>
        new(
            SchemaKey,
            "租户职位",
            StaticImportSchemaScopeKeys.Tenant,
            OrganizationPositionManagementPermissions.Import,
            [
                new StaticImportWorksheetDefinition(
                    PositionsWorksheetKey,
                    "职位",
                    ImportHeaders),
            ]);

    public byte[] CreateTemplate(string worksheetKey)
    {
        if (!string.Equals(worksheetKey, PositionsWorksheetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unsupported worksheet key.");
        }

        return OrganizationPositionWorkbookCodec.CreateImportTemplate();
    }

    public async Task<Result<StaticImportPreviewResult>> PreviewAsync(
        Stream content,
        long contentLength,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rows = await OrganizationPositionWorkbookCodec
                .ParseImportAsync(content, contentLength, cancellationToken)
                .ConfigureAwait(false);
            return Result<StaticImportPreviewResult>.Success(previewService.Preview(rows, context));
        }
        catch (InvalidDataException)
        {
            return Result<StaticImportPreviewResult>.Failure(new Error(
                OrganizationErrorCodes.PositionImportWorkbookInvalid,
                "The organization position import workbook is invalid.",
                ErrorType.Validation));
        }
    }

    public async Task<Result<StaticImportBatchExecutionResult>> ExecuteBatchAsync(
        Stream content,
        long contentLength,
        int startLineNumber,
        int batchSize,
        StaticImportPreviewContext context,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            return Result<StaticImportBatchExecutionResult>.Failure(new Error(
                ValidationErrorCodes.Failed,
                "Batch size must be positive.",
                ErrorType.Validation));
        }

        try
        {
            var rows = await OrganizationPositionWorkbookCodec
                .ParseImportAsync(content, contentLength, cancellationToken)
                .ConfigureAwait(false);
            var preview = previewService.Preview(rows, context);
            var validRows = preview.Rows
                .Where(row => row.IsValid)
                .OrderBy(row => row.LineNumber)
                .ToArray();
            var batchValidRows = validRows
                .Skip(startLineNumber)
                .Take(batchSize)
                .ToArray();
            var batchRows = batchValidRows
                .Select(valid => rows[valid.LineNumber - 1])
                .ToArray();
            if (batchRows.Length == 0)
            {
                return Result<StaticImportBatchExecutionResult>.Success(
                    new StaticImportBatchExecutionResult([]));
            }

            var capabilities = ResolveImportCapabilities(context);
            if (context.TaskId is not Guid taskId || taskId == Guid.Empty)
                return Result<StaticImportBatchExecutionResult>.Failure(new Error(ValidationErrorCodes.Failed,
                    "Durable task identity is required for resumable import.", ErrorType.Validation));
            var executionRows = new StaticImportRowExecutionResult[batchRows.Length];
            for (var index = 0; index < batchRows.Length; index++)
            {
                var line = batchValidRows[index].LineNumber;
                var imported = await managementService.ImportTaskRowAsync(taskId, line, batchRows[index],
                    capabilities, cancellationToken).ConfigureAwait(false);
                executionRows[index] = new StaticImportRowExecutionResult(line, imported.IsSuccess,
                    imported.IsSuccess ? imported.Value!.PositionId : null, imported.Error?.Code, imported.Error?.Message);
            }
            return Result<StaticImportBatchExecutionResult>.Success(
                new StaticImportBatchExecutionResult(executionRows));
        }
        catch (InvalidDataException)
        {
            return Result<StaticImportBatchExecutionResult>.Failure(new Error(
                OrganizationErrorCodes.PositionImportWorkbookInvalid,
                "The organization position import workbook is invalid.",
                ErrorType.Validation));
        }
    }

    private static OrganizationPositionImportCapabilities ResolveImportCapabilities(
        StaticImportPreviewContext context)
    {
        return new OrganizationPositionImportCapabilities(
            HasCapability(context, OrganizationPositionManagementPermissions.AssignUnit)
            && HasCapability(context, OrganizationUnitManagementPermissions.Read),
            HasCapability(context, OrganizationPositionManagementPermissions.AssignPositionLevel)
            && HasCapability(context, OrganizationPositionLevelManagementPermissions.Read));
    }

    private static bool HasCapability(StaticImportPreviewContext context, string permissionCode) =>
        context.CapabilityFlags.TryGetValue(permissionCode, out var allowed) && allowed;
}

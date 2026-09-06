using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Organization.Features.ManageTenantPositions;

namespace Full.NET.Modules.Organization.Features.ImportExport;

/// <summary>Organization 租户职位静态导入 Schema 处理器。</summary>
internal sealed class TenantPositionsStaticImportSchemaHandler(
    TenantPositionImportPreviewService previewService) : IStaticImportSchemaHandler
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
}

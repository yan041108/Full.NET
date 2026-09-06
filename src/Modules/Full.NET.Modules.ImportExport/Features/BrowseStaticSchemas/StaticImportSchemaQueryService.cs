using Full.NET.Abstractions.Results;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.ImportExport.Domain;

namespace Full.NET.Modules.ImportExport.Features.BrowseStaticSchemas;

/// <summary>静态导入 Schema 目录只读查询。</summary>
internal sealed class StaticImportSchemaQueryService(StaticImportSchemaRegistry registry)
{
    public Result<IReadOnlyList<StaticImportSchemaDefinition>> ListAsync() =>
        Result<IReadOnlyList<StaticImportSchemaDefinition>>.Success(registry.ListDefinitions());

    public Result<StaticImportSchemaDefinition> GetAsync(string schemaKey)
    {
        var handler = registry.TryResolve(schemaKey);
        return handler is null
            ? Result<StaticImportSchemaDefinition>.Failure(SchemaNotFoundError())
            : Result<StaticImportSchemaDefinition>.Success(handler.GetDefinition());
    }

    public Result<byte[]> CreateTemplate(string schemaKey, string worksheetKey)
    {
        var handler = registry.TryResolve(schemaKey);
        if (handler is null)
        {
            return Result<byte[]>.Failure(SchemaNotFoundError());
        }

        var definition = handler.GetDefinition();
        if (!definition.Worksheets.Any(worksheet =>
                string.Equals(worksheet.WorksheetKey, worksheetKey, StringComparison.Ordinal)))
        {
            return Result<byte[]>.Failure(WorksheetNotFoundError());
        }

        try
        {
            return Result<byte[]>.Success(handler.CreateTemplate(worksheetKey));
        }
        catch (Exception)
        {
            return Result<byte[]>.Failure(WorksheetNotFoundError());
        }
    }

    private static Error SchemaNotFoundError() =>
        new(
            ImportExportErrorCodes.SchemaNotFound,
            "The static import schema was not found.",
            ErrorType.NotFound);

    private static Error WorksheetNotFoundError() =>
        new(
            ImportExportErrorCodes.WorksheetNotFound,
            "The static import worksheet was not found.",
            ErrorType.NotFound);
}

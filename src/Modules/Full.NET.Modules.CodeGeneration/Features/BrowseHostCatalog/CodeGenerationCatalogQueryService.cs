using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Schema;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.CodeGeneration.Features.BrowseHostCatalog;

/// <summary>
/// 只读列举当前 API 进程数据库的基础表与默认可生成列，禁止接受连接串或执行 DDL。
/// </summary>
internal sealed class CodeGenerationCatalogQueryService(
    IQueryExecutor queryExecutor,
    IOptions<DatabaseOptions> databaseOptions)
{
    public async Task<Result<IReadOnlyList<CodeGenerationCatalogTableResponse>>>
        ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = await ReadTableNamesAsync(cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<CodeGenerationCatalogTableResponse>>.Success(
            tables
                .Select(tableName => new CodeGenerationCatalogTableResponse(
                    tableName))
                .ToArray());
    }

    public async Task<Result<CodeGenerationCatalogColumnListResponse>>
        ListColumnsAsync(
            string tableName,
            CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveTableAsync(tableName, cancellationToken)
            .ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Result<CodeGenerationCatalogColumnListResponse>.Failure(
                resolved.Error!);
        }

        var mapped = await MapColumnsAsync(resolved.Value!, cancellationToken)
            .ConfigureAwait(false);
        return Result<CodeGenerationCatalogColumnListResponse>.Success(
            new CodeGenerationCatalogColumnListResponse(
                resolved.Value!,
                mapped.Columns,
                mapped.SkippedColumnNames));
    }

    public async Task<Result<CodeGenerationCatalogColumnSyncResponse>>
        SyncColumnsAsync(
            CodeGenerationCatalogColumnSyncRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var live = await ListColumnsAsync(request.TableName, cancellationToken)
            .ConfigureAwait(false);
        if (!live.IsSuccess)
        {
            return Result<CodeGenerationCatalogColumnSyncResponse>.Failure(
                live.Error!);
        }

        var existing = request.Columns
            ?? Array.Empty<CodeGenerationPreviewColumnRequest>();
        var existingByName = existing
            .Where(column => column is not null)
            .GroupBy(column => column.DatabaseName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.Ordinal);
        var merged = new List<CodeGenerationPreviewColumnRequest>();
        var added = new List<string>();
        foreach (var liveColumn in live.Value!.Columns)
        {
            if (existingByName.TryGetValue(
                    liveColumn.DatabaseName,
                    out var configured))
            {
                merged.Add(configured with
                {
                    ScalarType = liveColumn.ScalarType,
                    IsNullable = liveColumn.IsNullable,
                    MaxLength = liveColumn.MaxLength,
                    NumericPrecision = liveColumn.NumericPrecision,
                    NumericScale = liveColumn.NumericScale,
                    Ui = configured.Ui ?? liveColumn.Ui,
                });
                continue;
            }

            merged.Add(liveColumn);
            added.Add(liveColumn.DatabaseName);
        }

        var liveNames = live.Value.Columns
            .Select(column => column.DatabaseName)
            .ToHashSet(StringComparer.Ordinal);
        var removed = existingByName.Keys
            .Where(name => !liveNames.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return Result<CodeGenerationCatalogColumnSyncResponse>.Success(
            new CodeGenerationCatalogColumnSyncResponse(
                live.Value.TableName,
                merged,
                added,
                removed,
                live.Value.SkippedColumnNames));
    }

    private async Task<Result<string>> ResolveTableAsync(
        string tableName,
        CancellationToken cancellationToken)
    {
        if (!DatabaseCatalogQueries.IsSafeTableName(tableName))
        {
            return Result<string>.Failure(new Error(
                CodeGenerationCatalogErrorCodes.InvalidTable,
                "The catalog table name is invalid.",
                ErrorType.Validation));
        }

        var tables = await ReadTableNamesAsync(cancellationToken)
            .ConfigureAwait(false);
        var match = tables.FirstOrDefault(candidate =>
            string.Equals(candidate, tableName, StringComparison.Ordinal));
        return match is null
            ? Result<string>.Failure(new Error(
                CodeGenerationCatalogErrorCodes.TableNotFound,
                "The catalog table was not found.",
                ErrorType.NotFound))
            : Result<string>.Success(match);
    }

    private async Task<IReadOnlyList<string>> ReadTableNamesAsync(
        CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                CodeGenerationCatalogSql.ListTablesSqlServer,
            DatabaseProvider.MySql => CodeGenerationCatalogSql.ListTablesMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor
            .QueryAsync<CodeGenerationCatalogTableRow>(
                statement,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return rows
            .Select(row => row.TableName)
            .Where(DatabaseCatalogQueries.IsSafeTableName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tableName => tableName, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<(
            IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
            IReadOnlyList<string> SkippedColumnNames)>
        MapColumnsAsync(
            string tableName,
            CancellationToken cancellationToken)
    {
        var statement = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer =>
                CodeGenerationCatalogSql.ListColumnsSqlServer,
            DatabaseProvider.MySql => CodeGenerationCatalogSql.ListColumnsMySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var provider = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DatabaseMetadataProvider.SqlServer,
            DatabaseProvider.MySql => DatabaseMetadataProvider.MySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rows = await queryExecutor
            .QueryAsync<CodeGenerationCatalogColumnRow>(
                statement,
                new { TableName = tableName },
                cancellationToken)
            .ConfigureAwait(false);
        var columns = new List<CodeGenerationPreviewColumnRequest>();
        var skipped = new List<string>();
        foreach (var row in rows.OrderBy(item => item.OrdinalPosition))
        {
            var metadata = new DatabaseColumnMetadata(
                row.ColumnName,
                row.DataType,
                row.ColumnType,
                string.Equals(
                    row.IsNullable,
                    "YES",
                    StringComparison.OrdinalIgnoreCase),
                row.MaxLength,
                row.OrdinalPosition,
                row.NumericPrecision,
                row.NumericScale);
            if (!DatabaseColumnMetadataMapper.TryMap(
                    provider,
                    metadata,
                    out var mapped))
            {
                skipped.Add(row.ColumnName);
                continue;
            }

            var ui = mapped.ResolvedUi;
            columns.Add(new CodeGenerationPreviewColumnRequest(
                mapped.DatabaseName,
                mapped.ClrPropertyName,
                mapped.JsonPropertyName,
                ToWireScalar(mapped.ScalarType),
                mapped.IsNullable,
                mapped.MaxLength,
                mapped.NumericPrecision,
                mapped.NumericScale,
                new CodeGenerationPreviewColumnUiRequest(
                    ToWireControl(ui.ControlKind),
                    ui.ShowInList,
                    ui.IncludeInCreate,
                    ui.IncludeInUpdate,
                    ui.Required,
                    ui.Sortable,
                    ui.Queryable,
                    ToWireQuery(ui.QueryKind),
                    ui.Unique,
                    ui.IncludeInImportExport)));
        }

        return (columns, skipped
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray());
    }

    private static string ToWireScalar(FullNetScalarType value) =>
        value switch
        {
            FullNetScalarType.Uuid => "uuid",
            FullNetScalarType.String => "string",
            FullNetScalarType.Int32 => "int32",
            FullNetScalarType.Int64 => "int64",
            FullNetScalarType.Boolean => "boolean",
            FullNetScalarType.DateTimeUtc => "date.time.utc",
            FullNetScalarType.Decimal => "decimal",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

    private static string ToWireControl(FullNetColumnControlKind value) =>
        value switch
        {
            FullNetColumnControlKind.Text => "text",
            FullNetColumnControlKind.Textarea => "textarea",
            FullNetColumnControlKind.Number => "number",
            FullNetColumnControlKind.Switch => "switch",
            FullNetColumnControlKind.DateTime => "datetime",
            FullNetColumnControlKind.Uuid => "uuid",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };

    private static string ToWireQuery(FullNetColumnQueryKind value) =>
        value switch
        {
            FullNetColumnQueryKind.None => "none",
            FullNetColumnQueryKind.Equals => "equals",
            FullNetColumnQueryKind.Contains => "contains",
            FullNetColumnQueryKind.Range => "range",
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
        };
}

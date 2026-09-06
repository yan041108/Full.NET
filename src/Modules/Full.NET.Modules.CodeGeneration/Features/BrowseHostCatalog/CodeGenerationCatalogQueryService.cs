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
    /// <summary>
    /// 按当前数据库 Provider 列举基础表名，过滤不安全名称并稳定排序；只读，禁止接受连接串或执行 DDL。
    /// </summary>
    public async Task<Result<IReadOnlyList<CodeGenerationCatalogTableResponse>>>
        ListTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.Table,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<CodeGenerationCatalogTableResponse>>.Success(
            tables
                .Select(tableName => new CodeGenerationCatalogTableResponse(
                    tableName))
                .ToArray());
    }

    /// <summary>
    /// 列举当前库视图，过滤不安全名称并稳定排序；只读，不执行 DDL。
    /// </summary>
    public async Task<Result<IReadOnlyList<CodeGenerationCatalogObjectResponse>>>
        ListViewsAsync(CancellationToken cancellationToken = default)
    {
        var views = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.View,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<CodeGenerationCatalogObjectResponse>>.Success(
            views
                .Select(viewName => new CodeGenerationCatalogObjectResponse(
                    viewName,
                    CodeGenerationCatalogObjectKinds.View))
                .ToArray());
    }

    /// <summary>
    /// 合并列举基础表与视图，供元数据检查页浏览。
    /// </summary>
    public async Task<Result<IReadOnlyList<CodeGenerationCatalogObjectResponse>>>
        ListObjectsAsync(CancellationToken cancellationToken = default)
    {
        var tables = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.Table,
                cancellationToken)
            .ConfigureAwait(false);
        var views = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.View,
                cancellationToken)
            .ConfigureAwait(false);
        var objects = tables
            .Select(name => new CodeGenerationCatalogObjectResponse(
                name,
                CodeGenerationCatalogObjectKinds.Table))
            .Concat(views.Select(name => new CodeGenerationCatalogObjectResponse(
                name,
                CodeGenerationCatalogObjectKinds.View)))
            .OrderBy(item => item.ObjectName, StringComparer.Ordinal)
            .ToArray();
        return Result<IReadOnlyList<CodeGenerationCatalogObjectResponse>>.Success(
            objects);
    }

    /// <summary>
    /// 返回表或视图的原始 INFORMATION_SCHEMA 列元数据，不做代码生成映射。
    /// </summary>
    public async Task<Result<CodeGenerationCatalogMetadataResponse>>
        GetMetadataAsync(
            string objectName,
            CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveObjectAsync(objectName, cancellationToken)
            .ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Result<CodeGenerationCatalogMetadataResponse>.Failure(
                resolved.Error!);
        }

        var columns = await ReadRawColumnsAsync(
                resolved.Value!.Name,
                cancellationToken)
            .ConfigureAwait(false);
        return Result<CodeGenerationCatalogMetadataResponse>.Success(
            new CodeGenerationCatalogMetadataResponse(
                resolved.Value!.Name,
                resolved.Value!.Kind,
                columns
                    .Select(column => new CodeGenerationCatalogMetadataColumnResponse(
                        column.Name,
                        column.DataType,
                        column.ColumnType,
                        column.IsNullable,
                        column.MaxLength,
                        column.OrdinalPosition,
                        column.NumericPrecision,
                        column.NumericScale))
                    .ToArray()));
    }

    /// <summary>
    /// 基于基础表当前列形态生成双库迁移草案文本，不执行 DDL。
    /// </summary>
    public async Task<Result<CodeGenerationCatalogMigrationDraftResponse>>
        GenerateMigrationDraftAsync(
            CodeGenerationCatalogMigrationDraftRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolved = await ResolveTableAsync(
                request.TableName,
                cancellationToken)
            .ConfigureAwait(false);
        if (!resolved.IsSuccess)
        {
            return Result<CodeGenerationCatalogMigrationDraftResponse>.Failure(
                resolved.Error!);
        }

        var columns = await ReadRawColumnsAsync(
                resolved.Value!,
                cancellationToken)
            .ConfigureAwait(false);
        var provider = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DatabaseMetadataProvider.SqlServer,
            DatabaseProvider.MySql => DatabaseMetadataProvider.MySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var draft = CatalogMigrationDraftGenerator.Generate(
            resolved.Value!,
            provider,
            columns);
        return Result<CodeGenerationCatalogMigrationDraftResponse>.Success(
            new CodeGenerationCatalogMigrationDraftResponse(
                resolved.Value!,
                draft.SqlServerDraft,
                draft.MySqlDraft,
                draft.Warnings));
    }

    /// <summary>
    /// 按表名读取列元数据并映射为可生成列；表名先经 IsSafeTableName 与存在性双重校验，不识别的列类型进入 SkippedColumnNames 而非抛异常。
    /// </summary>
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

    /// <summary>
    /// 用当前库列集合对照已编辑列配置：新增列带默认 UI，已有列保留人工 UI 并以实时列元数据覆盖物理属性，删除列只返回名称；按列名归并，重复配置取最后一条。
    /// </summary>
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

        var tables = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.Table,
                cancellationToken)
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

    private async Task<Result<(string Name, string Kind)>> ResolveObjectAsync(
        string objectName,
        CancellationToken cancellationToken)
    {
        if (!DatabaseCatalogQueries.IsSafeTableName(objectName))
        {
            return Result<(string Name, string Kind)>.Failure(new Error(
                CodeGenerationCatalogErrorCodes.InvalidTable,
                "The catalog object name is invalid.",
                ErrorType.Validation));
        }

        var tables = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.Table,
                cancellationToken)
            .ConfigureAwait(false);
        var tableMatch = tables.FirstOrDefault(candidate =>
            string.Equals(candidate, objectName, StringComparison.Ordinal));
        if (tableMatch is not null)
        {
            return Result<(string Name, string Kind)>.Success(
                (tableMatch, CodeGenerationCatalogObjectKinds.Table));
        }

        var views = await ReadObjectNamesAsync(
                CodeGenerationCatalogObjectKinds.View,
                cancellationToken)
            .ConfigureAwait(false);
        var viewMatch = views.FirstOrDefault(candidate =>
            string.Equals(candidate, objectName, StringComparison.Ordinal));
        return viewMatch is null
            ? Result<(string Name, string Kind)>.Failure(new Error(
                CodeGenerationCatalogErrorCodes.ObjectNotFound,
                "The catalog object was not found.",
                ErrorType.NotFound))
            : Result<(string Name, string Kind)>.Success(
                (viewMatch, CodeGenerationCatalogObjectKinds.View));
    }

    private async Task<IReadOnlyList<string>> ReadObjectNamesAsync(
        string objectKind,
        CancellationToken cancellationToken)
    {
        var statement = (databaseOptions.Value.Provider, objectKind) switch
        {
            (DatabaseProvider.SqlServer, CodeGenerationCatalogObjectKinds.Table) =>
                CodeGenerationCatalogSql.ListTablesSqlServer,
            (DatabaseProvider.MySql, CodeGenerationCatalogObjectKinds.Table) =>
                CodeGenerationCatalogSql.ListTablesMySql,
            (DatabaseProvider.SqlServer, CodeGenerationCatalogObjectKinds.View) =>
                CodeGenerationCatalogSql.ListViewsSqlServer,
            (DatabaseProvider.MySql, CodeGenerationCatalogObjectKinds.View) =>
                CodeGenerationCatalogSql.ListViewsMySql,
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

    private async Task<IReadOnlyList<DatabaseColumnMetadata>> ReadRawColumnsAsync(
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
        var rows = await queryExecutor
            .QueryAsync<CodeGenerationCatalogColumnRow>(
                statement,
                CodeGenerationSqlParameters.Create(("TableName", tableName)),
                cancellationToken)
            .ConfigureAwait(false);
        return rows
            .OrderBy(item => item.OrdinalPosition)
            .Select(row => new DatabaseColumnMetadata(
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
                row.NumericScale))
            .ToArray();
    }

    private async Task<(
            IReadOnlyList<CodeGenerationPreviewColumnRequest> Columns,
            IReadOnlyList<string> SkippedColumnNames)>
        MapColumnsAsync(
            string tableName,
            CancellationToken cancellationToken)
    {
        var provider = databaseOptions.Value.Provider switch
        {
            DatabaseProvider.SqlServer => DatabaseMetadataProvider.SqlServer,
            DatabaseProvider.MySql => DatabaseMetadataProvider.MySql,
            _ => throw new InvalidOperationException(
                "The configured database provider is not supported."),
        };
        var rawColumns = await ReadRawColumnsAsync(tableName, cancellationToken)
            .ConfigureAwait(false);
        var columns = new List<CodeGenerationPreviewColumnRequest>();
        var skipped = new List<string>();
        foreach (var metadata in rawColumns)
        {
            if (!DatabaseColumnMetadataMapper.TryMap(
                    provider,
                    metadata,
                    out var mapped))
            {
                skipped.Add(metadata.Name);
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

using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>以静态 SQL 和参数化 CASE 排序生成查询闭环，不把客户端字段或方向拼接成 SQL。</summary>
internal static class CrudListQueryGenerator
{
    internal static IReadOnlyList<FullNetCrudQueryDefinition.Field> Fields(FullNetCrudSchema schema) => FullNetCrudQueryDefinition.Fields(schema.Columns);
    internal static string ProblemCode(FullNetCrudSchema schema) => $"{schema.ModuleKey}.{schema.PermissionResourceName}.invalid_query";

    internal static string Contracts(FullNetCrudSchema schema)
    {
        var fields = Fields(schema);
        var names = new[] { "page", "pageSize", "sortBy", "descending" }.Concat(fields.Select(field => field.QueryName));
        var sortable = schema.Columns.Where(column => column.ResolvedUi.Sortable).Select(column => $"\"{column.JsonPropertyName}\"").ToArray();
        var properties = string.Join("\n", fields.Select(field => $"    {QueryAttribute(schema, field.QueryName)}public {Type(field.Column)}? {field.ClrName} {{ get; init; }}"));
        var invalid = new List<string> { sortable.Length == 0 ? "SortBy is not null" : $"SortBy is not null && SortBy is not ({string.Join(" or ", sortable)})" };
        invalid.AddRange(fields.Where(field => field.Column.ScalarType == FullNetScalarType.String).Select(field => $"{field.ClrName}?.Length > {Math.Min(field.Column.MaxLength ?? 1024, 4096)}"));
        invalid.AddRange(fields.Where(field => field.Column.ScalarType == FullNetScalarType.Uuid).Select(field => $"{field.ClrName} == Guid.Empty"));
        invalid.AddRange(fields.Where(field => field.Suffix == "From").Select(field => $"({field.ClrName} is not null && Filter{field.Column.ClrPropertyName}To is not null && {field.ClrName} > Filter{field.Column.ClrPropertyName}To)"));
        invalid.AddRange(fields.Where(field => field.Column.ScalarType == FullNetScalarType.DateTimeUtc)
            .Select(field => $"({field.ClrName} is DateTimeOffset {field.ClrName}Date && ({field.ClrName}Date.ToUniversalTime().Year < 1000 || {field.ClrName}Date.UtcTicks % 10 != 0))"));
        var id = schema.Columns.Single(column => column.DatabaseName == "Id").JsonPropertyName;
        var parameters = string.Join(",\n", fields.Select(field => $"            [\"{Parameter(field)}\"] = {ParameterValue(field)}"));
        return $$"""

            /// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
            public sealed class {{schema.ClrTypeName}}ListQuery
            {
                {{QueryAttribute(schema, "sortBy")}}public string? SortBy { get; init; }
                {{QueryAttribute(schema, "descending")}}public bool? Descending { get; init; }
            {{properties}}

                internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
                { {{string.Join(", ", names.Select(name => $"\"{name}\""))}} };

                internal Full.NET.Abstractions.Results.Error? Validate() =>
                    {{string.Join("\n            || ", invalid)}} ? Invalid() : null;

                internal static Full.NET.Abstractions.Results.Error Invalid() => new("{{ProblemCode(schema)}}",
                    "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

                internal static bool IsWireValueValid(string name, string? value) => name switch
                {
            {{string.Join("\n", fields.Where(field => field.Column.ScalarType is FullNetScalarType.Int64 or FullNetScalarType.Decimal or FullNetScalarType.DateTimeUtc).Select(field =>
                $"        \"{field.QueryName}\" => " + (field.Column.ScalarType == FullNetScalarType.Int64 ? "Full.NET.Hosting.Serialization.FullNetWireScalars.TryInt64(value, out _),"
                    : field.Column.ScalarType == FullNetScalarType.Decimal ? "Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),"
                    : $"Full.NET.Hosting.Serialization.FullNetWireScalars.TryUtcDateTime(value, out var {field.ClrName}Date) && {field.ClrName}Date.Year >= 1000 && {field.ClrName}Date.UtcTicks % 10 == 0,")))}}
                    _ => true,
                };

                internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
                {
                    ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "{{id}}", ["CodegenDescending"] = Descending ?? false{{(parameters.Length == 0 ? string.Empty : ",")}}
            {{parameters}}
                };

                // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
                private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
                    .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
            }
            """;
    }

    internal static string Sql(FullNetCrudSchema schema, string projection)
    {
        var blocks = new List<string>();
        foreach (var provider in new[] { "SqlServer", "MySql" })
        {
            var predicates = new List<string> { "1 = 1" };
            if (schema.IsTenantScoped) predicates.Add("TenantId = @TenantId");
            if (schema.EntityCapabilities.DeleteMode == FullNetCrudDeleteMode.SoftDelete) predicates.Add("IsDeleted = 0");
            foreach (var field in Fields(schema))
            {
                var column = SqlColumn(field.Column, provider);
                var op = field.Suffix == "From" ? ">=" : field.Suffix == "To" ? "<=" : field.Column.ResolvedUi.QueryKind == FullNetColumnQueryKind.Contains ? "LIKE" : "=";
                predicates.Add($"(@{Parameter(field)} IS NULL OR {column} {op} @{Parameter(field)}{(op == "LIKE" ? " ESCAPE '~'" : string.Empty)})");
            }
            var where = "WHERE " + string.Join("\n            AND ", predicates);
            var sortColumns = schema.Columns.Where(column => column.ResolvedUi.Sortable || column.DatabaseName == "Id");
            var order = string.Join(",\n            ", sortColumns.SelectMany(column => new[] {
                $"CASE WHEN @CodegenSortBy = '{column.JsonPropertyName}' AND @CodegenDescending = 0 THEN {SqlColumn(column, provider)} END ASC",
                $"CASE WHEN @CodegenSortBy = '{column.JsonPropertyName}' AND @CodegenDescending = 1 THEN {SqlColumn(column, provider)} END DESC" }).Append("Id ASC"));
            var pagination = provider == "SqlServer" ? "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;" : "LIMIT @PageSize OFFSET @Offset;";
            blocks.Add($$""""

                    public const string QueryCount{{provider}} = """
                        SELECT COUNT(1)
                        FROM {{schema.DatabaseTableName}}
                        {{where}};
                        """;
                    public const string QueryList{{provider}} = """
                        SELECT
                            {{projection}}
                        FROM {{schema.DatabaseTableName}}
                        {{where}}
                        ORDER BY {{order}}
                        {{pagination}}
                        """;
                """");
            if (schema.DataScope != FullNetCrudDataScope.Unspecified)
            {
                var scope = schema.IsTenantScoped ? "TenantRequired" : schema.DataScope == FullNetCrudDataScope.HostOnly ? "HostOnly" : "Global";
                var binding = schema.IsTenantScoped ? "CurrentTenantId" : "None";
                blocks.Add($$"""

                        public static readonly SqlStatement QueryCount{{provider}}Statement = new(
                            "{{schema.ModuleKey}}.count_{{schema.ApiResourceName.Replace('-', '_')}}_query.{{(provider == "SqlServer" ? "sql_server" : "my_sql")}}",
                            QueryCount{{provider}}, SqlDataScope.{{scope}}, SqlTenantBinding.{{binding}});
                    """);
            }
        }
        return "\n" + string.Join("\n", blocks);
    }

    internal static string EndpointValidation(FullNetCrudSchema schema) => $$"""
                        foreach (var pair in httpContext.Request.Query)
                        {
                            if (!{{schema.ClrTypeName}}ListQuery.QueryNames.Contains(pair.Key) || pair.Value.Count != 1
                                || !{{schema.ClrTypeName}}ListQuery.IsWireValueValid(pair.Key, pair.Value[0]))
                                return mapper.Map(Result<PagedResult<{{schema.ClrTypeName}}Response>>.Failure({{schema.ClrTypeName}}ListQuery.Invalid()), httpContext);
                        }
        """;

    internal static string ServiceValidation(FullNetCrudSchema schema) => $$"""
                    query ??= new();
                    if (query.Validate() is { } queryError)
                        return Result<PagedResult<{{schema.ClrTypeName}}Response>>.Failure(queryError);
        """;

    internal static string Parameter(FullNetCrudQueryDefinition.Field field) => "Query_" + field.Column.DatabaseName + field.Suffix;
    internal static string ClientOptions(FullNetCrudSchema schema)
    {
        var sort = schema.Columns.Where(column => column.ResolvedUi.Sortable).Select(column => $"'{column.JsonPropertyName}'").ToArray();
        return $$"""
            export interface {{schema.ClrTypeName}}ListOptions {
              sortBy?: {{(sort.Length == 0 ? "never" : string.Join(" | ", sort))}};
              descending?: boolean;
            {{string.Join("\n", Fields(schema).Select(field => $"  {field.QueryName}?: {ClientType(field.Column)};"))}}
            }
            """;
    }
    internal static string ClientType(FullNetColumn column) => column.ScalarType switch
    {
        FullNetScalarType.Int32 => "number", FullNetScalarType.Boolean => "boolean", _ => "string",
    };
    private static string ParameterValue(FullNetCrudQueryDefinition.Field field) => field.Column.ResolvedUi.QueryKind == FullNetColumnQueryKind.Contains
        ? $"Pattern({field.ClrName})" : field.Column.ScalarType == FullNetScalarType.DateTimeUtc ? $"{field.ClrName}?.ToUniversalTime()" : field.ClrName;
    private static string QueryAttribute(FullNetCrudSchema schema, string name) => schema.DataScope == FullNetCrudDataScope.Unspecified
        ? string.Empty : $"[Microsoft.AspNetCore.Mvc.FromQuery(Name = \"{name}\")] ";
    private static string SqlColumn(FullNetColumn column, string provider) => column.DatabaseName + (column.ScalarType == FullNetScalarType.String
        ? provider == "SqlServer" ? " COLLATE Latin1_General_100_BIN2" : " COLLATE utf8mb4_bin" : string.Empty);
    internal static string Type(FullNetColumn column) => column.ScalarType switch
    {
        FullNetScalarType.String => "string", FullNetScalarType.Uuid => "Guid", FullNetScalarType.Int32 => "int",
        FullNetScalarType.Int64 => "long", FullNetScalarType.Decimal => "decimal", FullNetScalarType.Boolean => "bool",
        FullNetScalarType.DateTimeUtc => "DateTimeOffset", _ => throw new ArgumentException("查询标量类型无效。"),
    };
}

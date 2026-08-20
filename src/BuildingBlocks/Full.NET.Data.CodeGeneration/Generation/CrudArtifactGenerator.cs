using System.Text;
using System.Text.Json;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 从已经确认名称的 CRUD Schema 生成首批跨栈契约、SQL 与客户端 API 产物。
/// </summary>
public static class CrudArtifactGenerator
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// 生成不依赖当前时间、文化、机器路径或随机数的内存产物清单。
    /// </summary>
    /// <param name="schema">已经通过 Naming Profile 与 CRUD 不变量校验的输入。</param>
    /// <param name="includeLayuiClientArtifacts">是否生成 Layui 客户端产物；默认 false（Frozen 客户端仅授权维护时启用）。</param>
    /// <returns>按相对路径稳定排序且路径唯一的只读产物集合。</returns>
    public static IReadOnlyList<GeneratedArtifact> Generate(
        FullNetCrudSchema schema,
        bool includeLayuiClientArtifacts = false)
    {
        ArgumentNullException.ThrowIfNull(schema);
        EnsureSupportedExplicitCapabilities(schema);

        var artifacts = new List<GeneratedArtifact>
        {
            new GeneratedArtifact(
                $"backend/{schema.ClrTypeName}Contracts.g.cs",
                GeneratedArtifactKind.Backend,
                GenerateContracts(schema)),
            new GeneratedArtifact(
                $"backend/{schema.ClrTypeName}Sql.g.cs",
                GeneratedArtifactKind.Backend,
                GenerateSql(schema)),
        };
        if (includeLayuiClientArtifacts)
        {
            artifacts.Add(new GeneratedArtifact(
                $"clients/layui/{schema.ApiResourceName}-page.generated.js",
                GeneratedArtifactKind.LayuiClient,
                CrudClientPageModelGenerator.GenerateLayui(schema)));
            artifacts.Add(new GeneratedArtifact(
                $"clients/layui/{schema.ApiResourceName}.generated.js",
                GeneratedArtifactKind.LayuiClient,
                GenerateLayuiClient(schema)));
        }

        artifacts.AddRange(
        [
            new GeneratedArtifact(
                $"clients/vue/{schema.ApiResourceName}-page.generated.ts",
                GeneratedArtifactKind.VueClient,
                CrudClientPageModelGenerator.GenerateVue(schema)),
            new GeneratedArtifact(
                $"clients/vue/{schema.ApiResourceName}.generated.ts",
                GeneratedArtifactKind.VueClient,
                GenerateVueClient(schema)),
            new GeneratedArtifact(
                $"clients/vue/{schema.ApiResourceName}View.vue",
                GeneratedArtifactKind.VueView,
                CrudVueViewGenerator.Generate(schema)),
            new GeneratedArtifact(
                $"contracts/openapi/{schema.ApiResourceName}.generated.openapi.json",
                GeneratedArtifactKind.OpenApiContract,
                CrudOpenApiContractGenerator.Generate(schema)),
            new GeneratedArtifact(
                $"backend/{schema.ClrTypeName}AuthorizationContributor.fragment.cs",
                GeneratedArtifactKind.Backend,
                CrudAuthorizationContributorFragmentGenerator.Generate(schema)),
            new GeneratedArtifact(
                $"reports/{schema.ApiResourceName}.generation.json",
                GeneratedArtifactKind.Report,
                GenerateReport(schema)),
        ]);
        if (schema.DataScope != FullNetCrudDataScope.Unspecified)
        {
            artifacts.Add(new GeneratedArtifact(
                $"backend/{schema.ClrTypeName}Endpoint.g.cs",
                GeneratedArtifactKind.Backend,
                CrudBackendFeatureGenerator.GenerateEndpoint(schema)));
            artifacts.Add(new GeneratedArtifact(
                $"backend/{schema.ClrTypeName}Feature.g.cs",
                GeneratedArtifactKind.Backend,
                CrudBackendFeatureGenerator.GenerateFeature(schema)));
            artifacts.Add(new GeneratedArtifact(
                $"backend/{schema.ClrTypeName}Record.g.cs",
                GeneratedArtifactKind.Backend,
                CrudBackendFeatureGenerator.GenerateRecord(schema)));
            artifacts.Add(new GeneratedArtifact(
                $"templates/migrations/MySql/Create{schema.ClrTypeName}.sql.template",
                GeneratedArtifactKind.MigrationTemplate,
                CrudMigrationTemplateGenerator.GenerateMySql(schema)));
            artifacts.Add(new GeneratedArtifact(
                $"templates/migrations/SqlServer/Create{schema.ClrTypeName}.sql.template",
                GeneratedArtifactKind.MigrationTemplate,
                CrudMigrationTemplateGenerator.GenerateSqlServer(schema)));
            artifacts.Add(new GeneratedArtifact(
                $"templates/tests/{schema.ClrTypeName}MigrationIntegrationTests.cs.template",
                GeneratedArtifactKind.IntegrationTestTemplate,
                CrudMigrationTemplateGenerator.GenerateIntegrationTest(schema)));
        }

        return Array.AsReadOnly(artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray());
    }

    private static string GenerateContracts(FullNetCrudSchema schema)
    {
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitContracts(schema);
        }

        var responseParameters = RenderCSharpParameters(schema.Columns);
        var mutableColumns = MutableColumns(schema).ToArray();
        var createParameters = RenderCSharpParameters(mutableColumns);
        var updateColumns = schema.HasVersion
            ? mutableColumns.Concat([RequiredColumn(schema, "Version")])
            : mutableColumns;
        var updateParameters = RenderCSharpParameters(updateColumns);
        var disableContract = schema.HasVersion
            ? $"""


            public sealed record Disable{schema.ClrTypeName}Request(
            {RenderCSharpParameters([RequiredColumn(schema, "Version")])});
            """
            : string.Empty;
        var errorCodes = CrudBackendFeatureGenerator.GenerateErrorCodes(schema);

        return Normalize(
            $$""""
            #nullable enable

            using System;
            using System.Text.Json.Serialization;

            namespace {{schema.RootNamespace}}.Generated;

            public static class {{schema.ClrTypeName}}Permissions
            {
                public const string Read = "{{schema.ReadPermission}}";
                public const string Write = "{{schema.WritePermission}}";
            }

            public sealed record {{schema.ClrTypeName}}Response(
            {{responseParameters}});

            public sealed record Create{{schema.ClrTypeName}}Request(
            {{createParameters}});

            public sealed record Update{{schema.ClrTypeName}}Request(
            {{updateParameters}});{{disableContract}}{{errorCodes}}
            """");
    }

    private static string GenerateSql(FullNetCrudSchema schema)
    {
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitSql(schema);
        }

        var projection = string.Join(
            ",\n            ",
            schema.Columns.Select(column => column.DatabaseName));
        var tenantFilter = schema.IsTenantScoped
            ? "\n            AND TenantId = @TenantId"
            : string.Empty;
        var countTenantFilter = schema.IsTenantScoped
            ? "\n        WHERE TenantId = @TenantId"
            : string.Empty;
        var insertColumns = string.Join(
            ", ",
            schema.Columns.Select(column => column.DatabaseName));
        var insertParameters = string.Join(
            ", ",
            schema.Columns.Select(column => $"@{column.ClrPropertyName}"));
        var mutableColumns = MutableColumns(schema).ToArray();
        if (mutableColumns.Length == 0)
        {
            throw new ArgumentException(
                "CRUD Schema 至少需要一个可更新字段。",
                nameof(schema));
        }

        var updateAssignments = string.Join(
            ",\n            ",
            mutableColumns.Select(column =>
                $"{column.DatabaseName} = @{column.ClrPropertyName}")
                .Concat(schema.HasVersion ? ["Version = Version + 1"] : []));
        var versionFilter = schema.HasVersion
            ? "\n            AND Version = @Version"
            : string.Empty;
        var disableAssignment = schema.HasVersion
            ? "IsActive = 0,\n            Version = Version + 1"
            : "IsActive = 0";
        var dataUsing = schema.DataScope != FullNetCrudDataScope.Unspecified
            ? "using Full.NET.Data.Abstractions;\n\n"
            : string.Empty;
        var tenantStatements =
            CrudBackendFeatureGenerator.GenerateSqlStatementMembers(schema);

        return Normalize(
            $$""""
            {{dataUsing}}namespace {{schema.RootNamespace}}.Generated;

            public static class {{schema.ClrTypeName}}Sql
            {
                public const string FindById = """
                    SELECT
                        {{projection}}
                    FROM {{schema.DatabaseTableName}}
                    WHERE Id = @Id{{tenantFilter}};
                    """;

                public const string Count = """
                    SELECT COUNT(1)
                    FROM {{schema.DatabaseTableName}}{{countTenantFilter}};
                    """;

                public const string ListSqlServer = """
                    SELECT
                        {{projection}}
                    FROM {{schema.DatabaseTableName}}
                    WHERE 1 = 1{{tenantFilter}}
                    ORDER BY Id
                    OFFSET @Offset ROWS
                    FETCH NEXT @PageSize ROWS ONLY;
                    """;

                public const string ListMySql = """
                    SELECT
                        {{projection}}
                    FROM {{schema.DatabaseTableName}}
                    WHERE 1 = 1{{tenantFilter}}
                    ORDER BY Id
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                public const string Insert = """
                    INSERT INTO {{schema.DatabaseTableName}} (
                        {{insertColumns}})
                    VALUES (
                        {{insertParameters}});
                    """;

                public const string Update = """
                    UPDATE {{schema.DatabaseTableName}}
                    SET {{updateAssignments}}
                    WHERE Id = @Id{{tenantFilter}}{{versionFilter}};
                    """;

                public const string Disable = """
                    UPDATE {{schema.DatabaseTableName}}
                    SET {{disableAssignment}}
                    WHERE Id = @Id{{tenantFilter}}{{versionFilter}};
                    """;{{tenantStatements}}
            }
            """");
    }

    private static string GenerateVueClient(FullNetCrudSchema schema) =>
        GenerateOpenApiVueClient(schema);

    private static string GenerateOpenApiVueClient(FullNetCrudSchema schema)
    {
        var entity = schema.ClrTypeName;
        var resource = HttpSegmentToPascalCase(schema.ApiResourceName);
        var operationPrefix = LowerFirst(HttpSegmentToPascalCase(schema.ModuleKey));
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var action = schema.UsesLegacyEntityCapabilities ? "Disable" : "Delete";
        var actionMember = schema.UsesLegacyEntityCapabilities ? "disable" : "delete";
        var operationImports = new List<string>
        {
            $"{operationPrefix}Create{entity}",
            $"{operationPrefix}List{resource}",
        };
        var typeImports = new List<string>
        {
            $"Create{entity}Request",
            "HttpClient",
            $"{entity}Response",
        };
        if (schema.EntityCapabilities.CanUpdate)
        {
            operationImports.Add($"{operationPrefix}Update{entity}");
            typeImports.Add($"Update{entity}Request");
        }
        if (schema.EntityCapabilities.CanDelete)
        {
            operationImports.Add($"{operationPrefix}{action}{entity}");
            if (schema.HasVersion)
            {
                typeImports.Add($"{action}{entity}Request");
            }
        }

        var imports = string.Join(",\n", operationImports
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => $"  {value}")
            .Concat(typeImports
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(value => $"  type {value}")));
        var exportedTypes = string.Join(",\n", typeImports
            .Where(value => value != "HttpClient")
            .OrderBy(value => value, StringComparer.Ordinal)
            .Select(value => $"  type {value}"));
        var updateMember = schema.EntityCapabilities.CanUpdate
            ? $$"""
            update: (id: string, input: Update{{entity}}Request) =>
              {{operationPrefix}}Update{{entity}}(
                http,
                { {{LowerFirst(entity)}}Id: id, body: input }
              )
            """
            : string.Empty;
        var actionInputType = schema.HasVersion
            ? $", input: {action}{entity}Request"
            : string.Empty;
        var actionBody = schema.HasVersion ? ", body: input" : string.Empty;
        var deleteMember = schema.EntityCapabilities.CanDelete
            ? $$"""
            {{actionMember}}: (id: string{{actionInputType}}) =>
              {{operationPrefix}}{{action}}{{entity}}(
                http,
                { {{LowerFirst(entity)}}Id: id{{actionBody}} }
              )
            """
            : string.Empty;
        var additionalMembers = new[] { updateMember, deleteMember }
            .Where(value => value.Length > 0)
            .ToArray();
        var renderedAdditionalMembers = additionalMembers.Length == 0
            ? string.Empty
            : ",\n" + IndentLines(string.Join(",\n", additionalMembers), 4);
        var permissionMembers = schema.UsesLegacyEntityCapabilities
            ? $$"""
              read: '{{schema.ReadPermission}}',
              write: '{{schema.WritePermission}}'
            """
            : $$"""
              read: '{{schema.ReadPermission}}',
              create: '{{schema.CreatePermission}}',
              update: '{{schema.UpdatePermission}}',
              disable: '{{schema.DisablePermission}}',
              write: '{{schema.WritePermission}}'
            """;

        return Normalize(
            $$"""
            import {
            {{imports}}
            } from '@fullnet/client-contracts';

            export {
            {{exportedTypes}}
            } from '@fullnet/client-contracts';

            export type GeneratedRequest = HttpClient;

            export const {{LowerFirst(entity)}}Permissions = {
            {{permissionMembers}}
            } as const;

            export function create{{apiFactoryName}}Api(
              http: GeneratedRequest
            ) {
              return {
                list: (page = 1, pageSize = 20) =>
                  {{operationPrefix}}List{{resource}}(http, { page, pageSize }),
                create: (input: Create{{entity}}Request) =>
                  {{operationPrefix}}Create{{entity}}(http, { body: input }){{renderedAdditionalMembers}}
              };
            }
            """);
    }

    private static string GenerateLegacyVueClient(FullNetCrudSchema schema)
    {
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitVueClient(schema);
        }

        var responseProperties = RenderTypeScriptProperties(schema.Columns);
        var mutableColumns = MutableColumns(schema).ToArray();
        var createProperties = RenderTypeScriptProperties(mutableColumns);
        var updateColumns = schema.HasVersion
            ? mutableColumns.Concat([RequiredColumn(schema, "Version")])
            : mutableColumns;
        var updateProperties = RenderTypeScriptProperties(updateColumns);
        var disableInterface = schema.HasVersion
            ? $$"""


            export interface Disable{{schema.ClrTypeName}}Request {
            {{RenderTypeScriptProperties([RequiredColumn(schema, "Version")])}}
            }
            """
            : string.Empty;
        var disableMember = IndentLines(
            schema.HasVersion
                ? $$"""
                disable: (id: string, input: Disable{{schema.ClrTypeName}}Request) =>
                  request<{{schema.ClrTypeName}}Response>(
                    `${basePath}/${encodeURIComponent(id)}/disable`,
                    jsonRequest('POST', input)
                  )
                """
                : $$"""
                disable: (id: string) =>
                  request<{{schema.ClrTypeName}}Response>(
                    `${basePath}/${encodeURIComponent(id)}/disable`,
                    { method: 'POST' }
                  )
                """,
            4);
        var apiPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);

        return Normalize(
            $$"""
            export interface {{schema.ClrTypeName}}Response {
            {{responseProperties}}
            }

            export interface Create{{schema.ClrTypeName}}Request {
            {{createProperties}}
            }

            export interface Update{{schema.ClrTypeName}}Request {
            {{updateProperties}}
            }{{disableInterface}}

            export interface GeneratedPage<T> {
              items: T[];
              page: number;
              pageSize: number;
              total: number;
            }

            export type GeneratedRequest = <T>(
              path: string,
              init?: RequestInit
            ) => Promise<T>;

            export const {{LowerFirst(schema.ClrTypeName)}}Permissions = {
              read: '{{schema.ReadPermission}}',
              write: '{{schema.WritePermission}}'
            } as const;

            export function create{{apiFactoryName}}Api(
              request: GeneratedRequest
            ) {
              const basePath = '{{apiPath}}';
              return {
                list: (page = 1, pageSize = 20) =>
                  request<GeneratedPage<{{schema.ClrTypeName}}Response>>(
                    `${basePath}?page=${page}&pageSize=${pageSize}`
                  ),
                create: (input: Create{{schema.ClrTypeName}}Request) =>
                  request<{{schema.ClrTypeName}}Response>(basePath, jsonRequest('POST', input)),
                update: (id: string, input: Update{{schema.ClrTypeName}}Request) =>
                  request<{{schema.ClrTypeName}}Response>(
                    `${basePath}/${encodeURIComponent(id)}`,
                    jsonRequest('PUT', input)
                  ),
            {{disableMember}}
              };
            }

            function jsonRequest(method: 'POST' | 'PUT', body: unknown): RequestInit {
              return {
                method,
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify(body)
              };
            }
            """);
    }

    private static string GenerateLayuiClient(FullNetCrudSchema schema)
    {
        if (!schema.UsesLegacyEntityCapabilities)
        {
            return GenerateExplicitLayuiClient(schema);
        }

        var apiPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var disableMember = IndentLines(
            schema.HasVersion
                ? """
                disable(id, input) {
                  return request(
                    `${basePath}/${encodeURIComponent(id)}/disable`,
                    jsonRequest('POST', input)
                  );
                }
                """
                : """
                disable(id) {
                  return request(
                    `${basePath}/${encodeURIComponent(id)}/disable`,
                    { method: 'POST' }
                  );
                }
                """,
            4);
        return Normalize(
            $$"""
            export const {{LowerFirst(schema.ClrTypeName)}}Permissions = Object.freeze({
              read: '{{schema.ReadPermission}}',
              write: '{{schema.WritePermission}}'
            });

            export function create{{apiFactoryName}}Api(request) {
              const basePath = '{{apiPath}}';
              return Object.freeze({
                list(page = 1, pageSize = 20) {
                  return request(`${basePath}?page=${page}&pageSize=${pageSize}`);
                },
                create(input) {
                  return request(basePath, jsonRequest('POST', input));
                },
                update(id, input) {
                  return request(
                    `${basePath}/${encodeURIComponent(id)}`,
                    jsonRequest('PUT', input)
                  );
                },
            {{disableMember}}
              });
            }

            function jsonRequest(method, body) {
              return {
                method,
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify(body)
              };
            }
            """);
    }

    private static string GenerateReport(FullNetCrudSchema schema)
    {
        var entityCapabilities = schema.UsesLegacyEntityCapabilities
            ? null
            : new
            {
                deleteMode = FullNetCrudWireValues.ToWireValue(
                    schema.EntityCapabilities.DeleteMode),
                schema.EntityCapabilities.HasCreatedAudit,
                schema.EntityCapabilities.HasUpdatedAudit,
                schema.EntityCapabilities.HasDeletedAudit,
                schema.EntityCapabilities.HasVersion,
                ownershipMode = FullNetCrudWireValues.ToWireValue(
                    schema.EntityCapabilities.OwnershipMode),
            };
        var report = new
        {
            schema.OwnerKey,
            schema.ModuleKey,
            schema.EntityKey,
            schema.DatabaseTableName,
            schema.RootNamespace,
            schema.ClrTypeName,
            schema.ApiResourceName,
            schema.PermissionResourceName,
            schema.ReadPermission,
            schema.CreatePermission,
            schema.UpdatePermission,
            schema.DisablePermission,
            schema.WritePermission,
            schema.IsTenantScoped,
            dataScope = FullNetCrudWireValues.ToWireValue(schema.DataScope),
            schema.HasVersion,
            schema.UsesLegacyEntityCapabilities,
            legacyLifecycle = schema.UsesLegacyEntityCapabilities
                ? "disable"
                : null,
            scene = FullNetCrudWireValues.ToWireValue(schema.Scene),
            relationships = schema.Relationships.Select(relationship => new
            {
                relationship.PrincipalEntityKey,
                relationship.PrincipalColumnName,
                principalDataScope = FullNetCrudWireValues.ToWireValue(
                    relationship.PrincipalDataScope),
                relationship.DependentEntityKey,
                relationship.DependentColumnName,
                dependentDataScope = FullNetCrudWireValues.ToWireValue(
                    relationship.DependentDataScope),
                compositeKeyColumnNames = relationship.CompositeKeyColumnNames,
                cascadeDelete = relationship.CascadeDelete,
            }),
            entityCapabilities,
            migrationTemplateGenerated =
                schema.DataScope != FullNetCrudDataScope.Unspecified,
            integrationTestTemplateGenerated =
                schema.DataScope != FullNetCrudDataScope.Unspecified,
            columns = schema.Columns.Select(column => new
            {
                column.DatabaseName,
                column.ClrPropertyName,
                column.JsonPropertyName,
                scalarType = FullNetCrudWireValues.ToWireValue(
                    column.ScalarType),
                column.IsNullable,
                column.MaxLength,
                column.NumericPrecision,
                column.NumericScale,
            }),
        };
        return Normalize(JsonSerializer.Serialize(report, ReportJsonOptions));
    }

    private static string GenerateExplicitContracts(FullNetCrudSchema schema)
    {
        var responseParameters = RenderCSharpParameters(schema.Columns);
        var writableColumns = WritableColumns(schema).ToArray();
        var createParameters = RenderCSharpParameters(writableColumns);
        var updateContract = schema.EntityCapabilities.CanUpdate
            ? $$"""


            public sealed record Update{{schema.ClrTypeName}}Request(
            {{RenderCSharpParameters(
                writableColumns.Concat(
                    schema.HasVersion
                        ? [RequiredColumn(schema, "Version")]
                        : []))}});
            """
            : string.Empty;
        var deleteContract = schema.EntityCapabilities.CanDelete
            && schema.HasVersion
            ? $$"""


            public sealed record Delete{{schema.ClrTypeName}}Request(
            {{RenderCSharpParameters([RequiredColumn(schema, "Version")])}});
            """
            : string.Empty;
        var errorCodes = CrudBackendFeatureGenerator.GenerateErrorCodes(schema);

        return Normalize(
            $$""""
            #nullable enable

            using System;
            using System.Text.Json.Serialization;

            namespace {{schema.RootNamespace}}.Generated;

            public static class {{schema.ClrTypeName}}Permissions
            {
                public const string Read = "{{schema.ReadPermission}}";
                public const string Create = "{{schema.CreatePermission}}";
                public const string Update = "{{schema.UpdatePermission}}";
                public const string Disable = "{{schema.DisablePermission}}";
            }

            public sealed record {{schema.ClrTypeName}}Response(
            {{responseParameters}});

            public sealed record Create{{schema.ClrTypeName}}Request(
            {{createParameters}});{{updateContract}}{{deleteContract}}{{errorCodes}}
            """");
    }

    private static string GenerateExplicitSql(FullNetCrudSchema schema)
    {
        var projection = string.Join(
            ",\n            ",
            schema.Columns.Select(column => column.DatabaseName));
        var tenantPredicate = schema.IsTenantScoped
            ? "\n            AND TenantId = @TenantId"
            : string.Empty;
        var deletedPredicate =
            schema.EntityCapabilities.DeleteMode
                == FullNetCrudDeleteMode.SoftDelete
                ? "\n            AND IsDeleted = 0"
                : string.Empty;
        var countPredicates = new List<string>();
        if (schema.IsTenantScoped)
        {
            countPredicates.Add("TenantId = @TenantId");
        }

        if (schema.EntityCapabilities.DeleteMode
            == FullNetCrudDeleteMode.SoftDelete)
        {
            countPredicates.Add("IsDeleted = 0");
        }

        var countWhere = countPredicates.Count == 0
            ? string.Empty
            : "\n        WHERE " + string.Join(
                "\n            AND ",
                countPredicates);
        var insertColumns = string.Join(
            ", ",
            schema.Columns.Select(column => column.DatabaseName));
        var insertParameters = string.Join(
            ", ",
            schema.Columns.Select(column => $"@{column.ClrPropertyName}"));
        const string orderBy = "Id";
        var updateSql = GenerateExplicitUpdateSql(
            schema,
            tenantPredicate,
            deletedPredicate);
        var deleteSql = GenerateExplicitDeleteSql(
            schema,
            tenantPredicate,
            deletedPredicate);
        var dataUsing = schema.DataScope != FullNetCrudDataScope.Unspecified
            ? "using Full.NET.Data.Abstractions;\n\n"
            : string.Empty;
        var statements =
            CrudBackendFeatureGenerator.GenerateSqlStatementMembers(schema);

        return Normalize(
            $$""""
            {{dataUsing}}namespace {{schema.RootNamespace}}.Generated;

            public static class {{schema.ClrTypeName}}Sql
            {
                public const string FindById = """
                    SELECT
                        {{projection}}
                    FROM {{schema.DatabaseTableName}}
                    WHERE Id = @Id{{tenantPredicate}}{{deletedPredicate}};
                    """;

                public const string Count = """
                    SELECT COUNT(1)
                    FROM {{schema.DatabaseTableName}}{{countWhere}};
                    """;

                public const string ListSqlServer = """
                    SELECT
                        {{projection}}
                    FROM {{schema.DatabaseTableName}}
                    WHERE 1 = 1{{tenantPredicate}}{{deletedPredicate}}
                    ORDER BY {{orderBy}}
                    OFFSET @Offset ROWS
                    FETCH NEXT @PageSize ROWS ONLY;
                    """;

                public const string ListMySql = """
                    SELECT
                        {{projection}}
                    FROM {{schema.DatabaseTableName}}
                    WHERE 1 = 1{{tenantPredicate}}{{deletedPredicate}}
                    ORDER BY {{orderBy}}
                    LIMIT @PageSize OFFSET @Offset;
                    """;

                public const string Insert = """
                    INSERT INTO {{schema.DatabaseTableName}} (
                        {{insertColumns}})
                    VALUES (
                        {{insertParameters}});
                    """;{{updateSql}}{{deleteSql}}{{statements}}
            }
            """");
    }

    private static string GenerateExplicitUpdateSql(
        FullNetCrudSchema schema,
        string tenantPredicate,
        string deletedPredicate)
    {
        if (!schema.EntityCapabilities.CanUpdate)
        {
            return string.Empty;
        }

        var assignments = WritableColumns(schema)
            .Select(column =>
                $"{column.DatabaseName} = @{column.ClrPropertyName}")
            .ToList();
        if (schema.EntityCapabilities.HasUpdatedAudit)
        {
            assignments.Add("UpdatedAtUtc = @UpdatedAtUtc");
            assignments.Add("UpdatedById = @UpdatedById");
        }

        if (schema.HasVersion)
        {
            assignments.Add("Version = Version + 1");
        }

        if (assignments.Count == 0)
        {
            throw new ArgumentException(
                "可更新实体至少需要一个业务字段、更新审计或 Version。",
                nameof(schema));
        }

        var versionPredicate = schema.HasVersion
            ? "\n            AND Version = @Version"
            : string.Empty;
        return $$""""


                public const string Update = """
                    UPDATE {{schema.DatabaseTableName}}
                    SET {{string.Join(",\n            ", assignments)}}
                    WHERE Id = @Id{{tenantPredicate}}{{versionPredicate}}{{deletedPredicate}};
                    """;
            """";
    }

    private static string GenerateExplicitDeleteSql(
        FullNetCrudSchema schema,
        string tenantPredicate,
        string deletedPredicate)
    {
        if (!schema.EntityCapabilities.CanDelete)
        {
            return string.Empty;
        }

        var versionPredicate = schema.HasVersion
            ? "\n            AND Version = @Version"
            : string.Empty;
        if (schema.EntityCapabilities.DeleteMode
            == FullNetCrudDeleteMode.HardDelete)
        {
            return $$""""


                public const string Delete = """
                    DELETE FROM {{schema.DatabaseTableName}}
                    WHERE Id = @Id{{tenantPredicate}}{{versionPredicate}};
                    """;
            """";
        }

        var assignments = new List<string> { "IsDeleted = 1" };
        if (schema.EntityCapabilities.HasDeletedAudit)
        {
            assignments.Add("DeletedAtUtc = @DeletedAtUtc");
            assignments.Add("DeletedById = @DeletedById");
        }

        if (schema.HasVersion)
        {
            assignments.Add("Version = Version + 1");
        }

        return $$""""


                public const string Delete = """
                    UPDATE {{schema.DatabaseTableName}}
                    SET {{string.Join(",\n            ", assignments)}}
                    WHERE Id = @Id{{tenantPredicate}}{{versionPredicate}}{{deletedPredicate}};
                    """;
            """";
    }

    private static string GenerateExplicitVueClient(FullNetCrudSchema schema)
    {
        var responseProperties = RenderTypeScriptProperties(schema.Columns);
        var writableColumns = WritableColumns(schema).ToArray();
        var createProperties = RenderTypeScriptProperties(writableColumns);
        var updateType = schema.EntityCapabilities.CanUpdate
            ? $$"""


            export interface Update{{schema.ClrTypeName}}Request {
            {{RenderTypeScriptProperties(
                writableColumns.Concat(
                    schema.HasVersion
                        ? [RequiredColumn(schema, "Version")]
                        : []))}}
            }
            """
            : string.Empty;
        var deleteType = schema.EntityCapabilities.CanDelete
            && schema.HasVersion
            ? $$"""


            export interface Delete{{schema.ClrTypeName}}Request {
            {{RenderTypeScriptProperties([RequiredColumn(schema, "Version")])}}
            }
            """
            : string.Empty;
        var updateMember = schema.EntityCapabilities.CanUpdate
            ? $$"""
                update: (id: string, input: Update{{schema.ClrTypeName}}Request) =>
                  request<{{schema.ClrTypeName}}Response>(
                    `${basePath}/${encodeURIComponent(id)}`,
                    jsonRequest('PUT', input)
                  ),
            """
            : string.Empty;
        var deleteArgument = schema.HasVersion
            ? $", input: Delete{schema.ClrTypeName}Request"
            : string.Empty;
        var deleteInit = schema.HasVersion
            ? "jsonRequest('POST', input)"
            : "{ method: 'POST' }";
        var deleteMember = schema.EntityCapabilities.CanDelete
            ? $$"""
                delete: (id: string{{deleteArgument}}) =>
                  request<{{schema.ClrTypeName}}Response>(
                    `${basePath}/${encodeURIComponent(id)}/delete`,
                    {{deleteInit}}
                  ),
            """
            : string.Empty;
        var apiPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);

        return Normalize(
            $$"""
            export interface {{schema.ClrTypeName}}Response {
            {{responseProperties}}
            }

            export interface Create{{schema.ClrTypeName}}Request {
            {{createProperties}}
            }{{updateType}}{{deleteType}}

            export interface GeneratedPage<T> {
              items: T[];
              page: number;
              pageSize: number;
              total: number;
            }

            export type GeneratedRequest = <T>(
              path: string,
              init?: RequestInit
            ) => Promise<T>;

            export const {{LowerFirst(schema.ClrTypeName)}}Permissions = {
              read: '{{schema.ReadPermission}}',
              create: '{{schema.CreatePermission}}',
              update: '{{schema.UpdatePermission}}',
              disable: '{{schema.DisablePermission}}',
              write: '{{schema.WritePermission}}'
            } as const;

            export function create{{apiFactoryName}}Api(
              request: GeneratedRequest
            ) {
              const basePath = '{{apiPath}}';
              return {
                list: (page = 1, pageSize = 20) =>
                  request<GeneratedPage<{{schema.ClrTypeName}}Response>>(
                    `${basePath}?page=${page}&pageSize=${pageSize}`
                  ),
                create: (input: Create{{schema.ClrTypeName}}Request) =>
                  request<{{schema.ClrTypeName}}Response>(basePath, jsonRequest('POST', input)),
            {{IndentLines(updateMember + deleteMember, 4)}}
              };
            }

            function jsonRequest(method: 'POST' | 'PUT', body: unknown): RequestInit {
              return {
                method,
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify(body)
              };
            }
            """);
    }

    private static string GenerateExplicitLayuiClient(FullNetCrudSchema schema)
    {
        var apiPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var apiFactoryName = HttpSegmentToPascalCase(schema.ApiResourceName);
        var updateMember = schema.EntityCapabilities.CanUpdate
            ? """
                update(id, input) {
                  return request(
                    `${basePath}/${encodeURIComponent(id)}`,
                    jsonRequest('PUT', input)
                  );
                },
            """
            : string.Empty;
        var deleteParameters = schema.HasVersion ? "id, input" : "id";
        var deleteInit = schema.HasVersion
            ? "jsonRequest('POST', input)"
            : "{ method: 'POST' }";
        var deleteMember = schema.EntityCapabilities.CanDelete
            ? $$"""
                delete({{deleteParameters}}) {
                  return request(
                    `${basePath}/${encodeURIComponent(id)}/delete`,
                    {{deleteInit}}
                  );
                },
            """
            : string.Empty;

        return Normalize(
            $$"""
            export const {{LowerFirst(schema.ClrTypeName)}}Permissions = Object.freeze({
              read: '{{schema.ReadPermission}}',
              create: '{{schema.CreatePermission}}',
              update: '{{schema.UpdatePermission}}',
              disable: '{{schema.DisablePermission}}',
              write: '{{schema.WritePermission}}'
            });

            export function create{{apiFactoryName}}Api(request) {
              const basePath = '{{apiPath}}';
              return Object.freeze({
                list(page = 1, pageSize = 20) {
                  return request(`${basePath}?page=${page}&pageSize=${pageSize}`);
                },
                create(input) {
                  return request(basePath, jsonRequest('POST', input));
                },
            {{IndentLines(updateMember + deleteMember, 4)}}
              });
            }

            function jsonRequest(method, body) {
              return {
                method,
                headers: { 'content-type': 'application/json' },
                body: JSON.stringify(body)
              };
            }
            """);
    }

    private static string RenderCSharpParameters(IEnumerable<FullNetColumn> columns) =>
        string.Join(
            ",\n",
            columns.Select(column =>
                $"    {CSharpSerializationAttributes(column)} "
                + $"{CSharpType(column)} {column.ClrPropertyName}"));

    private static string RenderTypeScriptProperties(
        IEnumerable<FullNetColumn> columns) =>
        string.Join(
            "\n",
            columns.Select(column =>
                $"  {column.JsonPropertyName}: {TypeScriptType(column)}"
                + $"{(column.IsNullable ? " | null" : string.Empty)};"));

    private static IEnumerable<FullNetColumn> MutableColumns(FullNetCrudSchema schema) =>
        schema.Columns.Where(column =>
            column.DatabaseName is not "Id"
            and not "TenantId"
            and not "Version"
            and not "CreatedAtUtc");

    private static IEnumerable<FullNetColumn> WritableColumns(
        FullNetCrudSchema schema) =>
        schema.Columns.Where(column =>
            column.DatabaseName is not "Id"
            and not "TenantId"
            and not "Version"
            and not "CreatedAtUtc"
            and not "CreatedById"
            and not "UpdatedAtUtc"
            and not "UpdatedById"
            and not "IsDeleted"
            and not "DeletedAtUtc"
            and not "DeletedById"
            and not "OrganizationUnitId");

    private static FullNetColumn RequiredColumn(
        FullNetCrudSchema schema,
        string databaseName) =>
        schema.Columns.Single(column => column.DatabaseName == databaseName);

    private static string CSharpType(FullNetColumn column)
    {
        var type = column.ScalarType switch
        {
            FullNetScalarType.Uuid => "Guid",
            FullNetScalarType.String => "string",
            FullNetScalarType.Int32 => "int",
            FullNetScalarType.Int64 => "long",
            FullNetScalarType.Boolean => "bool",
            FullNetScalarType.DateTimeUtc => "DateTimeOffset",
            FullNetScalarType.Decimal => "decimal",
            _ => throw new ArgumentOutOfRangeException(
                nameof(column),
                column.ScalarType,
                "不支持的 CRUD 标量类型。"),
        };
        return column.IsNullable ? $"{type}?" : type;
    }

    private static string TypeScriptType(FullNetColumn column) =>
        column.ScalarType switch
        {
            FullNetScalarType.Uuid or FullNetScalarType.String
                or FullNetScalarType.DateTimeUtc or FullNetScalarType.Int64
                or FullNetScalarType.Decimal => "string",
            FullNetScalarType.Int32 => "number",
            FullNetScalarType.Boolean => "boolean",
            _ => throw new ArgumentOutOfRangeException(
                nameof(column),
                column.ScalarType,
                "不支持的 CRUD 标量类型。"),
        };

    private static string CSharpSerializationAttributes(FullNetColumn column)
    {
        var numberHandling = column.ScalarType is FullNetScalarType.Int64
            or FullNetScalarType.Decimal
            ? ", JsonNumberHandling("
                + "JsonNumberHandling.AllowReadingFromString | "
                + "JsonNumberHandling.WriteAsString)"
            : string.Empty;
        return $"[property: JsonPropertyName(\"{column.JsonPropertyName}\")"
            + $"{numberHandling}]";
    }

    private static string HttpSegmentToPascalCase(string value) =>
        string.Concat(value.Split('-', StringSplitOptions.None).Select(UpperFirst));

    private static string UpperFirst(string value) =>
        string.Concat(char.ToUpperInvariant(value[0]), value[1..]);

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);

    private static string IndentLines(string content, int spaces)
    {
        var indentation = new string(' ', spaces);
        return indentation + content.Replace(
            "\n",
            $"\n{indentation}",
            StringComparison.Ordinal);
    }

    private static string Normalize(string content)
    {
        var builder = new StringBuilder(content.Length + 1);
        builder.Append(content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\r', '\n'));
        builder.Append('\n');
        return builder.ToString();
    }

    private static void EnsureSupportedExplicitCapabilities(
        FullNetCrudSchema schema)
    {
        if (schema.UsesLegacyEntityCapabilities)
        {
            return;
        }

        if (schema.Scene == FullNetCrudScene.Tree)
        {
            return;
        }

        if (schema.Scene is FullNetCrudScene.MasterDetail
            or FullNetCrudScene.ManyToMany)
        {
            EnsureRelationalGenerationReady(schema);
        }
    }

    /// <summary>
    /// 关系场景必须显式声明同作用域、复合键与级联语义，禁止跨模块猜测。
    /// </summary>
    private static void EnsureRelationalGenerationReady(FullNetCrudSchema schema)
    {
        if (schema.Relationships.Count == 0)
        {
            throw new NotSupportedException(
                "关系场景必须声明本模块聚合关系后再生成可执行产物。");
        }

        foreach (var relationship in schema.Relationships)
        {
            if (relationship.PrincipalDataScope != schema.DataScope
                || relationship.DependentDataScope != schema.DataScope)
            {
                throw new NotSupportedException(
                    "跨模块或跨数据作用域关系继续禁止生成可执行产物。");
            }

            if (relationship.CompositeKeyColumnNames is not { Count: > 0 }
                || relationship.CascadeDelete is null)
            {
                throw new NotSupportedException(
                    "关系场景必须等待聚合事务、复合键和级联语义显式声明后再生成可执行产物。");
            }
        }
    }
}

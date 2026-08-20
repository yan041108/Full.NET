using System.Text.Json;
using System.Text.Json.Nodes;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>
/// 从已确认的 CRUD Schema 生成标准 OpenAPI 契约，作为客户端 DTO、守卫与 Operation 的唯一生成输入。
/// </summary>
internal static class CrudOpenApiContractGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>生成不含时间、机器路径或随机值的 OpenAPI 3.1 文档。</summary>
    internal static string Generate(FullNetCrudSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var entity = schema.ClrTypeName;
        var resource = ToPascalCase(schema.ApiResourceName);
        var operationPrefix = LowerFirst(ToPascalCase(schema.ModuleKey));
        var tag = $"{ToPascalCase(schema.ModuleKey)}{resource}";
        var idParameter = $"{LowerFirst(entity)}Id";
        var collectionPath = $"/api/v1/{schema.ModuleKey}/{schema.ApiResourceName}";
        var itemPath = $"{collectionPath}/{{{idParameter}}}";

        var paths = new JsonObject
        {
            [collectionPath] = new JsonObject
            {
                ["get"] = Operation(
                    $"{operationPrefix}List{resource}",
                    tag,
                    schema.ReadPermission,
                    "200",
                    Ref($"PagedResultOf{entity}Response"),
                    parameters: PageParameters()),
                ["post"] = Operation(
                    $"{operationPrefix}Create{entity}",
                    tag,
                    CreatePermission(schema),
                    "201",
                    Ref($"{entity}Response"),
                    Ref($"Create{entity}Request")),
            },
            [itemPath] = BuildItemPath(schema, operationPrefix, tag, entity, idParameter),
        };

        if (schema.EntityCapabilities.CanDelete)
        {
            var action = schema.UsesLegacyEntityCapabilities ? "Disable" : "Delete";
            var actionPath = schema.UsesLegacyEntityCapabilities ? "disable" : "delete";
            var requestSchema = schema.HasVersion
                ? Ref($"{action}{entity}Request")
                : null;
            paths[$"{itemPath}/{actionPath}"] = new JsonObject
            {
                ["post"] = Operation(
                    $"{operationPrefix}{action}{entity}",
                    tag,
                    DeletePermission(schema),
                    "200",
                    Ref($"{entity}Response"),
                    requestSchema,
                    [PathParameter(idParameter)]),
            };
        }

        var root = new JsonObject
        {
            ["openapi"] = "3.1.0",
            ["info"] = new JsonObject
            {
                ["title"] = $"{schema.ModuleKey}.{schema.EntityKey} generated contract",
                ["version"] = "1.0.0",
            },
            ["paths"] = paths,
            ["components"] = new JsonObject
            {
                ["schemas"] = BuildSchemas(schema),
                ["securitySchemes"] = new JsonObject
                {
                    ["Bearer"] = new JsonObject
                    {
                        ["type"] = "http",
                        ["scheme"] = "bearer",
                        ["bearerFormat"] = "JWT",
                    },
                    ["ApiKey"] = new JsonObject
                    {
                        ["type"] = "apiKey",
                        ["in"] = "header",
                        ["name"] = "Authorization",
                    },
                },
            },
        };

        return root.ToJsonString(JsonOptions).ReplaceLineEndings("\n") + "\n";
    }

    private static JsonObject BuildItemPath(
        FullNetCrudSchema schema,
        string operationPrefix,
        string tag,
        string entity,
        string idParameter)
    {
        var item = new JsonObject
        {
            ["get"] = Operation(
                $"{operationPrefix}Get{entity}",
                tag,
                schema.ReadPermission,
                "200",
                Ref($"{entity}Response"),
                parameters: [PathParameter(idParameter)]),
        };
        if (schema.EntityCapabilities.CanUpdate)
        {
            item["put"] = Operation(
                $"{operationPrefix}Update{entity}",
                tag,
                UpdatePermission(schema),
                "200",
                Ref($"{entity}Response"),
                Ref($"Update{entity}Request"),
                [PathParameter(idParameter)]);
        }
        return item;
    }

    private static JsonObject Operation(
        string operationId,
        string tag,
        string permission,
        string successStatus,
        JsonObject responseSchema,
        JsonObject? requestSchema = null,
        IReadOnlyList<JsonObject>? parameters = null)
    {
        var operation = new JsonObject
        {
            ["operationId"] = operationId,
            ["tags"] = new JsonArray(tag),
            ["x-fullnet-permission"] = permission,
            ["security"] = new JsonArray
            {
                new JsonObject { ["Bearer"] = new JsonArray() },
                new JsonObject { ["ApiKey"] = new JsonArray() },
            },
            ["responses"] = Responses(successStatus, responseSchema),
        };
        if (parameters is { Count: > 0 })
        {
            operation["parameters"] = new JsonArray(
                parameters.Select(parameter => (JsonNode)parameter).ToArray());
        }
        if (requestSchema is not null)
        {
            operation["requestBody"] = new JsonObject
            {
                ["required"] = true,
                ["content"] = new JsonObject
                {
                    ["application/json"] = new JsonObject
                    {
                        ["schema"] = requestSchema,
                    },
                },
            };
        }
        return operation;
    }

    private static JsonObject Responses(string status, JsonObject schema) => new()
    {
        [status] = new JsonObject
        {
            ["description"] = status == "201" ? "Created" : "OK",
            ["content"] = new JsonObject
            {
                ["application/json"] = new JsonObject { ["schema"] = schema },
            },
        },
        ["401"] = ProblemResponse("Unauthorized"),
        ["403"] = ProblemResponse("Forbidden"),
    };

    private static JsonObject ProblemResponse(string description) => new()
    {
        ["description"] = description,
        ["content"] = new JsonObject
        {
            ["application/problem+json"] = new JsonObject
            {
                ["schema"] = Ref("ProblemDetails"),
            },
        },
    };

    private static IReadOnlyList<JsonObject> PageParameters() =>
    [
        QueryParameter("page", 1),
        QueryParameter("pageSize", 20),
    ];

    private static JsonObject QueryParameter(string name, int defaultValue) => new()
    {
        ["in"] = "query",
        ["name"] = name,
        ["required"] = false,
        ["schema"] = new JsonObject
        {
            ["type"] = "integer",
            ["format"] = "int32",
            ["default"] = defaultValue,
            ["minimum"] = 1,
        },
    };

    private static JsonObject PathParameter(string name) => new()
    {
        ["in"] = "path",
        ["name"] = name,
        ["required"] = true,
        ["schema"] = new JsonObject
        {
            ["type"] = "string",
            ["format"] = "uuid",
        },
    };

    private static JsonObject BuildSchemas(FullNetCrudSchema schema)
    {
        var entity = schema.ClrTypeName;
        var writableColumns = WritableColumns(schema).ToArray();
        var schemas = new JsonObject
        {
            [$"{entity}Response"] = ObjectSchema(schema.Columns),
            [$"Create{entity}Request"] = ObjectSchema(writableColumns),
            [$"PagedResultOf{entity}Response"] = PageSchema(entity),
            ["ProblemDetails"] = ProblemDetailsSchema(),
        };
        if (schema.EntityCapabilities.CanUpdate)
        {
            schemas[$"Update{entity}Request"] = ObjectSchema(
                writableColumns.Concat(
                    schema.HasVersion ? [RequiredColumn(schema, "Version")] : []));
        }
        if (schema.EntityCapabilities.CanDelete && schema.HasVersion)
        {
            var action = schema.UsesLegacyEntityCapabilities ? "Disable" : "Delete";
            schemas[$"{action}{entity}Request"] = ObjectSchema(
                [RequiredColumn(schema, "Version")]);
        }
        return schemas;
    }

    private static JsonObject ObjectSchema(IEnumerable<FullNetColumn> columns)
    {
        var materialized = columns.ToArray();
        var properties = new JsonObject();
        foreach (var column in materialized)
        {
            properties[column.JsonPropertyName] = ColumnSchema(column);
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(
                materialized.Select(column => (JsonNode)JsonValue.Create(
                    column.JsonPropertyName)!).ToArray()),
        };
    }

    private static JsonObject ColumnSchema(FullNetColumn column)
    {
        var type = column.ScalarType switch
        {
            FullNetScalarType.Int32 => "integer",
            FullNetScalarType.Boolean => "boolean",
            _ => "string",
        };
        var schema = new JsonObject
        {
            ["type"] = column.IsNullable
                ? new JsonArray("null", type)
                : JsonValue.Create(type),
        };
        switch (column.ScalarType)
        {
            case FullNetScalarType.Uuid:
                schema["format"] = "uuid";
                break;
            case FullNetScalarType.String when column.MaxLength is not null:
                schema["maxLength"] = column.MaxLength.Value;
                break;
            case FullNetScalarType.Int32:
                schema["format"] = "int32";
                break;
            case FullNetScalarType.Int64:
                schema["pattern"] = "^-?(?:0|[1-9]\\d*)$";
                break;
            case FullNetScalarType.DateTimeUtc:
                schema["format"] = "date-time";
                break;
            case FullNetScalarType.Decimal:
                schema["pattern"] = "^-?(?:0|[1-9]\\d*)(?:\\.\\d+)?$";
                break;
        }
        return schema;
    }

    private static JsonObject PageSchema(string entity) => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["items"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Ref($"{entity}Response"),
            },
            ["page"] = IntegerSchema("int32"),
            ["pageSize"] = IntegerSchema("int32"),
            ["total"] = IntegerSchema("int64"),
        },
        ["required"] = new JsonArray("items", "page", "pageSize", "total"),
    };

    private static JsonObject IntegerSchema(string format) => new()
    {
        ["type"] = "integer",
        ["format"] = format,
    };

    private static JsonObject ProblemDetailsSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["type"] = NullableStringSchema(),
            ["title"] = NullableStringSchema(),
            ["status"] = new JsonObject
            {
                ["type"] = new JsonArray("null", "integer"),
                ["format"] = "int32",
            },
            ["detail"] = NullableStringSchema(),
            ["instance"] = NullableStringSchema(),
        },
    };

    private static JsonObject NullableStringSchema() => new()
    {
        ["type"] = new JsonArray("null", "string"),
    };

    private static JsonObject Ref(string schemaName) => new()
    {
        ["$ref"] = $"#/components/schemas/{schemaName}",
    };

    private static IEnumerable<FullNetColumn> WritableColumns(FullNetCrudSchema schema) =>
        schema.UsesLegacyEntityCapabilities
            ? schema.Columns.Where(column => column.DatabaseName is not "Id"
                and not "TenantId" and not "Version" and not "CreatedAtUtc")
            : schema.Columns.Where(column => column.DatabaseName is not "Id"
                and not "TenantId" and not "Version" and not "CreatedAtUtc"
                and not "CreatedById" and not "UpdatedAtUtc" and not "UpdatedById"
                and not "IsDeleted" and not "DeletedAtUtc" and not "DeletedById"
                and not "OrganizationUnitId");

    private static FullNetColumn RequiredColumn(
        FullNetCrudSchema schema,
        string databaseName) =>
        schema.Columns.Single(column => column.DatabaseName == databaseName);

    private static string CreatePermission(FullNetCrudSchema schema) =>
        schema.UsesLegacyEntityCapabilities ? schema.WritePermission : schema.CreatePermission;

    private static string UpdatePermission(FullNetCrudSchema schema) =>
        schema.UsesLegacyEntityCapabilities ? schema.WritePermission : schema.UpdatePermission;

    private static string DeletePermission(FullNetCrudSchema schema) =>
        schema.UsesLegacyEntityCapabilities ? schema.WritePermission : schema.DisablePermission;

    private static string ToPascalCase(string value) =>
        string.Concat(value.Split('-', StringSplitOptions.None).Select(UpperFirst));

    private static string UpperFirst(string value) =>
        string.Concat(char.ToUpperInvariant(value[0]), value[1..]);

    private static string LowerFirst(string value) =>
        string.Concat(char.ToLowerInvariant(value[0]), value[1..]);
}

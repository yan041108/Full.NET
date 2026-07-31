using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 将严格 JSON 文档转换到已经执行命名和 CRUD 不变量校验的领域 Schema。
/// </summary>
internal sealed class CrudSchemaDocument
{
    private bool? _hasVersion;
    private bool _hasVersionSpecified;
    private CrudEntityCapabilitiesDocument? _entityCapabilities;
    private bool _entityCapabilitiesSpecified;
    private FullNetCrudScene _scene = FullNetCrudScene.Single;
    private bool _sceneSpecified;
    private IReadOnlyList<CrudRelationshipDocument>? _relationships;
    private bool _relationshipsSpecified;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public required string OwnerKey { get; init; }

    public required string ModuleKey { get; init; }

    public required string EntityKey { get; init; }

    public required string DatabaseTableName { get; init; }

    public required string RootNamespace { get; init; }

    public required string ClrTypeName { get; init; }

    public required string ApiResourceName { get; init; }

    public required string PermissionResourceName { get; init; }

    public bool? IsTenantScoped { get; init; }

    public FullNetCrudDataScope? DataScope { get; init; }

    public bool? HasVersion
    {
        get => _hasVersion;
        init
        {
            _hasVersion = value;
            _hasVersionSpecified = true;
        }
    }

    public CrudEntityCapabilitiesDocument? EntityCapabilities
    {
        get => _entityCapabilities;
        init
        {
            _entityCapabilities = value;
            _entityCapabilitiesSpecified = true;
        }
    }

    public FullNetCrudScene Scene
    {
        get => _scene;
        init
        {
            _scene = value;
            _sceneSpecified = true;
        }
    }

    public IReadOnlyList<CrudRelationshipDocument>? Relationships
    {
        get => _relationships;
        init
        {
            _relationships = value;
            _relationshipsSpecified = true;
        }
    }

    public required IReadOnlyList<CrudColumnDocument> Columns { get; init; }

    public static async Task<FullNetCrudSchema> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(
            path,
            cancellationToken);
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            throw new DecoderFallbackException(
                "CRUD Schema JSON 不得包含 UTF-8 BOM。");
        }

        var json = StrictUtf8.GetString(bytes);
        var document = JsonSerializer.Deserialize<CrudSchemaDocument>(
            json,
            JsonOptions)
            ?? throw new JsonException(
                "CRUD Schema JSON 不能为空。");
        return document.ToSchema();
    }

    private FullNetCrudSchema ToSchema()
    {
        ArgumentNullException.ThrowIfNull(Columns);
        var columns = Columns.Select(column =>
        {
            ArgumentNullException.ThrowIfNull(column);
            return column.ToColumn();
        }).ToArray();
        var dataScope = ResolveDataScope();

        if (_hasVersionSpecified && _entityCapabilitiesSpecified)
        {
            throw new JsonException(
                "CRUD Schema 的 hasVersion 与 entityCapabilities 不得同时提供。");
        }

        if (_entityCapabilitiesSpecified)
        {
            if (EntityCapabilities is null)
            {
                throw new JsonException(
                    "CRUD Schema 的 entityCapabilities 不得为 null。");
            }

            if (_relationshipsSpecified && Relationships is null)
            {
                throw new JsonException(
                    "CRUD Schema 的 relationships 不得为 null。");
            }

            var relationships = Relationships?
                .Select(relationship =>
                {
                    ArgumentNullException.ThrowIfNull(relationship);
                    return relationship.ToRelationship();
                })
                .ToArray()
                ?? [];
            return FullNetCrudSchema.CreateProject(
                OwnerKey,
                ModuleKey,
                EntityKey,
                DatabaseTableName,
                RootNamespace,
                ClrTypeName,
                ApiResourceName,
                PermissionResourceName,
                dataScope,
                EntityCapabilities.ToCapabilities(),
                Scene,
                relationships,
                columns);
        }

        if (!_hasVersionSpecified || HasVersion is null)
        {
            throw new JsonException(
                "CRUD Schema 必须提供 entityCapabilities 或兼容字段 hasVersion。");
        }

        if (_sceneSpecified || _relationshipsSpecified)
        {
            throw new JsonException(
                "兼容字段 hasVersion 不得与 scene 或 relationships 同时提供。");
        }

        return FullNetCrudSchema.CreateProject(
            OwnerKey,
            ModuleKey,
            EntityKey,
            DatabaseTableName,
            RootNamespace,
            ClrTypeName,
            ApiResourceName,
            PermissionResourceName,
            dataScope,
            HasVersion.Value,
            columns);
    }

    private FullNetCrudDataScope ResolveDataScope()
    {
        if (DataScope is not null && IsTenantScoped is not null)
        {
            throw new JsonException(
                "CRUD Schema 的 dataScope 与 isTenantScoped 不得同时提供。");
        }

        if (DataScope is not null)
        {
            return DataScope.Value;
        }

        if (IsTenantScoped is not null)
        {
            return IsTenantScoped.Value
                ? FullNetCrudDataScope.TenantRequired
                : FullNetCrudDataScope.Unspecified;
        }

        throw new JsonException(
            "CRUD Schema 必须提供 dataScope 或兼容字段 isTenantScoped。");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        StableCrudJsonConverters.AddTo(options);
        return options;
    }
}

internal static class StableCrudJsonConverters
{
    internal static void AddTo(JsonSerializerOptions options)
    {
        options.Converters.Add(
            new StableWireEnumJsonConverter<FullNetCrudScene>(
                FullNetCrudWireValues.TryParse,
                FullNetCrudWireValues.ToWireValue));
        options.Converters.Add(
            new StableWireEnumJsonConverter<FullNetCrudDeleteMode>(
                FullNetCrudWireValues.TryParse,
                FullNetCrudWireValues.ToWireValue));
        options.Converters.Add(
            new StableWireEnumJsonConverter<FullNetCrudOwnershipMode>(
                FullNetCrudWireValues.TryParse,
                FullNetCrudWireValues.ToWireValue));
        options.Converters.Add(
            new StableWireEnumJsonConverter<FullNetCrudDataScope>(
                FullNetCrudWireValues.TryParse,
                FullNetCrudWireValues.ToWireValue));
        options.Converters.Add(
            new StableWireEnumJsonConverter<FullNetScalarType>(
                FullNetCrudWireValues.TryParse,
                FullNetCrudWireValues.ToWireValue));
    }
}

internal delegate bool TryParseStableWireValue<T>(
    string value,
    out T result)
    where T : struct, Enum;

/// <summary>
/// 严格读取规范机器值，并仅为已存在的 pre-1.0 PascalCase Schema 保留显式兼容别名。
/// </summary>
internal sealed class StableWireEnumJsonConverter<T>(
    TryParseStableWireValue<T> parser,
    Func<T, string> formatter)
    : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"{typeof(T).Name} 必须使用稳定字符串机器值。");
        }

        var value = reader.GetString();
        if (value is null || !parser(value, out var result))
        {
            throw new JsonException(
                $"{typeof(T).Name} 的机器值不受支持。");
        }

        return result;
    }

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(formatter(value));
}

/// <summary>
/// 保存关系两端的稳定实体键、列名与显式数据作用域。
/// </summary>
internal sealed class CrudRelationshipDocument
{
    public required string PrincipalEntityKey { get; init; }

    public required string PrincipalColumnName { get; init; }

    public required FullNetCrudDataScope PrincipalDataScope { get; init; }

    public required string DependentEntityKey { get; init; }

    public required string DependentColumnName { get; init; }

    public required FullNetCrudDataScope DependentDataScope { get; init; }

    public FullNetCrudRelationship ToRelationship() =>
        new(
            PrincipalEntityKey,
            PrincipalColumnName,
            PrincipalDataScope,
            DependentEntityKey,
            DependentColumnName,
            DependentDataScope);
}

/// <summary>
/// 保存不能从列结构推导的实体生命周期、审计、并发与归属能力。
/// </summary>
internal sealed class CrudEntityCapabilitiesDocument
{
    public required FullNetCrudDeleteMode DeleteMode { get; init; }

    public required bool HasCreatedAudit { get; init; }

    public required bool HasUpdatedAudit { get; init; }

    public required bool HasDeletedAudit { get; init; }

    public required bool HasVersion { get; init; }

    public required FullNetCrudOwnershipMode OwnershipMode { get; init; }

    public FullNetCrudEntityCapabilities ToCapabilities() =>
        new(
            DeleteMode,
            HasCreatedAudit,
            HasUpdatedAudit,
            HasDeletedAudit,
            HasVersion,
            OwnershipMode);
}

/// <summary>
/// 保存 CLI 输入中显式确认的字段名称与跨库逻辑类型。
/// </summary>
internal sealed class CrudColumnDocument
{
    public required string DatabaseName { get; init; }

    public required string ClrPropertyName { get; init; }

    public required string JsonPropertyName { get; init; }

    public required FullNetScalarType ScalarType { get; init; }

    public bool IsNullable { get; init; }

    public int? MaxLength { get; init; }

    public int? NumericPrecision { get; init; }

    public int? NumericScale { get; init; }

    public FullNetColumn ToColumn()
    {
        return new FullNetColumn(
            DatabaseName,
            ClrPropertyName,
            JsonPropertyName,
            ScalarType,
            IsNullable,
            MaxLength,
            NumericPrecision,
            NumericScale);
    }
}

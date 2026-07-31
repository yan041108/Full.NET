using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Data.CodeGeneration.Naming;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 从严格 JSON 读取逐表显式语义，禁止把物理表名拆分为未经确认的业务名称。
/// </summary>
internal sealed class DatabaseBatchMappingDocument
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly JsonSerializerOptions JsonOptions =
        CreateJsonOptions();

    public required IReadOnlyList<DatabaseBatchTableDocument> Tables
    {
        get;
        init;
    }

    public static async Task<IReadOnlyList<DatabaseCrudImportOptions>> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF)
        {
            throw new DecoderFallbackException(
                "数据库批量映射 JSON 不得包含 UTF-8 BOM。");
        }

        var json = StrictUtf8.GetString(bytes);
        var document = JsonSerializer.Deserialize<DatabaseBatchMappingDocument>(
            json,
            JsonOptions)
            ?? throw new JsonException(
                "数据库批量映射 JSON 不能为空。");
        return document.ToMappings();
    }

    private IReadOnlyList<DatabaseCrudImportOptions> ToMappings()
    {
        ArgumentNullException.ThrowIfNull(Tables);
        if (Tables.Count == 0)
        {
            throw new ArgumentException(
                "数据库批量映射的 tables 至少需要一项。",
                nameof(Tables));
        }

        var mappings = new List<DatabaseCrudImportOptions>(Tables.Count);
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in Tables)
        {
            ArgumentNullException.ThrowIfNull(table);
            if (table.DataScope == FullNetCrudDataScope.Unspecified)
            {
                throw new ArgumentException(
                    "数据库批量映射必须显式选择 TenantRequired、HostOnly 或 Global。",
                    nameof(Tables));
            }

            var physicalTableName = SchemaName.CreateProject(
                table.OwnerKey,
                table.ModuleKey,
                table.EntityKey).Value;
            if (!tableNames.Add(physicalTableName))
            {
                throw new ArgumentException(
                    $"数据库批量映射包含重复物理表：{physicalTableName}",
                    nameof(Tables));
            }

            mappings.Add(table.ToImportOptions());
        }

        return mappings;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };
        StableCrudJsonConverters.AddTo(options);
        return options;
    }
}

/// <summary>
/// 保存单张物理表无法从数据库结构可靠推导的全部业务语义。
/// </summary>
internal sealed class DatabaseBatchTableDocument
{
    private bool? _hasVersion;
    private bool _hasVersionSpecified;
    private CrudEntityCapabilitiesDocument? _entityCapabilities;
    private bool _entityCapabilitiesSpecified;

    public required string OwnerKey { get; init; }

    public required string ModuleKey { get; init; }

    public required string EntityKey { get; init; }

    public required string RootNamespace { get; init; }

    public required string ClrTypeName { get; init; }

    public required string ApiResourceName { get; init; }

    public required string PermissionResourceName { get; init; }

    public required FullNetCrudDataScope DataScope { get; init; }

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

    public DatabaseCrudImportOptions ToImportOptions()
    {
        if (_hasVersionSpecified && _entityCapabilitiesSpecified)
        {
            throw new JsonException(
                "数据库批量映射的 hasVersion 与 entityCapabilities 不得同时提供。");
        }

        if (_entityCapabilitiesSpecified)
        {
            if (EntityCapabilities is null)
            {
                throw new JsonException(
                    "数据库批量映射的 entityCapabilities 不得为 null。");
            }

            return new DatabaseCrudImportOptions(
                OwnerKey,
                ModuleKey,
                EntityKey,
                RootNamespace,
                ClrTypeName,
                ApiResourceName,
                PermissionResourceName,
                DataScope,
                EntityCapabilities.ToCapabilities());
        }

        if (!_hasVersionSpecified || HasVersion is null)
        {
            throw new JsonException(
                "数据库批量映射必须提供 entityCapabilities 或兼容字段 hasVersion。");
        }

        return new DatabaseCrudImportOptions(
            OwnerKey,
            ModuleKey,
            EntityKey,
            RootNamespace,
            ClrTypeName,
            ApiResourceName,
            PermissionResourceName,
            DataScope,
            HasVersion.Value);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.CodeGeneration.Schema;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Serialization;

namespace Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;

/// <summary>
/// 将兼容输入收敛为同一个经过领域校验的 Schema，并生成可稳定持久化的规范 JSON。
/// </summary>
internal sealed class CodeGenerationSchemaNormalizer
{
    private const int MaximumColumnCount = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly Error InvalidSchema = new(
        CodeGenerationErrorCodes.InvalidPreviewSchema,
        "The CRUD preview schema is invalid.",
        ErrorType.Validation);

    public Result<NormalizedCodeGenerationSchema> Normalize(
        CodeGenerationPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var usesLegacyCapabilities = request.HasVersion.HasValue;
        var usesExplicitCapabilities = request.EntityCapabilities is not null;
        if (usesLegacyCapabilities == usesExplicitCapabilities
            || (usesLegacyCapabilities
                && (request.Scene is not null
                    || request.Relationships is not null))
            || (usesExplicitCapabilities
                && (request.Scene is null
                    || request.Relationships is null))
            || request.Columns is not
                { Count: > 0 and <= MaximumColumnCount }
            || !TryParseDataScope(request.DataScope, out var dataScope))
        {
            return Failure();
        }

        var columns = new FullNetColumn[request.Columns.Count];
        var canonicalColumns =
            new CodeGenerationPreviewColumnRequest[request.Columns.Count];
        for (var index = 0; index < request.Columns.Count; index++)
        {
            var input = request.Columns[index];
            if (input is null
                || !TryParseScalarType(input.ScalarType, out var scalarType))
            {
                return Failure();
            }

            columns[index] = new FullNetColumn(
                input.DatabaseName,
                input.ClrPropertyName,
                input.JsonPropertyName,
                scalarType,
                input.IsNullable,
                input.MaxLength,
                input.NumericPrecision,
                input.NumericScale);
            canonicalColumns[index] = input with
            {
                ScalarType = ToWireValue(scalarType),
            };
        }

        try
        {
            return usesLegacyCapabilities
                ? NormalizeLegacy(
                    request,
                    dataScope,
                    columns,
                    canonicalColumns)
                : NormalizeExplicit(
                    request,
                    dataScope,
                    columns,
                    canonicalColumns);
        }
        catch (ArgumentException)
        {
            // 输入错误只映射为稳定机器码，避免把命名或能力细节反射给非受信调用方。
            return Failure();
        }
    }

    private static Result<NormalizedCodeGenerationSchema> NormalizeLegacy(
        CodeGenerationPreviewRequest request,
        FullNetCrudDataScope dataScope,
        IReadOnlyList<FullNetColumn> columns,
        IReadOnlyList<CodeGenerationPreviewColumnRequest> canonicalColumns)
    {
        var schema = FullNetCrudSchema.CreateProject(
            request.OwnerKey,
            request.ModuleKey,
            request.EntityKey,
            request.DatabaseTableName,
            request.RootNamespace,
            request.ClrTypeName,
            request.ApiResourceName,
            request.PermissionResourceName,
            dataScope,
            request.HasVersion!.Value,
            columns);
        var canonicalRequest = request with
        {
            DataScope = ToWireValue(dataScope),
            Columns = canonicalColumns,
            EntityCapabilities = null,
            Scene = null,
            Relationships = null,
        };
        return Success(schema, canonicalRequest);
    }

    private static Result<NormalizedCodeGenerationSchema> NormalizeExplicit(
        CodeGenerationPreviewRequest request,
        FullNetCrudDataScope dataScope,
        IReadOnlyList<FullNetColumn> columns,
        IReadOnlyList<CodeGenerationPreviewColumnRequest> canonicalColumns)
    {
        var input = request.EntityCapabilities!;
        if (!TryParseDeleteMode(input.DeleteMode, out var deleteMode)
            || !TryParseOwnershipMode(
                input.OwnershipMode,
                out var ownershipMode)
            || !TryParseScene(request.Scene, out var scene))
        {
            return Failure();
        }

        var capabilities = new FullNetCrudEntityCapabilities(
            deleteMode,
            input.HasCreatedAudit,
            input.HasUpdatedAudit,
            input.HasDeletedAudit,
            input.HasVersion,
            ownershipMode);
        var relationships =
            new FullNetCrudRelationship[request.Relationships!.Count];
        var canonicalRelationships =
            new CodeGenerationRelationshipRequest[relationships.Length];
        for (var index = 0; index < relationships.Length; index++)
        {
            var relationship = request.Relationships[index];
            if (relationship is null
                || !TryParseDataScope(
                    relationship.PrincipalDataScope,
                    out var principalDataScope)
                || !TryParseDataScope(
                    relationship.DependentDataScope,
                    out var dependentDataScope))
            {
                return Failure();
            }

            relationships[index] = new FullNetCrudRelationship(
                relationship.PrincipalEntityKey,
                relationship.PrincipalColumnName,
                principalDataScope,
                relationship.DependentEntityKey,
                relationship.DependentColumnName,
                dependentDataScope);
            canonicalRelationships[index] = relationship with
            {
                PrincipalDataScope = ToWireValue(principalDataScope),
                DependentDataScope = ToWireValue(dependentDataScope),
            };
        }

        var schema = FullNetCrudSchema.CreateProject(
            request.OwnerKey,
            request.ModuleKey,
            request.EntityKey,
            request.DatabaseTableName,
            request.RootNamespace,
            request.ClrTypeName,
            request.ApiResourceName,
            request.PermissionResourceName,
            dataScope,
            capabilities,
            scene,
            relationships,
            columns);
        var canonicalRequest = request with
        {
            DataScope = ToWireValue(dataScope),
            HasVersion = null,
            Columns = canonicalColumns,
            EntityCapabilities = input with
            {
                DeleteMode = ToWireValue(deleteMode),
                OwnershipMode = ToWireValue(ownershipMode),
            },
            Scene = ToWireValue(scene),
            Relationships = canonicalRelationships,
        };
        return Success(schema, canonicalRequest);
    }

    private static Result<NormalizedCodeGenerationSchema> Success(
        FullNetCrudSchema schema,
        CodeGenerationPreviewRequest canonicalRequest)
    {
        var canonicalJson = JsonSerializer.Serialize(
            canonicalRequest,
            CodeGenerationJsonSerializerContext.Default
                .CodeGenerationPreviewRequest);
        var hash = Convert.ToHexString(
                SHA256.HashData(StrictUtf8.GetBytes(canonicalJson)))
            .ToLowerInvariant();
        return Result<NormalizedCodeGenerationSchema>.Success(
            new NormalizedCodeGenerationSchema(
                schema,
                canonicalRequest,
                canonicalJson,
                hash));
    }

    private static Result<NormalizedCodeGenerationSchema> Failure() =>
        Result<NormalizedCodeGenerationSchema>.Failure(InvalidSchema);

    private static bool TryParseDataScope(
        string? value,
        out FullNetCrudDataScope result)
    {
        result = value switch
        {
            "tenant.required" or "TenantRequired" =>
                FullNetCrudDataScope.TenantRequired,
            "host.only" or "HostOnly" => FullNetCrudDataScope.HostOnly,
            "global" or "Global" => FullNetCrudDataScope.Global,
            _ => FullNetCrudDataScope.Unspecified,
        };
        return result != FullNetCrudDataScope.Unspecified;
    }

    private static bool TryParseScalarType(
        string? value,
        out FullNetScalarType result)
    {
        result = value switch
        {
            "uuid" or "Uuid" => FullNetScalarType.Uuid,
            "string" or "String" => FullNetScalarType.String,
            "int32" or "Int32" => FullNetScalarType.Int32,
            "int64" or "Int64" => FullNetScalarType.Int64,
            "boolean" or "Boolean" => FullNetScalarType.Boolean,
            "date.time.utc" or "DateTimeUtc" =>
                FullNetScalarType.DateTimeUtc,
            "decimal" or "Decimal" => FullNetScalarType.Decimal,
            _ => default,
        };
        return result != default;
    }

    private static bool TryParseDeleteMode(
        string? value,
        out FullNetCrudDeleteMode result)
    {
        result = value switch
        {
            "hard.delete" or "HardDelete" =>
                FullNetCrudDeleteMode.HardDelete,
            "soft.delete" or "SoftDelete" =>
                FullNetCrudDeleteMode.SoftDelete,
            "immutable" or "Immutable" => FullNetCrudDeleteMode.Immutable,
            _ => default,
        };
        return value is "hard.delete"
            or "HardDelete"
            or "soft.delete"
            or "SoftDelete"
            or "immutable"
            or "Immutable";
    }

    private static bool TryParseOwnershipMode(
        string? value,
        out FullNetCrudOwnershipMode result)
    {
        result = value switch
        {
            "none" or "None" => FullNetCrudOwnershipMode.None,
            "organization.unit" or "OrganizationUnit" =>
                FullNetCrudOwnershipMode.OrganizationUnit,
            _ => default,
        };
        return value is "none"
            or "None"
            or "organization.unit"
            or "OrganizationUnit";
    }

    private static bool TryParseScene(
        string? value,
        out FullNetCrudScene result)
    {
        result = value switch
        {
            "single" or "Single" => FullNetCrudScene.Single,
            "tree" or "Tree" => FullNetCrudScene.Tree,
            "master.detail" or "MasterDetail" =>
                FullNetCrudScene.MasterDetail,
            "many.to.many" or "ManyToMany" =>
                FullNetCrudScene.ManyToMany,
            _ => default,
        };
        return value is "single"
            or "Single"
            or "tree"
            or "Tree"
            or "master.detail"
            or "MasterDetail"
            or "many.to.many"
            or "ManyToMany";
    }

    private static string ToWireValue(FullNetCrudDataScope value) =>
        value switch
        {
            FullNetCrudDataScope.TenantRequired => "tenant.required",
            FullNetCrudDataScope.HostOnly => "host.only",
            FullNetCrudDataScope.Global => "global",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string ToWireValue(FullNetScalarType value) =>
        value switch
        {
            FullNetScalarType.Uuid => "uuid",
            FullNetScalarType.String => "string",
            FullNetScalarType.Int32 => "int32",
            FullNetScalarType.Int64 => "int64",
            FullNetScalarType.Boolean => "boolean",
            FullNetScalarType.DateTimeUtc => "date.time.utc",
            FullNetScalarType.Decimal => "decimal",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string ToWireValue(FullNetCrudDeleteMode value) =>
        value switch
        {
            FullNetCrudDeleteMode.HardDelete => "hard.delete",
            FullNetCrudDeleteMode.SoftDelete => "soft.delete",
            FullNetCrudDeleteMode.Immutable => "immutable",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string ToWireValue(FullNetCrudOwnershipMode value) =>
        value switch
        {
            FullNetCrudOwnershipMode.None => "none",
            FullNetCrudOwnershipMode.OrganizationUnit =>
                "organization.unit",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

    private static string ToWireValue(FullNetCrudScene value) =>
        value switch
        {
            FullNetCrudScene.Single => "single",
            FullNetCrudScene.Tree => "tree",
            FullNetCrudScene.MasterDetail => "master.detail",
            FullNetCrudScene.ManyToMany => "many.to.many",
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
}

/// <summary>
/// 保存一次归一化得到的领域 Schema、规范请求、稳定 JSON 与内容摘要。
/// </summary>
internal sealed record NormalizedCodeGenerationSchema(
    FullNetCrudSchema Schema,
    CodeGenerationPreviewRequest CanonicalRequest,
    string CanonicalJson,
    string SchemaSha256);

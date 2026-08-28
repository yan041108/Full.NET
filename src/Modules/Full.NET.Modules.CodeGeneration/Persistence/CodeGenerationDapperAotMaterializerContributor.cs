#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;
using Full.NET.Modules.CodeGeneration.Persistence;

namespace Full.NET.Modules.CodeGeneration.Persistence;

/// <summary>
/// CodeGeneration 模块 Native AOT 行物化器注册。
/// </summary>
internal sealed class CodeGenerationDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<CodeGenerationCatalogTableRow>(ReadCatalogTableRow);
        registrar.Register<CodeGenerationCatalogColumnRow>(ReadCatalogColumnRow);
        registrar.Register<CodeGenerationTemplateRecord>(ReadTemplate);
        registrar.Register<CodeGenerationRunRecord>(ReadRun);
        registrar.Register<CodeGenerationCheckpointCleanupCandidate>(ReadCheckpointCleanupCandidate);
    }

    private static CodeGenerationCatalogTableRow ReadCatalogTableRow(DbDataReader reader) =>
        new()
        {
            TableName = reader.GetString(0),
        };

    private static CodeGenerationCatalogColumnRow ReadCatalogColumnRow(DbDataReader reader) => new()
    {
        ColumnName = ReadString(reader, "ColumnName"),
        DataType = ReadString(reader, "DataType"),
        ColumnType = ReadString(reader, "ColumnType"),
        IsNullable = ReadString(reader, "IsNullable"),
        MaxLength = ReadNullableInt64(reader, "MaxLength"),
        NumericPrecision = ReadNullableInt32(reader, "NumericPrecision"),
        NumericScale = ReadNullableInt32(reader, "NumericScale"),
        OrdinalPosition = ReadInt32(reader, "OrdinalPosition"),
    };

    private static CodeGenerationTemplateRecord ReadTemplate(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        Name = ReadString(reader, "Name"),
        Description = ReadNullableString(reader, "Description"),
        SchemaJson = ReadString(reader, "SchemaJson"),
        SchemaSha256 = ReadString(reader, "SchemaSha256"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        CreatedByUserId = ReadGuid(reader, "CreatedByUserId"),
        UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
        UpdatedByUserId = ReadNullableGuid(reader, "UpdatedByUserId"),
        Version = ReadInt64(reader, "Version"),
    };

    private static CodeGenerationRunRecord ReadRun(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        TemplateId = ReadNullableGuid(reader, "TemplateId"),
        TemplateVersion = ReadNullableInt64(reader, "TemplateVersion"),
        OperationKind = ReadString(reader, "OperationKind"),
        Status = ReadString(reader, "Status"),
        ModuleKey = ReadNullableString(reader, "ModuleKey"),
        EntityKey = ReadNullableString(reader, "EntityKey"),
        SchemaSha256 = ReadNullableString(reader, "SchemaSha256"),
        ArtifactCount = ReadInt32(reader, "ArtifactCount"),
        ManifestSha256 = ReadNullableString(reader, "ManifestSha256"),
        ErrorCode = ReadNullableString(reader, "ErrorCode"),
        RequestedByUserId = ReadGuid(reader, "RequestedByUserId"),
        StartedAtUtc = ReadDateTimeOffset(reader, "StartedAtUtc"),
        FinishedAtUtc = ReadDateTimeOffset(reader, "FinishedAtUtc"),
        SourceApplyRunId = ReadNullableGuid(reader, "SourceApplyRunId"),
    };

    private static CodeGenerationCheckpointCleanupCandidate ReadCheckpointCleanupCandidate(
        DbDataReader reader) => new()
    {
        ApplyRunId = ReadGuid(reader, "ApplyRunId"),
    };

    private static Guid ReadGuid(DbDataReader reader, string name) =>
        reader.GetGuid(reader.GetOrdinal(name));

    private static Guid? ReadNullableGuid(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadNullableGuid(reader, reader.GetOrdinal(name));

    private static string ReadString(DbDataReader reader, string name) =>
        reader.GetString(reader.GetOrdinal(name));

    private static string? ReadNullableString(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadNullableString(reader, reader.GetOrdinal(name));

    private static int ReadInt32(DbDataReader reader, string name) =>
        Convert.ToInt32(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static int? ReadNullableInt32(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static long ReadInt64(DbDataReader reader, string name) =>
        Convert.ToInt64(reader.GetValue(reader.GetOrdinal(name)), CultureInfo.InvariantCulture);

    private static long? ReadNullableInt64(DbDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadDateTimeOffset(reader, reader.GetOrdinal(name));

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, string name) =>
        AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, reader.GetOrdinal(name));
}
#endif

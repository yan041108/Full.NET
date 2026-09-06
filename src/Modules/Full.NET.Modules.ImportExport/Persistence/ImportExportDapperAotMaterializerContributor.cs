#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>ImportExport Native AOT 行物化器。</summary>
internal sealed class ImportExportDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar) =>
        registrar.Register<ImportExportTaskRecord>(ReadTask);

    private static ImportExportTaskRecord ReadTask(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        TenantId = ReadGuid(reader, "TenantId"),
        SchemaKey = ReadString(reader, "SchemaKey"),
        SchemaDisplayName = ReadString(reader, "SchemaDisplayName"),
        WorksheetKey = ReadString(reader, "WorksheetKey"),
        SourceFileId = ReadGuid(reader, "SourceFileId"),
        SourceFileName = ReadNullableString(reader, "SourceFileName"),
        StatusKey = ReadString(reader, "StatusKey"),
        TotalRows = ReadInt32(reader, "TotalRows"),
        ValidRowCount = ReadInt32(reader, "ValidRowCount"),
        InvalidRowCount = ReadInt32(reader, "InvalidRowCount"),
        PreviewRowsJson = ReadNullableString(reader, "PreviewRowsJson"),
        ErrorCode = ReadNullableString(reader, "ErrorCode"),
        RequestedByUserId = ReadGuid(reader, "RequestedByUserId"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        PreviewCompletedAtUtc = ReadNullableDateTimeOffset(reader, "PreviewCompletedAtUtc"),
        Version = ReadInt64(reader, "Version"),
    };

    private static int RequiredOrdinal(DbDataReader reader, string name) => reader.GetOrdinal(name);

    private static Guid ReadGuid(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return reader.GetGuid(ordinal);
    }

    private static string ReadString(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return reader.GetString(ordinal);
    }

    private static string? ReadNullableString(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadNullableString(reader, ordinal);
    }

    private static int ReadInt32(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static long ReadInt64(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadDateTimeOffset(reader, ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, ordinal);
    }
}
#endif

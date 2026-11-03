#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.ImportExport.Persistence;

/// <summary>ImportExport Native AOT 行物化器。</summary>
internal sealed class ImportExportDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<ImportExportTaskRecord>(ReadTask);
        registrar.Register<ImportTaskCancellationState>(reader => new()
        {
            Id = ReadGuid(reader, "Id"), StatusKey = ReadString(reader, "StatusKey"), Version = ReadInt64(reader, "Version"),
        });
        registrar.Register<ImportProfileRecord>(reader => new() {
            Id = ReadGuid(reader, "Id"), Name = ReadString(reader, "Name"), HeaderFingerprint = ReadString(reader, "HeaderFingerprint"),
            CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"), UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
            Version = ReadInt64(reader, "Version"), ConfigurationJson = reader.FieldCount == 7 ? ReadNullableString(reader, "ConfigurationJson") : null });
        registrar.Register<ImportExportTaskListRecord>(ReadMyTask);
    }

    private static ImportExportTaskListRecord ReadMyTask(DbDataReader reader) => new()
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
        ErrorCode = ReadNullableString(reader, "ErrorCode"),
        RequestedByUserId = ReadGuid(reader, "RequestedByUserId"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        PreviewCompletedAtUtc = ReadNullableDateTimeOffset(reader, "PreviewCompletedAtUtc"),
        ProcessedRowCount = ReadInt32(reader, "ProcessedRowCount"),
        SucceededRowCount = ReadInt32(reader, "SucceededRowCount"),
        ExecutionFailedRowCount = ReadInt32(reader, "ExecutionFailedRowCount"),
        NextLineNumber = ReadInt32(reader, "NextLineNumber"),
        ErrorReceiptFileId = ReadNullableGuid(reader, "ErrorReceiptFileId"),
        ExecutionStartedAtUtc = ReadNullableDateTimeOffset(reader, "ExecutionStartedAtUtc"),
        ExecutionCompletedAtUtc = ReadNullableDateTimeOffset(reader, "ExecutionCompletedAtUtc"),
        Version = ReadInt64(reader, "Version"),
    };

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
        ProcessedRowCount = ReadInt32(reader, "ProcessedRowCount"),
        SucceededRowCount = ReadInt32(reader, "SucceededRowCount"),
        ExecutionFailedRowCount = ReadInt32(reader, "ExecutionFailedRowCount"),
        NextLineNumber = ReadInt32(reader, "NextLineNumber"),
        ExecutionRowsJson = ReadNullableString(reader, "ExecutionRowsJson"),
        ColumnMappingJson = ReadNullableString(reader, "ColumnMappingJson"),
        ActorSessionBindingJson = ReadNullableString(reader, "ActorSessionBindingJson"),
        ErrorReceiptFileId = ReadNullableGuid(reader, "ErrorReceiptFileId"),
        ExecutionStartedAtUtc = ReadNullableDateTimeOffset(reader, "ExecutionStartedAtUtc"),
        ExecutionCompletedAtUtc = ReadNullableDateTimeOffset(reader, "ExecutionCompletedAtUtc"),
        LeaseId = ReadNullableGuid(reader, "LeaseId"),
        LeaseExpiresAtUtc = ReadNullableDateTimeOffset(reader, "LeaseExpiresAtUtc"),
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

    private static Guid? ReadNullableGuid(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }
}
#endif

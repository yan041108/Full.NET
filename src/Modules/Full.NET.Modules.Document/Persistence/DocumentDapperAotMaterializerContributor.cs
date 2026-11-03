#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Document.Persistence;

/// <summary>
/// Document Native AOT 行物化器。按稳定列名解析可兼容同一记录在不同查询中的显式投影。
/// </summary>
internal sealed class DocumentDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<DocumentCategoryRecord>(ReadCategory);
        registrar.Register<Contracts.HostDocumentSearchHit>(reader => new(reader.GetGuid(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2)));
        registrar.Register<DocumentTagRecord>(ReadTag);
        registrar.Register<DocumentNameConflictRecord>(ReadNameConflict);
        registrar.Register<DocumentTagAssignmentRow>(ReadTagAssignment);
        registrar.Register<DocumentItemRecord>(ReadItem);
        registrar.Register<DocumentItemDetailRecord>(ReadItemDetail);
        registrar.Register<DocumentVersionRecord>(ReadVersion);
        registrar.Register<DocumentVersionOperationRecord>(reader => new DocumentVersionOperationRecord {
            Id = ReadGuid(reader, "Id"), RequestKey = ReadGuid(reader, "RequestKey"), DocumentId = ReadGuid(reader, "DocumentId"), ActorUserId = ReadGuid(reader, "ActorUserId"),
            VersionId = ReadGuid(reader, "VersionId"), FileId = ReadGuid(reader, "FileId"), ChangeDescription = ReadNullableString(reader, "ChangeDescription"),
            CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc") });
        registrar.Register<DocumentVersionRetentionCandidateRecord>(ReadRetentionCandidate);
        registrar.Register<DocumentAccessLogRecord>(ReadAccessLog);
        registrar.Register<DocumentPreviewTaskRecord>(ReadPreviewTask);
        registrar.Register<DocumentPreviewReferenceRecord>(r => new DocumentPreviewReferenceRecord {
            Id = ReadGuid(r, "Id"), DocumentId = ReadGuid(r, "DocumentId"), TaskId = ReadGuid(r, "TaskId"), FileId = ReadGuid(r, "FileId"),
            Purpose = ReadString(r, "Purpose"), StateKey = ReadString(r, "StateKey"), ReleaseAfterUtc = ReadNullableDateTimeOffset(r, "ReleaseAfterUtc"), CreatedAtUtc = ReadDateTimeOffset(r, "CreatedAtUtc") });
        registrar.Register<DocumentPermissionRecord>(ReadPermission);
        registrar.Register<DocumentShareRecord>(ReadShare);
        registrar.Register<DocumentShareSessionRecord>(reader => new DocumentShareSessionRecord {
            Id = ReadGuid(reader, "Id"), ShareId = ReadGuid(reader, "ShareId"), DocumentId = ReadGuid(reader, "DocumentId"),
            VersionId = ReadGuid(reader, "VersionId"), RequestKey = ReadGuid(reader, "RequestKey"),
            AccessRevision = ReadInt64(reader, "AccessRevision"), PolicyRevision = ReadInt64(reader, "PolicyRevision"),
            CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"), ExpiresAtUtc = ReadDateTimeOffset(reader, "ExpiresAtUtc") });
        registrar.Register<DocumentStatisticsSummaryRecord>(ReadStatisticsSummary);
        registrar.Register<DocumentStatisticsByTypeRecord>(ReadStatisticsByType);
        registrar.Register<DocumentStatisticsByCategoryRecord>(ReadStatisticsByCategory);
        registrar.Register<DocumentStatisticsShareCountRecord>(ReadStatisticsShareCount);
        registrar.Register<DocumentVersionRetentionSettingRecord>(ReadVersionRetentionSetting);
    }

    private static DocumentCategoryRecord ReadCategory(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        ParentId = ReadNullableGuid(reader, "ParentId"),
        Name = ReadString(reader, "Name"),
        SortOrder = ReadInt32(reader, "SortOrder"),
        Code = ReadNullableString(reader, "Code"),
        Icon = ReadNullableString(reader, "Icon"),
        Color = ReadNullableString(reader, "Color"),
        Description = ReadNullableString(reader, "Description"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
        Version = ReadInt64(reader, "Version"),
    };

    private static DocumentTagRecord ReadTag(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        Name = ReadString(reader, "Name"),
        Code = ReadNullableString(reader, "Code"),
        Icon = ReadNullableString(reader, "Icon"),
        Color = ReadNullableString(reader, "Color"),
        Description = ReadNullableString(reader, "Description"),
        UseCount = ReadInt32(reader, "UseCount"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
        Version = ReadInt64(reader, "Version"),
        IsHot = ReadBoolean(reader, "IsHot"),
        IsRecommended = ReadBoolean(reader, "IsRecommended"),
    };

    private static DocumentNameConflictRecord ReadNameConflict(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        Name = ReadString(reader, "Name"),
        Version = ReadInt64(reader, "Version"),
    };

    private static DocumentTagAssignmentRow ReadTagAssignment(DbDataReader reader) => new()
    {
        DocumentItemId = ReadGuid(reader, "DocumentItemId"),
        TagId = ReadGuid(reader, "TagId"),
        TagName = ReadString(reader, "TagName"),
    };

    private static DocumentItemRecord ReadItem(DbDataReader reader)
    {
        var detail = ReadItemDetail(reader);
        return new DocumentItemRecord
        {
            Id = detail.Id,
            DocumentNo = detail.DocumentNo,
            Title = detail.Title,
            Description = detail.Description,
            CategoryId = detail.CategoryId,
            CategoryName = detail.CategoryName,
            CategoryColor = detail.CategoryColor,
            DocumentType = detail.DocumentType,
            SizeKb = detail.SizeKb,
            Thumbnail = detail.Thumbnail,
            Status = detail.Status,
            AccessCount = detail.AccessCount,
            Sort = detail.Sort,
            LastAccessTime = detail.LastAccessTime,
            CurrentVersionId = detail.CurrentVersionId,
            CreatedAtUtc = detail.CreatedAtUtc,
            CreatedByUserId = detail.CreatedByUserId,
            UpdatedAtUtc = detail.UpdatedAtUtc,
            UpdatedByUserId = detail.UpdatedByUserId,
            Version = detail.Version,
            DeletedAtUtc = detail.DeletedAtUtc,
            DeletedByUserId = detail.DeletedByUserId,
        };
    }

    private static DocumentItemDetailRecord ReadItemDetail(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        DocumentNo = ReadOptionalString(reader, "DocumentNo"),
        Title = ReadString(reader, "Title"),
        Description = ReadNullableString(reader, "Description"),
        CategoryId = ReadNullableGuid(reader, "CategoryId"),
        CategoryName = ReadOptionalNullableString(reader, "CategoryName"),
        CategoryColor = ReadOptionalNullableString(reader, "CategoryColor"),
        DocumentType = ReadOptionalInt32(reader, "DocumentType"),
        SizeKb = ReadOptionalInt64(reader, "SizeKb"),
        Thumbnail = ReadOptionalNullableString(reader, "Thumbnail"),
        Status = ReadOptionalInt32(reader, "Status"),
        AccessCount = ReadOptionalInt32(reader, "AccessCount"),
        Sort = ReadOptionalInt32(reader, "Sort"),
        LastAccessTime = ReadOptionalNullableDateTimeOffset(reader, "LastAccessTime"),
        CurrentVersionId = ReadNullableGuid(reader, "CurrentVersionId"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        CreatedByUserId = ReadGuid(reader, "CreatedByUserId"),
        UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
        UpdatedByUserId = ReadNullableGuid(reader, "UpdatedByUserId"),
        Version = ReadInt64(reader, "Version"),
        IsPurging = ReadBoolean(reader, "IsPurging"),
        VisibilityMode = ReadString(reader, "VisibilityMode"),
        AccessRevision = ReadInt64(reader, "AccessRevision"),
        LastVersionNumber = ReadInt32(reader, "LastVersionNumber"),
        VersionId = ReadNullableGuid(reader, "VersionId"),
        VersionNumber = ReadNullableInt32(reader, "VersionNumber"),
        FileId = ReadNullableGuid(reader, "FileId"),
        ContentHash = ReadNullableString(reader, "ContentHash"),
        SizeBytes = ReadNullableInt64(reader, "SizeBytes"),
        FileName = ReadNullableString(reader, "FileName"),
        MimeType = ReadNullableString(reader, "MimeType"),
        Extension = ReadNullableString(reader, "Extension"),
        FileSizeBytes = ReadNullableInt64(reader, "FileSizeBytes"),
        ChangeDescription = ReadOptionalNullableString(reader, "ChangeDescription"),
        VersionCreatedAtUtc = ReadNullableDateTimeOffset(reader, "VersionCreatedAtUtc"),
        UploadedByUserId = ReadNullableGuid(reader, "UploadedByUserId"),
        DeletedAtUtc = ReadNullableDateTimeOffset(reader, "DeletedAtUtc"),
        DeletedByUserId = ReadNullableGuid(reader, "DeletedByUserId"),
    };

    private static DocumentVersionRecord ReadVersion(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        DocumentItemId = ReadGuid(reader, "DocumentItemId"),
        FileId = ReadGuid(reader, "FileId"),
        VersionNumber = ReadInt32(reader, "VersionNumber"),
        ContentHash = ReadNullableString(reader, "ContentHash"),
        SizeBytes = ReadInt64(reader, "SizeBytes"),
        ChangeDescription = ReadNullableString(reader, "ChangeDescription"),
        UploadedByUserId = ReadGuid(reader, "UploadedByUserId"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
    };

    private static DocumentVersionRetentionCandidateRecord ReadRetentionCandidate(DbDataReader reader) => new()
    {
        DocumentItemId = ReadGuid(reader, "DocumentItemId"),
        CurrentVersionId = ReadGuid(reader, "CurrentVersionId"),
        HistoryCount = ReadInt32(reader, "HistoryCount"),
    };

    private static DocumentAccessLogRecord ReadAccessLog(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        DocumentItemId = ReadGuid(reader, "DocumentItemId"),
        DocumentTitle = ReadString(reader, "DocumentTitle"),
        AccessTypeKey = ReadString(reader, "AccessTypeKey"),
        SourceKey = ReadString(reader, "SourceKey"),
        ActorUserId = ReadNullableGuid(reader, "ActorUserId"),
        OccurredAtUtc = ReadDateTimeOffset(reader, "OccurredAtUtc"),
        ClientIpFingerprint = ReadNullableString(reader, "ClientIpFingerprint"),
    };

    private static DocumentPreviewTaskRecord ReadPreviewTask(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        DocumentItemId = ReadGuid(reader, "DocumentItemId"),
        VersionId = ReadNullableGuid(reader, "VersionId"),
        DocumentTitle = ReadString(reader, "DocumentTitle"),
        SourceReferenceId = ReadNullableGuid(reader, "SourceReferenceId"), OutputReferenceId = ReadNullableGuid(reader, "OutputReferenceId"),
        OutputOwnerId = ReadNullableGuid(reader, "OutputOwnerId"), AttemptCount = ReadInt32(reader, "AttemptCount"), NextAttemptAtUtc = ReadNullableDateTimeOffset(reader, "NextAttemptAtUtc"), LeaseExpiresAtUtc = ReadNullableDateTimeOffset(reader, "LeaseExpiresAtUtc"),
        SourceFileId = ReadGuid(reader, "SourceFileId"),
        SourceFileName = ReadNullableString(reader, "SourceFileName"),
        SourceMimeType = ReadNullableString(reader, "SourceMimeType"),
        OutputFileId = ReadNullableGuid(reader, "OutputFileId"),
        StatusKey = ReadString(reader, "StatusKey"),
        ProviderKey = ReadString(reader, "ProviderKey"),
        ErrorCode = ReadNullableString(reader, "ErrorCode"),
        RequestedByUserId = ReadNullableGuid(reader, "RequestedByUserId"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        StartedAtUtc = ReadNullableDateTimeOffset(reader, "StartedAtUtc"),
        CompletedAtUtc = ReadNullableDateTimeOffset(reader, "CompletedAtUtc"),
        Version = ReadInt64(reader, "Version"),
    };

    private static DocumentPermissionRecord ReadPermission(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        DocumentId = ReadGuid(reader, "DocumentId"),
        UserId = ReadGuid(reader, "UserId"),
        Actions = ReadInt32(reader, "Actions"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
    };

    private static DocumentShareRecord ReadShare(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        DocumentId = ReadGuid(reader, "DocumentId"),
        ShareCode = ReadString(reader, "ShareCode"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        ExpireTime = ReadDateTimeOffset(reader, "ExpireTime"),
        PasswordHash = ReadNullableString(reader, "PasswordHash"),
        MaxAccessCount = ReadNullableInt32(reader, "MaxAccessCount"),
        AccessCount = ReadInt32(reader, "AccessCount"),
        PolicyRevision = ReadInt64(reader, "PolicyRevision"),
        IsEnabled = ReadBoolean(reader, "IsEnabled"),
        Version = ReadInt64(reader, "Version"),
    };

    private static DocumentVersionRetentionSettingRecord ReadVersionRetentionSetting(DbDataReader reader) =>
        new()
        {
            Id = ReadGuid(reader, "Id"),
            MinimumRetainedVersionsPerItem = ReadInt32(reader, "MinimumRetainedVersionsPerItem"),
            MaximumRetainedHistoryVersions = ReadInt32(reader, "MaximumRetainedHistoryVersions"),
            PollSeconds = ReadInt32(reader, "PollSeconds"),
            BatchSize = ReadInt32(reader, "BatchSize"),
            Version = ReadInt64(reader, "Version"),
            UpdatedAtUtc = ReadDateTimeOffset(reader, "UpdatedAtUtc"),
        };

    private static DocumentStatisticsSummaryRecord ReadStatisticsSummary(DbDataReader reader) => new()
    {
        TotalItems = ReadInt64(reader, "TotalItems"),
        TotalVersions = ReadInt64(reader, "TotalVersions"),
        HistorySizeBytes = ReadInt64(reader, "HistorySizeBytes"),
        TotalSizeBytes = ReadInt64(reader, "TotalSizeBytes"),
    };

    private static DocumentStatisticsByTypeRecord ReadStatisticsByType(DbDataReader reader) => new()
    {
        Extension = ReadNullableString(reader, "Extension"),
        Count = ReadInt64(reader, "Count"),
        TotalSizeBytes = ReadInt64(reader, "TotalSizeBytes"),
    };

    private static DocumentStatisticsByCategoryRecord ReadStatisticsByCategory(DbDataReader reader) => new()
    {
        CategoryId = ReadNullableGuid(reader, "CategoryId"),
        CategoryName = ReadNullableString(reader, "CategoryName"),
        Count = ReadInt64(reader, "Count"),
    };

    private static DocumentStatisticsShareCountRecord ReadStatisticsShareCount(DbDataReader reader) => new()
    {
        ShareCount = ReadInt64(reader, "ShareCount"),
        TodayAccessCount = ReadInt64(reader, "TodayAccessCount"),
        TodayDownloadCount = ReadInt64(reader, "TodayDownloadCount"),
        TodayPreviewCount = ReadInt64(reader, "TodayPreviewCount"),
        TodayShareAccessCount = ReadInt64(reader, "TodayShareAccessCount"),
        TodayCreatedCount = ReadInt64(reader, "TodayCreatedCount"),
        RecycleBinCount = ReadInt64(reader, "RecycleBinCount"),
    };

    private static int RequiredOrdinal(DbDataReader reader, string name) => reader.GetOrdinal(name);

    private static int OptionalOrdinal(DbDataReader reader, string name)
    {
        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
        {
            if (string.Equals(reader.GetName(ordinal), name, StringComparison.OrdinalIgnoreCase))
            {
                return ordinal;
            }
        }

        return -1;
    }

    private static Guid ReadGuid(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return reader.GetGuid(ordinal);
    }

    private static Guid? ReadNullableGuid(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadNullableGuid(reader, ordinal);
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

    private static int? ReadNullableInt32(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static long ReadInt64(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static long? ReadNullableInt64(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return reader.IsDBNull(ordinal)
            ? null
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static bool ReadBoolean(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadBoolean(reader, ordinal);
    }

    private static DateTimeOffset ReadDateTimeOffset(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadDateTimeOffset(reader, ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(
        DbDataReader reader,
        string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, ordinal);
    }

    private static string ReadOptionalString(DbDataReader reader, string name)
    {
        var ordinal = OptionalOrdinal(reader, name);
        return ordinal < 0 ? string.Empty : reader.GetString(ordinal);
    }

    private static string? ReadOptionalNullableString(DbDataReader reader, string name)
    {
        var ordinal = OptionalOrdinal(reader, name);
        return ordinal < 0
            ? null
            : AotDataReaderExtensions.ReadNullableString(reader, ordinal);
    }

    private static int ReadOptionalInt32(DbDataReader reader, string name)
    {
        var ordinal = OptionalOrdinal(reader, name);
        return ordinal < 0
            ? 0
            : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static long ReadOptionalInt64(DbDataReader reader, string name)
    {
        var ordinal = OptionalOrdinal(reader, name);
        return ordinal < 0
            ? 0L
            : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ReadOptionalNullableDateTimeOffset(
        DbDataReader reader,
        string name)
    {
        var ordinal = OptionalOrdinal(reader, name);
        return ordinal < 0
            ? null
            : AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, ordinal);
    }
}
#endif

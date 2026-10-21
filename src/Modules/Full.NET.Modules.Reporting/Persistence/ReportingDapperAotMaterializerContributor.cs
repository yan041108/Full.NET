#if FULLNET_AOT_COMPILE
using System.Data.Common;
using System.Globalization;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Reporting.Persistence;

namespace Full.NET.Modules.Reporting.Persistence;

/// <summary>Reporting Native AOT 行物化器。</summary>
internal sealed class ReportingDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<ReportingDataSourceRecord>(ReadDataSource);
        registrar.Register<ReportingExportTaskRecord>(ReadExportTask);
        registrar.Register<ReportingGroupRecord>(ReadGroup);
        registrar.Register<ReportingDefinitionRecord>(ReadDefinition);
        registrar.Register<ReportingDefinitionVersionRecord>(ReadDefinitionVersion);
    }

    // 以下序号与分组、定义和版本的完整/摘要投影一致；摘要 SQL 不读取大正文列。
    private static ReportingGroupRecord ReadGroup(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        Name = reader.GetString(2),
        SortOrder = AotDataReaderExtensions.ReadInt32(reader, 3),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 4),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
        Version = AotDataReaderExtensions.ReadInt32(reader, 7),
    };

    private static ReportingDefinitionRecord ReadDefinition(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        GroupId = reader.GetGuid(1),
        DataSourceId = reader.GetGuid(2),
        DefinitionKey = reader.GetString(3),
        Name = reader.GetString(4),
        Description = AotDataReaderExtensions.ReadNullableString(reader, 5),
        QueryPortKey = reader.GetString(6),
        ParameterSchemaJson = reader.GetString(7),
        LayoutConfigJson = reader.GetString(8),
        LatestPublishedVersionNumber = AotDataReaderExtensions.ReadInt32(reader, 9),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 10),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
        Version = AotDataReaderExtensions.ReadInt32(reader, 13),
    };

    private static ReportingDefinitionVersionRecord ReadDefinitionVersion(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        DefinitionId = reader.GetGuid(1),
        VersionNumber = AotDataReaderExtensions.ReadInt32(reader, 2),
        DataSourceId = reader.GetGuid(3),
        QueryPortKey = reader.GetString(4),
        ParameterSchemaJson = reader.GetString(5),
        LayoutConfigJson = reader.GetString(6),
        ChangeNote = AotDataReaderExtensions.ReadNullableString(reader, 7),
        PublishedByUserId = reader.GetGuid(8),
        PublishedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
    };

    private static ReportingDataSourceRecord ReadDataSource(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        TenantId = ReadNullableGuid(reader, "TenantId"),
        Name = ReadString(reader, "Name"),
        ProviderKey = ReadString(reader, "ProviderKey"),
        ServerHost = ReadString(reader, "ServerHost"),
        Port = ReadInt32(reader, "Port"),
        DatabaseName = ReadString(reader, "DatabaseName"),
        Username = ReadString(reader, "Username"),
        PasswordProtected = ReadString(reader, "PasswordProtected"),
        TrustServerCertificate = ReadBoolean(reader, "TrustServerCertificate"),
        IsEnabled = ReadBoolean(reader, "IsEnabled"),
        LastTestedAtUtc = ReadNullableDateTimeOffset(reader, "LastTestedAtUtc"),
        LastTestStatusKey = ReadNullableString(reader, "LastTestStatusKey"),
        LastTestMessage = ReadNullableString(reader, "LastTestMessage"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
        Version = ReadInt32(reader, "Version"),
    };

    /// <summary>读取导出任务行，列顺序与 SelectColumns 一致。</summary>
    /// <param name="reader">数据读取器。</param>
    private static ReportingExportTaskRecord ReadExportTask(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"),
        TenantId = ReadGuid(reader, "TenantId"),
        DefinitionId = ReadGuid(reader, "DefinitionId"),
        VersionNumber = ReadInt32(reader, "VersionNumber"),
        DefinitionKey = ReadString(reader, "DefinitionKey"),
        DefinitionName = ReadString(reader, "DefinitionName"),
        FormatKey = ReadString(reader, "FormatKey"),
        ParametersJson = ReadString(reader, "ParametersJson"),
        StatusKey = ReadString(reader, "StatusKey"),
        OutputFileId = ReadNullableGuid(reader, "OutputFileId"),
        OutputFileName = ReadNullableString(reader, "OutputFileName"),
        RowCount = ReadInt32(reader, "RowCount"),
        ErrorCode = ReadNullableString(reader, "ErrorCode"),
        ErrorMessage = ReadNullableString(reader, "ErrorMessage"),
        RequestedByUserId = ReadGuid(reader, "RequestedByUserId"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"),
        CompletedAtUtc = ReadNullableDateTimeOffset(reader, "CompletedAtUtc"),
        LeaseId = ReadNullableGuid(reader, "LeaseId"),
        LeaseExpiresAtUtc = ReadNullableDateTimeOffset(reader, "LeaseExpiresAtUtc"),
        ActorPermissionCodesJson = ReadNullableString(reader, "ActorPermissionCodesJson"),
        Version = Convert.ToInt64(reader.GetValue(RequiredOrdinal(reader, "Version")), CultureInfo.InvariantCulture),
    };

    private static int RequiredOrdinal(DbDataReader reader, string name) => reader.GetOrdinal(name);

    private static Guid ReadGuid(DbDataReader reader, string name) =>
        reader.GetGuid(RequiredOrdinal(reader, name));

    private static Guid? ReadNullableGuid(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadNullableGuid(reader, ordinal);
    }

    private static string ReadString(DbDataReader reader, string name) =>
        reader.GetString(RequiredOrdinal(reader, name));

    private static string? ReadNullableString(DbDataReader reader, string name)
    {
        var ordinal = RequiredOrdinal(reader, name);
        return AotDataReaderExtensions.ReadNullableString(reader, ordinal);
    }

    private static int ReadInt32(DbDataReader reader, string name) =>
        Convert.ToInt32(reader.GetValue(RequiredOrdinal(reader, name)), CultureInfo.InvariantCulture);

    private static bool ReadBoolean(DbDataReader reader, string name) =>
        Convert.ToBoolean(reader.GetValue(RequiredOrdinal(reader, name)), CultureInfo.InvariantCulture);

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

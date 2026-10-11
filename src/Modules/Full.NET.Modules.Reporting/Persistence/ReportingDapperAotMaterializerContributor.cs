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
        registrar.Register<ReportingPublishedDefinitionRecord>(ReadPublishedDefinition);
        registrar.Register<ReportingDefinitionRecord>(ReadDefinition);
        registrar.Register<ReportingDefinitionVersionRecord>(ReadDefinitionVersion);
    }

    /// <summary>读取本模块定义元信息；租户 SQL 必须先匹配精确版本授权。</summary>
    private static ReportingDefinitionRecord ReadDefinition(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"), GroupId = ReadGuid(reader, "GroupId"), DataSourceId = ReadGuid(reader, "DataSourceId"),
        DefinitionKey = ReadString(reader, "DefinitionKey"), Name = ReadString(reader, "Name"),
        Description = ReadNullableString(reader, "Description"), QueryPortKey = ReadString(reader, "QueryPortKey"),
        ParameterSchemaJson = ReadString(reader, "ParameterSchemaJson"), LayoutConfigJson = ReadString(reader, "LayoutConfigJson"),
        LatestPublishedVersionNumber = ReadInt32(reader, "LatestPublishedVersionNumber"), IsEnabled = ReadBoolean(reader, "IsEnabled"),
        CreatedAtUtc = ReadDateTimeOffset(reader, "CreatedAtUtc"), UpdatedAtUtc = ReadNullableDateTimeOffset(reader, "UpdatedAtUtc"),
        Version = ReadInt32(reader, "Version"),
    };

    /// <summary>不可变发布版本使用静态物化，授权不会退化到当前草稿。</summary>
    private static ReportingDefinitionVersionRecord ReadDefinitionVersion(DbDataReader reader) => new()
    {
        Id = ReadGuid(reader, "Id"), DefinitionId = ReadGuid(reader, "DefinitionId"), VersionNumber = ReadInt32(reader, "VersionNumber"),
        DataSourceId = ReadGuid(reader, "DataSourceId"), QueryPortKey = ReadString(reader, "QueryPortKey"),
        ParameterSchemaJson = ReadString(reader, "ParameterSchemaJson"), LayoutConfigJson = ReadString(reader, "LayoutConfigJson"),
        ChangeNote = ReadNullableString(reader, "ChangeNote"), PublishedByUserId = ReadGuid(reader, "PublishedByUserId"),
        PublishedAtUtc = ReadDateTimeOffset(reader, "PublishedAtUtc"),
    };

    /// <summary>目录只物化获授版本的执行字段，保持 Native AOT 静态闭包。</summary>
    private static ReportingPublishedDefinitionRecord ReadPublishedDefinition(DbDataReader reader) => new()
    {
        DefinitionId = ReadGuid(reader, "DefinitionId"), DefinitionKey = ReadString(reader, "DefinitionKey"),
        Name = ReadString(reader, "Name"), VersionNumber = ReadInt32(reader, "VersionNumber"),
        QueryPortKey = ReadString(reader, "QueryPortKey"), ParameterSchemaJson = ReadString(reader, "ParameterSchemaJson"),
        LayoutConfigJson = ReadString(reader, "LayoutConfigJson"),
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

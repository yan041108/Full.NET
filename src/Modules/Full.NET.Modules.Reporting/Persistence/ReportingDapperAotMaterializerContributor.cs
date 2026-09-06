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
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar) =>
        registrar.Register<ReportingDataSourceRecord>(ReadDataSource);

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

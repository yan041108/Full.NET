#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Settings.Features.ManageHostConfigEntries;
using HostDictItems = Full.NET.Modules.Settings.Features.ManageHostDictItems;
using HostDictTypes = Full.NET.Modules.Settings.Features.ManageHostDictTypes;
using TenantDictItems = Full.NET.Modules.Settings.Features.ManageTenantDictItems;
using TenantDictTypes = Full.NET.Modules.Settings.Features.ManageTenantDictTypes;

namespace Full.NET.Modules.Settings.Persistence;

/// <summary>
/// Settings Native AOT 行物化器。Host/Tenant 字典记录类型分属不同命名空间，必须分别注册。
/// </summary>
internal sealed class SettingsDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<HostDictTypes.DictTypeRecord>(ReadHostDictTypeRecord);
        registrar.Register<TenantDictTypes.DictTypeRecord>(ReadTenantDictTypeRecord);
        registrar.Register<HostDictTypes.DictTypeIdentityRecord>(ReadHostDictTypeIdentityRecord);
        registrar.Register<TenantDictTypes.DictTypeIdentityRecord>(ReadTenantDictTypeIdentityRecord);
        registrar.Register<HostDictItems.DictItemRecord>(ReadHostDictItemRecord);
        registrar.Register<TenantDictItems.DictItemRecord>(ReadTenantDictItemRecord);
        registrar.Register<HostDictItems.DictItemIdentityRecord>(ReadHostDictItemIdentityRecord);
        registrar.Register<TenantDictItems.DictItemIdentityRecord>(ReadTenantDictItemIdentityRecord);
        registrar.Register<ConfigEntryRecord>(ReadConfigEntryRecord);
        registrar.Register<ConfigEntryIdentityRecord>(ReadConfigEntryIdentityRecord);
        registrar.Register<ConfigEntrySecretRecord>(ReadConfigEntrySecretRecord);
        registrar.Register<GridPreferenceRecord>(ReadGridPreferenceRecord);
    }

    private static HostDictTypes.DictTypeRecord ReadHostDictTypeRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            reader.GetInt32(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            reader.GetInt32(8));

    private static TenantDictTypes.DictTypeRecord ReadTenantDictTypeRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            reader.GetInt32(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            reader.GetInt32(8));

    private static HostDictTypes.DictTypeIdentityRecord ReadHostDictTypeIdentityRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            reader.GetInt32(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            reader.GetInt32(6));

    private static TenantDictTypes.DictTypeIdentityRecord ReadTenantDictTypeIdentityRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            reader.GetInt32(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            reader.GetInt32(6));

    private static HostDictItems.DictItemRecord ReadHostDictItemRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetInt32(5),
            AotDataReaderExtensions.ReadBoolean(reader, 6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            reader.GetInt32(9));

    private static TenantDictItems.DictItemRecord ReadTenantDictItemRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetInt32(5),
            AotDataReaderExtensions.ReadBoolean(reader, 6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            reader.GetInt32(9));

    private static HostDictItems.DictItemIdentityRecord ReadHostDictItemIdentityRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetInt32(5),
            AotDataReaderExtensions.ReadBoolean(reader, 6),
            reader.GetInt32(7));

    private static TenantDictItems.DictItemIdentityRecord ReadTenantDictItemIdentityRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetInt32(5),
            AotDataReaderExtensions.ReadBoolean(reader, 6),
            reader.GetInt32(7));

    private static ConfigEntryRecord ReadConfigEntryRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            AotDataReaderExtensions.ReadBoolean(reader, 8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            reader.GetInt32(11));

    private static ConfigEntryIdentityRecord ReadConfigEntryIdentityRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadNullableString(reader, 3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            AotDataReaderExtensions.ReadBoolean(reader, 8),
            reader.GetInt32(9));

    private static ConfigEntrySecretRecord ReadConfigEntrySecretRecord(DbDataReader reader) =>
        new()
        {
            ValueKind = reader.GetString(0),
            Value = reader.GetString(1),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 2),
        };

    private static GridPreferenceRecord ReadGridPreferenceRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            GridKey = reader.GetString(2),
            SchemaVersion = reader.GetInt32(3),
            ColumnsJson = reader.GetString(4),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 6),
            Version = reader.GetInt32(7),
        };
}
#endif

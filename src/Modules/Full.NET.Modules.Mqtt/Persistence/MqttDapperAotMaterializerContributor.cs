#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Mqtt.Persistence;

/// <summary>MQTT Native AOT 行物化器；列序号必须与 MqttSql 投影一致。</summary>
internal sealed class MqttDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <inheritdoc />
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<MqttClientRecord>(ReadClientRecord);
        registrar.Register<MqttMessageRecord>(ReadMessageRecord);
    }

    private static MqttClientRecord ReadClientRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ClientKey = reader.GetString(1),
        DisplayName = reader.GetString(2),
        Description = AotDataReaderExtensions.ReadNullableString(reader, 3),
        TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 4),
        IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 5),
        SortOrder = reader.GetInt32(6),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
    };

    private static MqttMessageRecord ReadMessageRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        ClientId = AotDataReaderExtensions.ReadNullableGuid(reader, 2),
        ClientKey = AotDataReaderExtensions.ReadNullableString(reader, 3),
        Topic = reader.GetString(4),
        PayloadSizeBytes = reader.GetInt32(5),
        Qos = reader.GetInt32(6),
        Status = reader.GetString(7),
        IdempotencyKey = AotDataReaderExtensions.ReadNullableString(reader, 8),
        SummaryMessage = AotDataReaderExtensions.ReadNullableString(reader, 9),
        PublishedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
        CreatedByUserId = reader.GetGuid(12),
    };
}
#endif

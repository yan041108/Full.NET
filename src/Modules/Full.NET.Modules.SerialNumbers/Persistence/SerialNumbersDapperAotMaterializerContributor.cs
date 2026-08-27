#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.SerialNumbers.Persistence;

/// <summary>
/// SerialNumbers Native AOT 行物化器。读取序号必须与 SerialNumberSql 中对应投影保持一致。
/// </summary>
internal sealed class SerialNumbersDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<SerialNumberRuleRecord>(ReadSerialNumberRuleRecord);
        registrar.Register<AllocatedCounterValue>(ReadAllocatedCounterValue);
        registrar.Register<SerialNumberAllocationRecord>(ReadSerialNumberAllocationRecord);
    }

    private static SerialNumberRuleRecord ReadSerialNumberRuleRecord(
        DbDataReader reader) => new()
        {
            Id = reader.GetGuid(0),
            RuleKey = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Description = reader.IsDBNull(3) ? null : reader.GetString(3),
            Scope = AotDataReaderExtensions.ReadInt32(reader, 4),
            ResetInterval = AotDataReaderExtensions.ReadInt32(reader, 5),
            Pattern = reader.GetString(6),
            MinimumValue = reader.GetInt64(7),
            MaximumValue = reader.GetInt64(8),
            DisplayOrder = reader.GetInt32(9),
            IsEnabled = AotDataReaderExtensions.ReadBoolean(reader, 10),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            CreatedByUserId = reader.GetGuid(12),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            UpdatedByUserId = AotDataReaderExtensions.ReadNullableGuid(reader, 14),
            Version = reader.GetInt64(15),
        };

    private static AllocatedCounterValue ReadAllocatedCounterValue(
        DbDataReader reader) => new()
        {
            Value = reader.GetInt64(0),
        };

    private static SerialNumberAllocationRecord ReadSerialNumberAllocationRecord(
        DbDataReader reader) => new()
        {
            RuleKey = reader.GetString(0),
            SerialNumber = reader.GetString(1),
            SequenceValue = reader.GetInt64(2),
            ResetBucket = reader.GetString(3),
            AllocatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 4),
        };
}
#endif

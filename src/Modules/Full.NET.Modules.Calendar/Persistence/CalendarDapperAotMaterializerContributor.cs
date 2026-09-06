#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Calendar.Persistence;

/// <summary>
/// Calendar Native AOT 行物化器。读取序号必须与 PersonalScheduleSql 中对应投影保持一致。
/// </summary>
internal sealed class CalendarDapperAotMaterializerContributor
    : IDapperAotMaterializerContributor
{
    /// <summary>注册 Calendar 模块所需的 Dapper AOT 行物化器。</summary>
    /// <param name="registrar">Native AOT 物化器注册器。</param>
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<PersonalScheduleRecord>(ReadPersonalScheduleRecord);
    }

    private static PersonalScheduleRecord ReadPersonalScheduleRecord(
        DbDataReader reader) => new()
        {
            Id = reader.GetGuid(0),
            TenantId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            OwnerUserId = reader.GetGuid(2),
            Content = reader.GetString(3),
            StartAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 4),
            EndAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            Status = reader.GetString(6),
            CompletedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 9),
            Version = reader.GetInt32(10),
        };
}
#endif

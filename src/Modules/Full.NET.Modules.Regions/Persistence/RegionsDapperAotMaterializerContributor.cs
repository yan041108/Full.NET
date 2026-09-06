#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Regions.Persistence;

/// <summary>
/// Regions Native AOT 行物化器。读取序号必须与 AdministrativeRegionSql 中对应投影保持一致。
/// </summary>
internal sealed class RegionsDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    /// <summary>注册 Regions 模块所需的 Dapper AOT 行物化器。</summary>
    /// <param name="registrar">Native AOT 物化器注册器。</param>
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<AdministrativeRegionRecord>(ReadAdministrativeRegionRecord);
        registrar.Register<AdministrativeRegionParentLinkRecord>(ReadParentLinkRecord);
        registrar.Register<AdministrativeRegionCodeLinkRecord>(ReadCodeLinkRecord);
        registrar.Register<AdministrativeRegionChildQueryRecord>(ReadChildQueryRecord);
        registrar.Register<AdministrativeRegionTreeRecord>(ReadTreeRecord);
        registrar.Register<DatasetManifestRecord>(ReadDatasetManifestRecord);
    }

    private static AdministrativeRegionRecord ReadAdministrativeRegionRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        Code = reader.GetString(2),
        Name = reader.GetString(3),
        ShortName = AotDataReaderExtensions.ReadNullableString(reader, 4),
        MergerName = AotDataReaderExtensions.ReadNullableString(reader, 5),
        ZipCode = AotDataReaderExtensions.ReadNullableString(reader, 6),
        CityCode = AotDataReaderExtensions.ReadNullableString(reader, 7),
        Level = reader.GetInt32(8),
        RegionType = AotDataReaderExtensions.ReadNullableString(reader, 9),
        PinYin = AotDataReaderExtensions.ReadNullableString(reader, 10),
        Longitude = AotDataReaderExtensions.ReadNullableDecimal(reader, 11),
        Latitude = AotDataReaderExtensions.ReadNullableDecimal(reader, 12),
        DisplayOrder = reader.GetInt32(13),
        Remark = AotDataReaderExtensions.ReadNullableString(reader, 14),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 15),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 16),
        Version = reader.GetInt32(17),
    };

    private static AdministrativeRegionParentLinkRecord ReadParentLinkRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
    };

    private static AdministrativeRegionCodeLinkRecord ReadCodeLinkRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Code = reader.GetString(1),
        ParentCode = AotDataReaderExtensions.ReadNullableString(reader, 2),
    };

    private static AdministrativeRegionChildQueryRecord ReadChildQueryRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        Code = reader.GetString(2),
        Name = reader.GetString(3),
        Level = reader.GetInt32(4),
        DisplayOrder = reader.GetInt32(5),
        ChildCount = reader.GetInt32(6),
    };

    private static AdministrativeRegionTreeRecord ReadTreeRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        Code = reader.GetString(2),
        Name = reader.GetString(3),
        Level = reader.GetInt32(4),
        DisplayOrder = reader.GetInt32(5),
    };

    private static DatasetManifestRecord ReadDatasetManifestRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        DatasetKey = reader.GetString(1),
        DatasetVersion = reader.GetString(2),
        SourceDigest = reader.GetString(3),
        RecordCount = reader.GetInt32(4),
        AppliedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
        AppliedByUserId = reader.GetGuid(6),
    };
}
#endif

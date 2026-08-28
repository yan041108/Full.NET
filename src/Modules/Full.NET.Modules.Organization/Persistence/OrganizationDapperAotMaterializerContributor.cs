#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using global::Dapper;

namespace Full.NET.Modules.Organization.Persistence;

/// <summary>
/// Organization 模块 Native AOT 行物化与 typed insert 参数绑定。
/// </summary>
/// <remarks>
/// 列序必须与对应 SQL 投影一致；insert record 不含 TenantId，由范围守卫注入当前租户。
/// </remarks>
internal sealed class OrganizationDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<OrganizationUnitRecord>(ReadOrganizationUnitRecord);
        registrar.Register<OrganizationUnitListRow>(ReadOrganizationUnitListRow);
        registrar.Register<OrganizationUnitParentLink>(ReadOrganizationUnitParentLink);
        registrar.Register<OrganizationUnitSnapshotRow>(ReadOrganizationUnitSnapshotRow);
        registrar.Register<OrganizationUserUnitRecord>(ReadOrganizationUserUnitRecord);
        registrar.Register<OrganizationUserUnitListRow>(ReadOrganizationUserUnitListRow);
        registrar.Register<OrganizationUserPositionRecord>(ReadOrganizationUserPositionRecord);
        registrar.Register<OrganizationUserPositionListRow>(ReadOrganizationUserPositionListRow);
        registrar.Register<OrganizationPositionRecord>(ReadOrganizationPositionRecord);
        registrar.Register<OrganizationPositionListRow>(ReadOrganizationPositionListRow);
        registrar.Register<OrganizationPositionLevelRecord>(ReadOrganizationPositionLevelRecord);

        DapperAotParameterRegistry.Register<InsertOrganizationUnit>(BindInsertOrganizationUnit);
        DapperAotParameterRegistry.Register<InsertOrganizationPosition>(
            BindInsertOrganizationPosition);
        DapperAotParameterRegistry.Register<InsertOrganizationPositionLevel>(
            BindInsertOrganizationPositionLevel);
        DapperAotParameterRegistry.Register<InsertOrganizationUserUnit>(
            BindInsertOrganizationUserUnit);
        DapperAotParameterRegistry.Register<InsertOrganizationUserPosition>(
            BindInsertOrganizationUserPosition);
    }

    private static OrganizationUnitRecord ReadOrganizationUnitRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            AotDataReaderExtensions.ReadNullableGuid(reader, 2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetInt32(5),
            AotDataReaderExtensions.ReadBoolean(reader, 6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            reader.GetInt32(9));

    private static OrganizationUnitListRow ReadOrganizationUnitListRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
            Code = reader.GetString(2),
            Name = reader.GetString(3),
            DisplayOrder = reader.GetInt32(4),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 5),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            Version = reader.GetInt32(8),
        };

    private static OrganizationUnitParentLink ReadOrganizationUnitParentLink(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ParentId = AotDataReaderExtensions.ReadNullableGuid(reader, 1),
        };

    private static OrganizationUnitSnapshotRow ReadOrganizationUnitSnapshotRow(DbDataReader reader) =>
        new()
        {
            UnitId = reader.GetGuid(0),
            Name = reader.GetString(1),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 2),
            Version = reader.GetInt32(3),
            ChangedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 4),
        };

    private static OrganizationUserUnitRecord ReadOrganizationUserUnitRecord(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetGuid(1),
            UserId = reader.GetGuid(2),
            UnitId = reader.GetGuid(3),
            IsPrimary = AotDataReaderExtensions.ReadBoolean(reader, 4),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 5),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            Version = reader.GetInt32(8),
        };

    private static OrganizationUserUnitListRow ReadOrganizationUserUnitListRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            UnitId = reader.GetGuid(2),
            UnitCode = reader.GetString(3),
            UnitName = reader.GetString(4),
            IsPrimary = AotDataReaderExtensions.ReadBoolean(reader, 5),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 6),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            Version = reader.GetInt32(9),
        };

    private static OrganizationUserPositionRecord ReadOrganizationUserPositionRecord(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetGuid(1),
            UserId = reader.GetGuid(2),
            PositionId = reader.GetGuid(3),
            IsPrimary = AotDataReaderExtensions.ReadBoolean(reader, 4),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 5),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            Version = reader.GetInt32(8),
        };

    private static OrganizationUserPositionListRow ReadOrganizationUserPositionListRow(
        DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            UserId = reader.GetGuid(1),
            PositionId = reader.GetGuid(2),
            PositionCode = reader.GetString(3),
            PositionName = reader.GetString(4),
            IsPrimary = AotDataReaderExtensions.ReadBoolean(reader, 5),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 6),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 8),
            Version = reader.GetInt32(9),
        };

    private static OrganizationPositionRecord ReadOrganizationPositionRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            AotDataReaderExtensions.ReadNullableGuid(reader, 4),
            AotDataReaderExtensions.ReadNullableString(reader, 5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadNullableGuid(reader, 7),
            AotDataReaderExtensions.ReadNullableString(reader, 8),
            AotDataReaderExtensions.ReadNullableString(reader, 9),
            reader.GetInt32(10),
            AotDataReaderExtensions.ReadBoolean(reader, 11),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 12),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 13),
            reader.GetInt32(14));

    private static OrganizationPositionListRow ReadOrganizationPositionListRow(DbDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            Code = reader.GetString(1),
            Name = reader.GetString(2),
            UnitId = AotDataReaderExtensions.ReadNullableGuid(reader, 3),
            UnitCode = AotDataReaderExtensions.ReadNullableString(reader, 4),
            UnitName = AotDataReaderExtensions.ReadNullableString(reader, 5),
            PositionLevelId = AotDataReaderExtensions.ReadNullableGuid(reader, 6),
            PositionLevelCode = AotDataReaderExtensions.ReadNullableString(reader, 7),
            PositionLevelName = AotDataReaderExtensions.ReadNullableString(reader, 8),
            DisplayOrder = reader.GetInt32(9),
            IsActive = AotDataReaderExtensions.ReadBoolean(reader, 10),
            CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 11),
            UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 12),
            Version = reader.GetInt32(13),
        };

    private static OrganizationPositionLevelRecord ReadOrganizationPositionLevelRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            AotDataReaderExtensions.ReadBoolean(reader, 5),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 6),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 7),
            reader.GetInt32(8));

    private static DynamicParameters BindInsertOrganizationUnit(InsertOrganizationUnit row)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", row.Id);
        parameters.Add("ParentId", (object?)row.ParentId ?? DBNull.Value);
        parameters.Add("Code", row.Code);
        parameters.Add("Name", row.Name);
        parameters.Add("DisplayOrder", row.DisplayOrder);
        parameters.Add("IsActive", row.IsActive);
        parameters.Add("CreatedAtUtc", row.CreatedAtUtc);
        parameters.Add("Version", row.Version);
        return parameters;
    }

    private static DynamicParameters BindInsertOrganizationPosition(InsertOrganizationPosition row)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", row.Id);
        parameters.Add("Code", row.Code);
        parameters.Add("Name", row.Name);
        parameters.Add("DisplayOrder", row.DisplayOrder);
        parameters.Add("IsActive", row.IsActive);
        parameters.Add("CreatedAtUtc", row.CreatedAtUtc);
        parameters.Add("Version", row.Version);
        return parameters;
    }

    private static DynamicParameters BindInsertOrganizationPositionLevel(
        InsertOrganizationPositionLevel row)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", row.Id);
        parameters.Add("Code", row.Code);
        parameters.Add("Name", row.Name);
        parameters.Add("DisplayOrder", row.DisplayOrder);
        parameters.Add("IsActive", row.IsActive);
        parameters.Add("CreatedAtUtc", row.CreatedAtUtc);
        parameters.Add("Version", row.Version);
        return parameters;
    }

    private static DynamicParameters BindInsertOrganizationUserUnit(InsertOrganizationUserUnit row)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", row.Id);
        parameters.Add("UserId", row.UserId);
        parameters.Add("UnitId", row.UnitId);
        parameters.Add("IsPrimary", row.IsPrimary);
        parameters.Add("IsActive", row.IsActive);
        parameters.Add("CreatedAtUtc", row.CreatedAtUtc);
        parameters.Add("Version", row.Version);
        return parameters;
    }

    private static DynamicParameters BindInsertOrganizationUserPosition(
        InsertOrganizationUserPosition row)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Id", row.Id);
        parameters.Add("UserId", row.UserId);
        parameters.Add("PositionId", row.PositionId);
        parameters.Add("IsPrimary", row.IsPrimary);
        parameters.Add("IsActive", row.IsActive);
        parameters.Add("CreatedAtUtc", row.CreatedAtUtc);
        parameters.Add("Version", row.Version);
        return parameters;
    }
}
#endif

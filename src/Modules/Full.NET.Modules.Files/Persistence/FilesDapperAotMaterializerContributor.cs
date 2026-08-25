#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;
using Full.NET.Modules.Files.Features.HostFileReferenceClaims;

namespace Full.NET.Modules.Files.Persistence;

/// <summary>
/// Files 模块 Native AOT 行物化器注册，覆盖 API 查询与 Worker 清理、对账读取。
/// </summary>
internal sealed class FilesDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar)
    {
        registrar.Register<HostFileListRecord>(ReadHostFileListRecord);
        registrar.Register<HostFileDetailRecord>(ReadHostFileDetailRecord);
        registrar.Register<DeletedHostFileBlobRecord>(ReadDeletedHostFileBlobRecord);
        registrar.Register<PendingHostFileRecord>(ReadPendingHostFileRecord);
        registrar.Register<HostFileReferenceClaimRecord>(ReadHostFileReferenceClaimRecord);
    }

    private static HostFileListRecord ReadHostFileListRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            AotDataReaderExtensions.ReadNullableString(reader, 4),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 5),
            reader.GetGuid(6));

    private static HostFileDetailRecord ReadHostFileDetailRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 7),
            reader.GetGuid(8));

    private static DeletedHostFileBlobRecord ReadDeletedHostFileBlobRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 3));

    private static PendingHostFileRecord ReadPendingHostFileRecord(DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 3),
            reader.GetString(4));

    private static HostFileReferenceClaimRecord ReadHostFileReferenceClaimRecord(
        DbDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetGuid(4),
            reader.GetString(5),
            AotDataReaderExtensions.ReadNullableString(reader, 6),
            reader.GetInt64(7),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 8),
            AotDataReaderExtensions.ReadDateTimeOffset(reader, 9),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 10),
            AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11));
}
#endif

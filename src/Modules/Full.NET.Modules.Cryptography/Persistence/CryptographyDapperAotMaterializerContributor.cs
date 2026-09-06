#if FULLNET_AOT_COMPILE
using System.Data.Common;
using Full.NET.Data.Dapper;

namespace Full.NET.Modules.Cryptography.Persistence;

/// <summary>国密 Native AOT 行物化器。</summary>
internal sealed class CryptographyDapperAotMaterializerContributor : IDapperAotMaterializerContributor
{
    public void RegisterMaterializers(DapperAotMaterializerRegistrar registrar) =>
        registrar.Register<CryptographyKeyRecord>(ReadKeyRecord);

    private static CryptographyKeyRecord ReadKeyRecord(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        KeyKey = reader.GetString(1),
        DisplayName = reader.GetString(2),
        Description = AotDataReaderExtensions.ReadNullableString(reader, 3),
        Algorithm = reader.GetString(4),
        Purpose = reader.GetString(5),
        PublicKeyHex = reader.GetString(6),
        PublicKeyFingerprint = reader.GetString(7),
        Status = reader.GetString(8),
        SortOrder = reader.GetInt32(9),
        CreatedAtUtc = AotDataReaderExtensions.ReadDateTimeOffset(reader, 10),
        UpdatedAtUtc = AotDataReaderExtensions.ReadNullableDateTimeOffset(reader, 11),
    };
}
#endif

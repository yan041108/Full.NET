using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class DatabaseColumnMetadataMapperTests
{
    [TestMethod]
    public void Map_sql_server_metadata_preserves_order_nullability_and_length()
    {
        var columns = DatabaseColumnMetadataMapper.Map(
            DatabaseMetadataProvider.SqlServer,
            [
                new("Name", "nvarchar", "nvarchar(200)", false, 200, 3),
                new("CreatedAtUtc", "datetimeoffset", "datetimeoffset(7)", false, null, 8),
                new("Id", "uniqueidentifier", "uniqueidentifier", false, null, 1),
                new("Description", "varchar", "varchar(500)", true, 500, 4),
                new(
                    "Price",
                    "decimal",
                    "decimal(18,2)",
                    false,
                    null,
                    5,
                    NumericPrecision: 18,
                    NumericScale: 2),
                new("TenantId", "uniqueidentifier", "uniqueidentifier", false, null, 2),
                new("Version", "bigint", "bigint", false, null, 7),
                new("IsActive", "bit", "bit", false, null, 6),
            ]);

        CollectionAssert.AreEqual(
            new[]
            {
                "Id",
                "TenantId",
                "Name",
                "Description",
                "Price",
                "IsActive",
                "Version",
                "CreatedAtUtc",
            },
            columns.Select(column => column.DatabaseName).ToArray());
        Assert.AreEqual(FullNetScalarType.Uuid, columns[0].ScalarType);
        Assert.AreEqual(FullNetScalarType.String, columns[2].ScalarType);
        Assert.AreEqual(200, columns[2].MaxLength);
        Assert.IsTrue(columns[3].IsNullable);
        Assert.AreEqual(FullNetScalarType.Decimal, columns[4].ScalarType);
        Assert.AreEqual(18, columns[4].NumericPrecision);
        Assert.AreEqual(2, columns[4].NumericScale);
        Assert.AreEqual(FullNetScalarType.Boolean, columns[5].ScalarType);
        Assert.AreEqual(FullNetScalarType.Int64, columns[6].ScalarType);
        Assert.AreEqual(FullNetScalarType.DateTimeUtc, columns[7].ScalarType);
    }

    [TestMethod]
    public void Map_mysql_metadata_matches_the_shared_logical_types()
    {
        var columns = DatabaseColumnMetadataMapper.Map(
            DatabaseMetadataProvider.MySql,
            [
                new("Id", "binary", "binary(16)", false, 16, 1),
                new("TenantId", "binary", "binary(16)", false, 16, 2),
                new("Name", "varchar", "varchar(200)", false, 200, 3),
                new(
                    "Price",
                    "decimal",
                    "decimal(18,2)",
                    false,
                    null,
                    4,
                    NumericPrecision: 18,
                    NumericScale: 2),
                new("IsActive", "tinyint", "tinyint(1)", false, null, 5),
                new("Version", "bigint", "bigint", false, null, 6),
                new("CreatedAtUtc", "datetime", "datetime(6)", false, null, 7),
            ]);

        CollectionAssert.AreEqual(
            new[]
            {
                FullNetScalarType.Uuid,
                FullNetScalarType.Uuid,
                FullNetScalarType.String,
                FullNetScalarType.Decimal,
                FullNetScalarType.Boolean,
                FullNetScalarType.Int64,
                FullNetScalarType.DateTimeUtc,
            },
            columns.Select(column => column.ScalarType).ToArray());
        Assert.AreEqual("tenantId", columns[1].JsonPropertyName);
        Assert.AreEqual(200, columns[2].MaxLength);
        Assert.AreEqual(18, columns[3].NumericPrecision);
        Assert.AreEqual(2, columns[3].NumericScale);
    }

    [TestMethod]
    public void Map_rejects_ambiguous_binary_and_tinyint_mysql_types()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseColumnMetadataMapper.Map(
                DatabaseMetadataProvider.MySql,
                [new("Id", "binary", "binary(32)", false, 32, 1)]));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseColumnMetadataMapper.Map(
                DatabaseMetadataProvider.MySql,
                [new("IsActive", "tinyint", "tinyint", false, null, 1)]));
    }

    [TestMethod]
    public void Map_rejects_unbounded_or_oversized_string_lengths()
    {
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseColumnMetadataMapper.Map(
                DatabaseMetadataProvider.SqlServer,
                [new("Name", "nvarchar", "nvarchar(max)", false, -1, 1)]));
        Assert.ThrowsExactly<NotSupportedException>(() =>
            DatabaseColumnMetadataMapper.Map(
                DatabaseMetadataProvider.MySql,
                [new(
                    "Name",
                    "varchar",
                    "varchar(4294967295)",
                    false,
                    4_294_967_295,
                    1)]));
    }
}

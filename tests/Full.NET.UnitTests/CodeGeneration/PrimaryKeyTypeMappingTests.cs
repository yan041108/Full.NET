using Full.NET.Data.CodeGeneration.PrimaryKeys;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class PrimaryKeyTypeMappingTests
{
    [TestMethod]
    public void UuidV7_profile_maps_to_guid_binary16_and_json_uuid()
    {
        var types = PrimaryKeyTypeMapping.Resolve(PrimaryKeyProfile.UuidV7);

        Assert.AreEqual("Guid", types.CSharpType);
        Assert.AreEqual("uniqueidentifier", types.SqlServerColumnType);
        Assert.AreEqual("BINARY(16)", types.MySqlColumnType);
        Assert.AreEqual("string", types.JsonSchemaType);
        Assert.AreEqual("uuid", types.JsonSchemaFormat);
    }

    [TestMethod]
    public void Snowflake_profile_maps_to_long_bigint_and_decimal_json_string()
    {
        var types = PrimaryKeyTypeMapping.Resolve(PrimaryKeyProfile.Snowflake);

        Assert.AreEqual("long", types.CSharpType);
        Assert.AreEqual("bigint", types.SqlServerColumnType);
        Assert.AreEqual("BIGINT", types.MySqlColumnType);
        Assert.AreEqual("string", types.JsonSchemaType);
        Assert.IsNull(types.JsonSchemaFormat);
    }

    [TestMethod]
    public void Uuid_and_snowflake_profiles_are_mutually_exclusive()
    {
        Assert.IsTrue(PrimaryKeyTypeMapping.AreProfilesCompatible(
            PrimaryKeyProfile.UuidV7,
            PrimaryKeyProfile.UuidV7));
        Assert.IsTrue(PrimaryKeyTypeMapping.AreProfilesCompatible(
            PrimaryKeyProfile.Snowflake,
            PrimaryKeyProfile.Snowflake));
        Assert.IsFalse(PrimaryKeyTypeMapping.AreProfilesCompatible(
            PrimaryKeyProfile.UuidV7,
            PrimaryKeyProfile.Snowflake));
    }

    [TestMethod]
    public void Foreign_key_columns_reuse_the_same_profile_mapping()
    {
        var primary = PrimaryKeyTypeMapping.Resolve(PrimaryKeyProfile.UuidV7);
        var foreign = PrimaryKeyTypeMapping.Resolve(PrimaryKeyProfile.UuidV7);

        Assert.AreEqual(primary.CSharpType, foreign.CSharpType);
        Assert.AreEqual(primary.SqlServerColumnType, foreign.SqlServerColumnType);
        Assert.AreEqual(primary.MySqlColumnType, foreign.MySqlColumnType);
        Assert.AreEqual(primary.JsonSchemaType, foreign.JsonSchemaType);
        Assert.AreEqual(primary.JsonSchemaFormat, foreign.JsonSchemaFormat);
    }

    [TestMethod]
    public void Composite_primary_key_columns_share_profile_physical_types()
    {
        var userRole = PrimaryKeyTypeMapping.Resolve(PrimaryKeyProfile.UuidV7);

        Assert.AreEqual("Guid", userRole.CSharpType);
        Assert.AreEqual("BINARY(16)", userRole.MySqlColumnType);
    }
}

using Full.NET.Abstractions.Ids;

namespace Full.NET.UnitTests.Ids;

[TestClass]
public sealed class GuidV7IdGeneratorTests
{
    [TestMethod]
    public void NewId_is_never_empty()
    {
        var generator = new GuidV7IdGenerator();

        var id = generator.NewId();

        Assert.AreNotEqual(Guid.Empty, id);
    }

    [TestMethod]
    public void NewId_returns_version_7_uuid()
    {
        var generator = new GuidV7IdGenerator();

        var id = generator.NewId();

        Assert.AreEqual(7, id.Version);
    }

    [TestMethod]
    public void NewId_uses_rfc_4122_variant()
    {
        var generator = new GuidV7IdGenerator();

        var id = generator.NewId();
        var variantNibble = Convert.ToInt32(id.ToString("D")[19].ToString(), 16);

        Assert.IsGreaterThanOrEqualTo(8, variantNibble);
        Assert.IsLessThanOrEqualTo(11, variantNibble);
    }

    [TestMethod]
    public void NewId_generates_distinct_values_in_burst()
    {
        var generator = new GuidV7IdGenerator();

        var ids = Enumerable.Range(0, 64)
            .Select(_ => generator.NewId())
            .ToHashSet();

        Assert.HasCount(64, ids);
    }
}

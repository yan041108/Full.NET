using Full.NET.Modules.Identity.Features.ManageOpenAccessClients;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class OpenAccessClientRequestValidationTests
{
    [TestMethod]
    public void ParseOptionalUserId_rejects_invalid_filter()
    {
        var result = OpenAccessClientRequestValidation.ParseOptionalUserId("1000");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ParseOptionalUserId_accepts_guid_string()
    {
        var id = Guid.Parse("01a0bcee-4744-7413-a677-3771b5ea5412");
        var result = OpenAccessClientRequestValidation.ParseOptionalUserId(id.ToString("D"));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(id, result.Value);
    }

    [TestMethod]
    public void ParseRequiredUsername_rejects_empty()
    {
        var result = OpenAccessClientRequestValidation.ParseRequiredUsername("   ");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ParseRequiredUsername_rejects_too_short()
    {
        var result = OpenAccessClientRequestValidation.ParseRequiredUsername("ab");
        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void ParseRequiredUsername_accepts_valid_name()
    {
        var result = OpenAccessClientRequestValidation.ParseRequiredUsername(" admin ");
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("admin", result.Value);
    }
}

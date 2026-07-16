using Full.NET.Abstractions.Results;

namespace Full.NET.UnitTests.Results;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Success_contains_value_and_no_error()
    {
        var result = Result<string>.Success("ok");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("ok", result.Value);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Failure_contains_error_and_no_value()
    {
        var error = new Error("tenant.not-found", "Tenant was not found.", ErrorType.NotFound);
        var result = Result<string>.Failure(error);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Value);
        Assert.AreEqual(error, result.Error);
    }
}

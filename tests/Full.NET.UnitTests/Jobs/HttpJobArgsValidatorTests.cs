using Full.NET.Modules.Jobs.Contracts;
using Full.NET.Modules.Jobs.Execution;

namespace Full.NET.UnitTests.Jobs;

[TestClass]
public sealed class HttpJobArgsValidatorTests
{
    [TestMethod]
    public void TryValidate_RejectsAuthorizationInPlainHeaders()
    {
        var args = new HttpJobArgs(
            "https://example.com/health",
            "GET",
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer secret",
            });

        Assert.IsFalse(HttpJobArgsValidator.TryValidate(args, true, out _));
        Assert.IsTrue(HttpJobArgsValidator.IsSensitiveHeaderName("Authorization"));
    }

    [TestMethod]
    public void TryValidate_AcceptsSecretHeaderReference()
    {
        var args = new HttpJobArgs(
            "https://example.com/health",
            "GET",
            null,
            new Dictionary<string, HttpJobSecretHeaderRef>
            {
                ["Authorization"] = new("jobs.http.secrets.demo"),
            });

        Assert.IsTrue(HttpJobArgsValidator.TryValidate(args, true, out _));
    }

    [TestMethod]
    public void TryValidate_RejectsUserInfoInUrl()
    {
        var args = new HttpJobArgs("https://user:pass@example.com", "GET");

        Assert.IsFalse(HttpJobArgsValidator.TryValidate(args, true, out _));
    }
}

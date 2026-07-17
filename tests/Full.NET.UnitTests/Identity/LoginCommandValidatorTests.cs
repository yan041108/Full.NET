using Full.NET.Modules.Identity.Features.Login;
using Full.NET.Modules.Identity.Http;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class LoginCommandValidatorTests
{
    [TestMethod]
    public async Task Valid_login_input_passes()
    {
        var validator = new LoginCommandValidator();

        var result = await validator.ValidateAsync(new Command(
            " admin ",
            "FullNet!2026Secure",
            new ClientRequestContext("127.0.0.1", "test")));

        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public async Task Blank_or_oversized_credentials_are_rejected()
    {
        var validator = new LoginCommandValidator();

        var blank = await validator.ValidateAsync(new Command(
            " ",
            " ",
            new ClientRequestContext(null, null)));
        var oversized = await validator.ValidateAsync(new Command(
            new string('u', 129),
            new string('p', 1025),
            new ClientRequestContext(null, null)));

        Assert.IsFalse(blank.IsValid);
        Assert.IsFalse(oversized.IsValid);
    }
}

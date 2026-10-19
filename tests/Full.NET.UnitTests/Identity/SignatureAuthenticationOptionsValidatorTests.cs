using Full.NET.Modules.Identity.Security;

namespace Full.NET.UnitTests.Identity;

/// <summary>单元测试（Identity）：Signature Authentication Options Validator。</summary>
[TestClass]
public sealed class SignatureAuthenticationOptionsValidatorTests
{
    [TestMethod]
    public void Validator_rejects_invalid_max_body_bytes_and_accepts_valid_configuration()
    {
        var validator = new SignatureAuthenticationOptionsValidator();
        Assert.IsTrue(validator.Validate(null, new SignatureAuthenticationOptions
        {
            MaxBodyBytes = 0,
        }).Failed);
        Assert.IsTrue(validator.Validate(null, new SignatureAuthenticationOptions
        {
            MaxBodyBytes = -1,
        }).Failed);
        Assert.IsTrue(validator.Validate(null, new SignatureAuthenticationOptions
        {
            MaxBodyBytes = SignatureAuthenticationOptions.MaxBodyBytesLimit + 1,
        }).Failed);
        Assert.IsFalse(validator.Validate(null, new SignatureAuthenticationOptions
        {
            MaxBodyBytes = 1024,
        }).Failed);
    }
}

using Full.NET.Modules.Identity.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Full.NET.UnitTests.Identity;

[TestClass]
public sealed class TotpAlgorithmTests
{
    [TestMethod]
    public void Compute_and_verify_accept_current_timestep_code()
    {
        var secret = TotpAlgorithm.GenerateSharedSecretBase32();
        var key = TotpAlgorithm.DecodeSharedSecret(secret);
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var code = TotpAlgorithm.ComputeCode(key, now);

        Assert.AreEqual(6, code.Length);
        Assert.IsTrue(TotpAlgorithm.Verify(key, code, now));
    }

    [TestMethod]
    public void Verify_accepts_adjacent_timestep_within_window()
    {
        var secret = TotpAlgorithm.GenerateSharedSecretBase32();
        var key = TotpAlgorithm.DecodeSharedSecret(secret);
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var previous = TotpAlgorithm.ComputeCode(
            key,
            now.AddSeconds(-TotpAlgorithm.StepSeconds));

        Assert.IsTrue(TotpAlgorithm.Verify(key, previous, now, window: 1));
    }

    [TestMethod]
    public void Verify_rejects_wrong_code()
    {
        var secret = TotpAlgorithm.GenerateSharedSecretBase32();
        var key = TotpAlgorithm.DecodeSharedSecret(secret);
        var now = DateTimeOffset.UtcNow;

        Assert.IsFalse(TotpAlgorithm.Verify(key, "000000", now));
    }

    [TestMethod]
    public void Data_protection_round_trips_shared_secret()
    {
        var provider = DataProtectionProvider.Create("Full.NET.UnitTests.Totp");
        var protector = new TotpSecretProtector(provider);
        var secret = TotpAlgorithm.GenerateSharedSecretBase32();

        var protectedValue = protector.Protect(secret);
        Assert.AreNotEqual(secret, protectedValue);
        Assert.AreEqual(secret, protector.Unprotect(protectedValue));
    }

    [TestMethod]
    public void Otpauth_uri_contains_issuer_and_secret()
    {
        var uri = TotpAlgorithm.BuildOtpAuthUri("Full.NET", "admin", "ABCDEFGH");
        StringAssert.Contains(uri, "otpauth://totp/");
        StringAssert.Contains(uri, "secret=ABCDEFGH");
        StringAssert.Contains(uri, "issuer=Full.NET");
    }
}

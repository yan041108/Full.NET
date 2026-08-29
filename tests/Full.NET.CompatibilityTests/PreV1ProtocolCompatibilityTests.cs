using Full.NET.Compatibility.AdminNet;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.CompatibilityTests;

[TestClass]
public sealed class PreV1ProtocolCompatibilityTests
{
    [TestMethod]
    public void Error_code_catalogs_use_canonical_pre_v1_values()
    {
        foreach (var code in TenancyErrorCodes.All)
        {
            Assert.IsFalse(
                PreV1ProtocolCompatibility.IsLegacyErrorCode(code),
                $"Tenancy 错误码仍使用 legacy 值：{code}");
        }

        foreach (var code in IdentityErrorCodes.All)
        {
            Assert.IsFalse(
                PreV1ProtocolCompatibility.IsLegacyErrorCode(code),
                $"Identity 错误码仍使用 legacy 值：{code}");
        }
    }

    [TestMethod]
    [DataRow("tenancy.identifier-exists", "tenancy.identifier_exists")]
    [DataRow("identity.bootstrap.invalid-password", "identity.bootstrap.invalid_password")]
    [DataRow("identity.login-succeeded", "identity.login_succeeded")]
    public void NormalizeErrorCode_maps_registered_legacy_values(
        string legacy,
        string canonical)
    {
        Assert.AreEqual(
            canonical,
            PreV1ProtocolCompatibility.NormalizeErrorCode(legacy));
        Assert.AreEqual(
            canonical,
            PreV1ProtocolCompatibility.NormalizeErrorCode(canonical));
    }

    [TestMethod]
    public void ToLegacyErrorCode_only_maps_registered_canonical_values()
    {
        Assert.AreEqual(
            "tenancy.domain-exists",
            PreV1ProtocolCompatibility.ToLegacyErrorCode("tenancy.domain_exists"));
        Assert.AreEqual(
            "identity.invalid_credentials",
            PreV1ProtocolCompatibility.ToLegacyErrorCode("identity.invalid_credentials"));
    }
}

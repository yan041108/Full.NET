using Full.NET.Modules.Ai.Domain;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiMcpRemoteConnectionValidatorTests
{
    [TestMethod]
    public void Valid_connection_key_is_accepted()
    {
        Assert.IsTrue(AiMcpRemoteConnectionValidator.IsValidConnectionKey("loopback"));
    }

    [TestMethod]
    public void Invalid_connection_key_is_rejected()
    {
        Assert.IsFalse(AiMcpRemoteConnectionValidator.IsValidConnectionKey("Loopback"));
        Assert.IsFalse(AiMcpRemoteConnectionValidator.IsValidConnectionKey("1bad"));
    }

    [TestMethod]
    public void Create_requires_safe_endpoint_and_service_token()
    {
        Assert.IsNull(AiMcpRemoteConnectionValidator.ValidateCreate(
            "loopback",
            "Loopback",
            "https://provider.example/mcp",
            "service-token"));

        Assert.IsNotNull(AiMcpRemoteConnectionValidator.ValidateCreate(
            "loopback",
            "Loopback",
            "ftp://provider.example/mcp",
            "service-token"));
    }
}

namespace Full.NET.ArchitectureTests;

/// <summary>
/// 验证 Hosting 只承载通用 HTTP 出口能力，不持有业务模块的 pre-v1 协议映射。
/// </summary>
[TestClass]
public sealed class HostingBusinessKnowledgeBoundaryTests
{
    [TestMethod]
    public void PreV1_error_code_map_is_owned_by_compatibility_adapter()
    {
        var hostingAssembly = typeof(Full.NET.Hosting.Api.IApiResultMapper).Assembly;
        var compatibilityAssembly =
            typeof(Full.NET.Compatibility.AdminNet.AdminNetApiResultMapper).Assembly;

        Assert.IsNull(
            hostingAssembly.GetType(
                "Full.NET.Hosting.Api.PreV1ProtocolCompatibility"),
            "Hosting 不得持有 Identity/Tenancy 的 pre-v1 error_code 映射。");
        Assert.IsNotNull(
            compatibilityAssembly.GetType(
                "Full.NET.Compatibility.AdminNet.PreV1ProtocolCompatibility"),
            "pre-v1 error_code 映射必须由 Admin.NET Compatibility 适配层拥有。");
    }
}

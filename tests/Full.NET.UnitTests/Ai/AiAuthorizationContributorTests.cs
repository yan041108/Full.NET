using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Contracts;

namespace Full.NET.UnitTests.Ai;

[TestClass]
public sealed class AiAuthorizationContributorTests
{
    [TestMethod]
    public void Contributor_registers_model_and_quota_permissions()
    {
        var catalog = AuthorizationCatalog.Create([new AiAuthorizationContributor()]);

        CollectionAssert.AreEquivalent(
            new[]
            {
                AiModelPermissions.Read,
                AiModelPermissions.Create,
                AiModelPermissions.Update,
                AiModelPermissions.Test,
                AiTenantQuotaPermissions.Read,
                AiTenantQuotaPermissions.Update,
                AiChatPermissions.Read,
                AiChatPermissions.Create,
                AiChatPermissions.Update,
                AiChatPermissions.Delete,
                AiChatPermissions.Send,
                AiChatPermissions.Cancel,
                AiAgentToolPermissions.CatalogRead,
                AiAgentToolPermissions.CallsRead,
                AiAgentRunPermissions.Read,
                AiAgentRunPermissions.Create,
                AiAgentRunPermissions.Cancel,
                AiAgentRunPermissions.Resume,
                AiAgentApprovalPermissions.Read,
                AiAgentApprovalPermissions.Request,
                AiAgentApprovalPermissions.Decide,
                AiAgentApprovalPermissions.Delegate,
                AiMcpPermissions.Read,
                AiMcpPermissions.Manage,
                AiMcpPermissions.RemoteInvoke,
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var navigation = catalog.Navigation.Single(item => item.Id == "ai-model-configs");
        Assert.AreEqual(AiModelPermissions.Read, navigation.RequiredPermission);
        Assert.AreEqual("/ai/model-configs", navigation.Path);
    }

    [TestMethod]
    public void Tenant_agent_permissions_include_the_tool_catalog_and_agent_run_actions()
    {
        var catalog = AuthorizationCatalog.Create([new AiAuthorizationContributor()]);
        var expectedScope = AuthorizationScope.Host | AuthorizationScope.Tenant;

        foreach (var code in new[]
                 {
                     AiAgentToolPermissions.CatalogRead,
                     AiAgentRunPermissions.Read,
                     AiAgentRunPermissions.Create,
                     AiAgentRunPermissions.Cancel,
                     AiAgentRunPermissions.Resume,
                 })
        {
            var permission = catalog.Permissions.Single(item => item.Code == code);
            Assert.AreEqual(expectedScope, permission.Scope, code);
        }
    }
}

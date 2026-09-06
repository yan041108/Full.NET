using Full.NET.Modules.Identity.Authorization;
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
            },
            catalog.Permissions.Select(permission => permission.Code).ToArray());

        var navigation = catalog.Navigation.Single(item => item.Id == "ai-model-configs");
        Assert.AreEqual(AiModelPermissions.Read, navigation.RequiredPermission);
        Assert.AreEqual("/ai/model-configs", navigation.Path);
    }
}

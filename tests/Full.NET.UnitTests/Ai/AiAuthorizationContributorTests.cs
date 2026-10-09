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
                AiPromptPermissions.Read, AiPromptPermissions.Create, AiPromptPermissions.Append, AiPromptPermissions.Publish,
                AiPromptPermissions.Deactivate, AiPromptPermissions.Rollback, AiPromptPermissions.Use,
                AiKnowledgeAnswerFeedbackPermissions.Read,
                AiKnowledgeAnswerFeedbackPermissions.Submit, AiKnowledgeAnswerFeedbackPermissions.Withdraw,
                AiKnowledgePermissions.Read,
                AiKnowledgePermissions.Create,
                AiKnowledgePermissions.Update,
                AiKnowledgePermissions.PolicyUpdate,
                AiKnowledgePermissions.MembersRead,
                AiKnowledgePermissions.OrganizationCandidatesRead,
                AiKnowledgePermissions.RoleCandidatesRead,
                AiKnowledgePermissions.OrganizationsRead,
                AiKnowledgePermissions.OrganizationsUpdate,
                AiKnowledgePermissions.RolesRead,
                AiKnowledgePermissions.RolesUpdate,
                AiKnowledgePermissions.MembersUpdate,
                AiKnowledgeDocumentPermissions.Read,
                AiKnowledgeDocumentPermissions.Upload,
                "ai.knowledge_documents.index_enqueue",
                "ai.knowledge_documents.index_read",
                "ai.knowledge_documents.index_retry",
                AiKnowledgeDocumentPermissions.VersionsRead,
                AiKnowledgeDocumentPermissions.SourceRead,
                AiKnowledgeDocumentPermissions.ImportsRead,
                AiKnowledgeDocumentPermissions.ImportsRetry,
                AiKnowledgeDocumentPermissions.Create,
                AiKnowledgeDocumentPermissions.Update,
                AiKnowledgeDocumentPermissions.Delete,
                AiKnowledgeDocumentPermissions.MembersRead,
                AiKnowledgeDocumentPermissions.OrganizationCandidatesRead,
                AiKnowledgeDocumentPermissions.RoleCandidatesRead,
                AiKnowledgeDocumentPermissions.OrganizationsRead,
                AiKnowledgeDocumentPermissions.OrganizationsUpdate,
                AiKnowledgeDocumentPermissions.RolesRead,
                AiKnowledgeDocumentPermissions.RolesUpdate,
                AiKnowledgeDocumentPermissions.MembersUpdate,
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
    public void Feedback_actions_are_grantable_on_the_chat_page_in_host_and_tenant_scopes()
    {
        var catalog = AuthorizationCatalog.Create([new AiAuthorizationContributor()]);
        var scope = AuthorizationScope.Host | AuthorizationScope.Tenant;
        Assert.AreEqual(scope, catalog.Permissions.Single(p => p.Code == AiChatPermissions.Read).Scope);
        foreach (var code in new[] { AiKnowledgeAnswerFeedbackPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Submit, AiKnowledgeAnswerFeedbackPermissions.Withdraw })
        {
            Assert.AreEqual(scope, catalog.Permissions.Single(p => p.Code == code).Scope);
            var action = catalog.Actions.Single(a => a.PermissionCode == code);
            Assert.AreEqual("ai-chat", action.NavigationId);
        }
    }

    [TestMethod]
    public void Knowledge_pages_register_exact_navigation_permissions()
    {
        var catalog = AuthorizationCatalog.Create([new AiAuthorizationContributor()]);
        var knowledge = catalog.Navigation.Single(item => item.Id == "ai-knowledge-bases");
        var documents = catalog.Navigation.Single(item => item.Id == "ai-knowledge-documents");
        Assert.AreEqual(AiKnowledgePermissions.Read, knowledge.RequiredPermission);
        Assert.AreEqual("/ai/knowledge-bases", knowledge.Path);
        Assert.AreEqual(AiKnowledgeDocumentPermissions.Read, documents.RequiredPermission);
        Assert.AreEqual("/ai/knowledge-documents", documents.Path);
    }

    [TestMethod]
    public void Tenant_agent_permissions_include_the_tool_catalog_and_agent_run_actions()
    {
        var catalog = AuthorizationCatalog.Create([new AiAuthorizationContributor()]);
        var expectedScope = AuthorizationScope.Host | AuthorizationScope.Tenant;

        foreach (var code in new[]
                 {
                     AiKnowledgePermissions.Read,
                     AiKnowledgePermissions.Create,
                     AiKnowledgePermissions.Update,
                     AiKnowledgePermissions.PolicyUpdate,
                     AiKnowledgePermissions.MembersRead,
                     AiKnowledgePermissions.OrganizationCandidatesRead,
                AiKnowledgePermissions.RoleCandidatesRead,
                     AiKnowledgePermissions.OrganizationsRead,
                AiKnowledgePermissions.OrganizationsUpdate,
                AiKnowledgePermissions.RolesRead,
                     AiKnowledgePermissions.RolesUpdate,
                     AiKnowledgePermissions.MembersUpdate,
                     AiKnowledgeDocumentPermissions.Read,
                     AiKnowledgeDocumentPermissions.Create,
                     AiKnowledgeDocumentPermissions.Update,
                     AiKnowledgeDocumentPermissions.Delete,
                     AiKnowledgeDocumentPermissions.MembersRead,
                     AiKnowledgeDocumentPermissions.OrganizationCandidatesRead,
                AiKnowledgeDocumentPermissions.RoleCandidatesRead,
                     AiKnowledgeDocumentPermissions.OrganizationsRead,
                AiKnowledgeDocumentPermissions.OrganizationsUpdate,
                AiKnowledgeDocumentPermissions.RolesRead,
                     AiKnowledgeDocumentPermissions.RolesUpdate,
                     AiKnowledgeDocumentPermissions.MembersUpdate,
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

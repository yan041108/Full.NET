using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Workflow.Contracts;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod] public Task R5_simulation_selections_with_sql_server() => VerifyR5Async(DatabaseProvider.SqlServer, selectionsOnly: true);
    [TestMethod] public Task R5_simulation_selections_with_mysql() => VerifyR5Async(DatabaseProvider.MySql, selectionsOnly: true);

    private static async Task<(HttpClient Client, Guid UserId)> CreateSimulationPreviewerAsync(FullNetApiFactory factory,
        Func<HttpClient> createClient, HttpClient tenantOwner, Guid? tenant)
    {
        var name = "simulation.reader." + Guid.CreateVersion7().ToString("N");
        var user = await factory.CreateHostIdentityAsync(name, [WorkflowPermissions.DefinitionsRead, WorkflowPermissions.FormsRead,
            WorkflowPermissions.DefinitionsSimulate, "tenancy.tenants.switch"], password: FullNetApiFactory.TestPassword);
        var client = createClient(); client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "http://localhost");
        var access = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, name);
        client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        try
        {
            if (tenant is { } tenantId)
            {
                var email = name + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(user.UserId, email, name);
                var invitation = await PostAsync(tenantOwner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
                await PostAsync(client, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
                client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, access, tenantId));
            }
            return (client, user.UserId);
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task VerifySimulationSelectionsAsync(HttpClient author, HttpClient previewer, HttpClient wrongScope,
        Guid selected, DbConnection db)
    {
        var authorId = (await author.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();
        var form = await PostAsync(author, "/api/v1/workflow/forms", new { formKey = "sim.select." + Guid.CreateVersion7().ToString("N"),
            draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields = new[] {
                new { fieldKey = "amount", fieldTypeKey = "integer", required = true, constraints = new { minimum = 0, maximum = 100 } } } } } } }, HttpStatusCode.Created);
        var formId = form.GetProperty("id").GetGuid();
        var formVersion = (await PostAsync(author, $"/api/v1/workflow/forms/{formId:D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        var created = await PostAsync(author, "/api/v1/workflow/definitions", new { definitionKey = "sim.select." + Guid.CreateVersion7().ToString("N"),
            draft = SelectionDraft("publishedChoice", [authorId]) }, HttpStatusCode.Created);
        var definition = created.GetProperty("id").GetGuid();
        var published = (await PostAsync(author, $"/api/v1/workflow/definitions/{definition:D}/publish", new { expectedRevision = 1, formVersionId = formVersion })).GetProperty("id").GetGuid();
        using (var update = await author.PutAsJsonAsync($"/api/v1/workflow/definitions/{definition:D}/draft", new { expectedRevision = 2,
                   draft = SelectionDraft("reviewChoice", [selected, authorId]) }))
            Assert.AreEqual(HttpStatusCode.OK, update.StatusCode, await update.Content.ReadAsStringAsync());
        var prefix = $"/api/v1/workflow/definitions/{definition:D}/simulation/starter-selections";
        var query = $"?expectedRevision=3&formVersionId={formVersion:D}";
        var candidatePath = prefix + "/reviewChoice/candidates" + query + "&page=1&pageSize=20";
        var facts = await R5FactsAsync(db);
        var metadata = await previewer.GetFromJsonAsync<JsonElement>(prefix + query);
        Assert.AreEqual(definition, metadata.GetProperty("definitionId").GetGuid());
        Assert.AreEqual(3L, metadata.GetProperty("draftRevision").GetInt64());
        Assert.AreEqual(formVersion, metadata.GetProperty("formVersionId").GetGuid());
        Assert.AreEqual("reviewChoice", metadata.GetProperty("items")[0].GetProperty("selectionKey").GetString());
        var candidates = await previewer.GetFromJsonAsync<JsonElement>(candidatePath);
        Assert.AreEqual(2L, candidates.GetProperty("total").GetInt64());
        CollectionAssert.AreEquivalent(new[] { selected, authorId }, candidates.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
        var old = await author.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definition-versions/{published:D}/starter-selections");
        Assert.AreEqual("publishedChoice", old.GetProperty("items")[0].GetProperty("selectionKey").GetString());
        var oldCandidates = await author.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definition-versions/{published:D}/starter-selections/publishedChoice/candidates?page=1&pageSize=20");
        Assert.AreEqual(1L, oldCandidates.GetProperty("total").GetInt64());
        Assert.AreEqual(authorId, oldCandidates.GetProperty("items")[0].GetProperty("id").GetGuid());

        var simulate = $"/api/v1/workflow/definitions/{definition:D}/simulate";
        var success = await PostAsync(previewer, simulate, new { expectedRevision = 3, formVersionId = formVersion,
            values = new { amount = 12 }, assigneeSelections = new Dictionary<string, Guid[]> { ["reviewChoice"] = [selected] } });
        Assert.AreEqual(1, success.GetProperty("trace").EnumerateArray().Single(item => item.GetProperty("nodeKey").GetString() == "claim").GetProperty("memberCount").GetInt32());
        foreach (var choices in new Dictionary<string, Guid[]>[] { new(), new() { ["reviewChoice"] = [Guid.CreateVersion7()] }, new() { ["publishedChoice"] = [authorId] } })
        {
            var denied = await PostAsync(previewer, simulate, new { expectedRevision = 3, formVersionId = formVersion,
                values = new { amount = 12 }, assigneeSelections = choices }, HttpStatusCode.BadRequest);
            Assert.AreEqual("workflow.definition.assignee_policy_invalid", denied.GetProperty("code").GetString());
        }
        using (var forbiddenStart = await previewer.PostAsJsonAsync("/api/v1/workflow/instances", new { definitionVersionId = published,
                   businessType = "simulation.preview", businessId = Guid.CreateVersion7().ToString("N"), initialValues = new { amount = 12 },
                   assigneeSelections = new Dictionary<string, Guid[]> { ["publishedChoice"] = [authorId] }, idempotencyKey = Guid.CreateVersion7().ToString("N") }))
            Assert.AreEqual(HttpStatusCode.Forbidden, forbiddenStart.StatusCode, "预览资格不能隐式取得真实启动权限。");
        foreach (var path in new[] { prefix + query, candidatePath })
        {
            using var hidden = await wrongScope.GetAsync(path); Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
            using var stale = await previewer.GetAsync(path.Replace("expectedRevision=3", "expectedRevision=2", StringComparison.Ordinal)); Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        }
        using (var outside = await previewer.GetAsync(prefix + "/publishedChoice/candidates" + query)) Assert.AreEqual(HttpStatusCode.NotFound, outside.StatusCode);
        using (var broad = await previewer.GetAsync(candidatePath.Replace("pageSize=20", "pageSize=101", StringComparison.Ordinal))) Assert.AreEqual(HttpStatusCode.BadRequest, broad.StatusCode);
        // 使用真实目录失效事实；不伪造选择结果，也不放松范围验证。
        await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=0 WHERE Id=@Id", new { Id = authorId });
        try
        {
            await PostAsync(previewer, simulate, new { expectedRevision = 3, formVersionId = formVersion, values = new { amount = 12 },
                assigneeSelections = new Dictionary<string, Guid[]> { ["reviewChoice"] = [authorId] } }, HttpStatusCode.BadRequest);
        }
        finally { await db.ExecuteAsync("UPDATE fn_identity_user SET IsActive=1 WHERE Id=@Id", new { Id = authorId }); }
        Assert.AreEqual(facts, await R5FactsAsync(db), "选人预览、拒绝请求及仿真均不能产生Workflow写入。");
    }

    private static object SelectionDraft(string key, Guid[] users) => new { schemaVersion = 1, workflowKindKey = "automation", nodes = new object[] {
        new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "claim" } } },
        new { nodeKey = "claim", nodeTypeKey = "human.claim", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" },
            claimPolicy = new { allowRelease = true }, assigneePolicy = new { sources = new[] { new { resolverKindKey = "starter_selected_users",
                selectionPolicy = new { selectionKey = key, modeKey = "single", maximumUsers = 1, range = new { kindKey = "specified_users", userIds = users } } } } } } },
        new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } } } };
}

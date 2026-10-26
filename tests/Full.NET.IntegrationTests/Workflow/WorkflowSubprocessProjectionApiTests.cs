using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;
using static Full.NET.IntegrationTests.Workflow.WorkflowDurableDelayApiTests;

namespace Full.NET.IntegrationTests.Workflow;

/// <summary>父子真实文件/明细投影在Host和Tenant执行；不同上传者、回滚及失配来源不能被裸FileId绕过。</summary>
[TestClass]
public sealed class WorkflowSubprocessProjectionApiTests
{
    [TestMethod]
    public async Task Subprocess_projection_with_sql_server() => await VerifyAsync(DatabaseProvider.SqlServer, await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
    [TestMethod]
    public async Task Subprocess_projection_with_mysql() => await VerifyAsync(DatabaseProvider.MySql, await SharedDatabaseFixture.CreateMySqlDatabaseAsync());

    private static async Task VerifyAsync(DatabaseProvider provider, string connection)
    {
        var fault = new LinkFault();
        using var factory = new FullNetApiFactory(provider, connection, configureTestServices: services => {
            var descriptor = services.Last(item => item.ServiceType == typeof(ICommandExecutor));
            var create = descriptor.ImplementationFactory ?? throw new InvalidOperationException("Missing command executor factory.");
            services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new LinkFaultExecutor((ICommandExecutor)create(scope), fault)));
        });
        await factory.InitializeAsync(useSchemaTemplate: false);
        var owner = await factory.CreateHostIdentityAsync("projection.owner." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish,
            WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.InstancesStart,
            WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesCancel, "tenancy.tenants.switch"]);
        var reviewer = await factory.CreateHostIdentityAsync("projection.reviewer." + Guid.NewGuid().ToString("N"), [WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.TodosSign, "tenancy.tenants.switch"]);
        using var ownerClient = factory.CreateClientForHost("localhost"); ownerClient.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        using var reviewerClient = factory.CreateClientForHost("localhost"); reviewerClient.DefaultRequestHeaders.Authorization = new("Bearer", reviewer.AccessToken);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        await VerifyScopeAsync(factory, db, ownerClient, reviewerClient, owner.UserId, reviewer.UserId, "host", fault);
        using var probe = factory.CreateClientForHost("acme.localhost"); var tenant = await IntegrationTestTenantContextHelper.GetCurrentTenantAsync(probe);
        foreach (var user in new[] { owner.UserId, reviewer.UserId })
        {
            var hostRole = await db.ExecuteScalarAsync<Guid>("SELECT RoleId FROM fn_identity_user_role WHERE UserId=@user", new { user });
            var role = Guid.CreateVersion7();
            await db.ExecuteAsync("INSERT INTO fn_identity_tenant_member (Id,TenantId,UserId,MemberRole,Status,CreatedAtUtc,UpdatedAtUtc,Version) VALUES (@id,@tenant,@user,'Member','Active',@now,@now,1)",
                new { id = Guid.CreateVersion7(), tenant = tenant.Id, user, now = DateTimeOffset.UtcNow });
            await db.ExecuteAsync("""
                INSERT INTO fn_identity_role (Id,TenantId,ScopeKey,Code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version)
                SELECT @role,@tenant,@scope,@code,Name,IsSystem,IsActive,IsSuperAdministrator,DataScopeKind,CreatedAtUtc,Version FROM fn_identity_role WHERE Id=@hostRole
                """, new { role, tenant = tenant.Id, scope = $"tenant:{tenant.Id:N}", code = "projection-" + role.ToString("N"), hostRole });
            await db.ExecuteAsync("INSERT INTO fn_identity_role_permission (RoleId,PermissionCode) SELECT @role,PermissionCode FROM fn_identity_role_permission WHERE RoleId=@hostRole", new { role, hostRole });
            await db.ExecuteAsync("INSERT INTO fn_identity_user_role (UserId,RoleId) VALUES (@user,@role)", new { user, role });
        }
        var ownerToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(ownerClient, owner.AccessToken, tenant.Id);
        var reviewerToken = await IntegrationTestTenantContextHelper.SwitchToTenantAsync(reviewerClient, reviewer.AccessToken, tenant.Id);
        using var tenantOwner = factory.CreateClientForHost("localhost"); tenantOwner.DefaultRequestHeaders.Authorization = new("Bearer", ownerToken);
        using var tenantReviewer = factory.CreateClientForHost("localhost"); tenantReviewer.DefaultRequestHeaders.Authorization = new("Bearer", reviewerToken);
        await VerifyScopeAsync(factory, db, tenantOwner, tenantReviewer, owner.UserId, reviewer.UserId, $"tenant:{tenant.Id:N}", fault);
    }

    private static async Task VerifyScopeAsync(FullNetApiFactory factory, DbConnection db, HttpClient owner, HttpClient reviewer, Guid ownerId, Guid reviewerId, string scopeKey, LinkFault fault)
    {
        object Node(string key, string kind, object config) => new { nodeKey = key, nodeTypeKey = kind, nodeSchemaVersion = 1, config };
        object File(string key) => new { fieldKey = key, fieldTypeKey = "attachment", required = false, constraints = new { maxCount = 2, maxSizeBytes = 1024, allowedExtensions = new[] { "png" } } };
        object Table(string key) => new { fieldKey = key, fieldTypeKey = "subtable", required = false, constraints = new { maxRows = 4,
            columns = new object[] { new { columnKey = "label", fieldTypeKey = "text", required = true, constraints = new { maxLength = 32 } },
                new { columnKey = "amount", fieldTypeKey = "integer", required = true, constraints = new { minimum = 0, maximum = 100 } } } } };
        async Task<Guid> Form(object[] fields)
        {
            var created = await PostAsync(owner, "/api/v1/workflow/forms", new { formKey = "projection." + Guid.NewGuid().ToString("N"),
                draft = new { schemaVersion = 1, adapterVersion = 1, sections = new[] { new { sectionKey = "main", fields } } } }, HttpStatusCode.Created);
            return (await PostAsync(owner, $"/api/v1/workflow/forms/{created.GetProperty("id").GetGuid():D}/publish", new { expectedRevision = 1 })).GetProperty("id").GetGuid();
        }
        var childForm = await Form([File("inputDocs"), File("outputDocs"), Table("inputRows"), Table("outputRows")]);
        var child = await PublishNodesAsync(owner, childForm, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }), Node("review", "human.approval", new {
            nextNodeKeys = new[] { "end" }, signRequired = true, fieldPolicies = new { inputDocs = "readOnly", inputRows = "readOnly", outputDocs = "editable", outputRows = "editable" },
            assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { reviewerId } } } } }), Node("end", "end", new { })]);
        var parentForm = await Form([File("sourceDocs"), File("targetDocs"), Table("sourceRows"), Table("targetRows")]);
        async Task<Guid> Publish(bool multi)
        {
            var config = new Dictionary<string, object> { ["nextNodeKeys"] = new[] { "end" }, ["fieldPolicies"] = new { sourceDocs = "readOnly", sourceRows = "readOnly", targetDocs = "editable", targetRows = "editable" },
                ["subprocessPolicy"] = new { definitionVersionId = child, inputFields = new { inputDocs = "sourceDocs", inputRows = "sourceRows" }, outputFields = new { targetDocs = "outputDocs", targetRows = "outputRows" } } };
            if (multi) config["multiInstancePolicy"] = new { modeKey = "parallel", source = new { kindKey = "fixed_count", count = 2 }, requiredPercentage = 100, emptyModeKey = "reject", outputReducers = new { targetDocs = "append", targetRows = "append" } };
            return await PublishNodesAsync(owner, parentForm, [Node("start", "start", new { nextNodeKeys = new[] { "review" } }), Node("review", "human.approval", new {
                nextNodeKeys = new[] { "children" }, signRequired = true, fieldPolicies = new { sourceDocs = "editable", sourceRows = "editable" },
                assigneePolicy = new { sources = new[] { new { resolverKindKey = "specified_users", userIds = new[] { reviewerId } } } } }), Node("children", "process.subprocess", config), Node("end", "end", new { })]);
        }
        async Task Tick()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkflowSubprocessProcessor>().ProcessPendingAsync(default);
            await scope.ServiceProvider.GetRequiredService<WorkflowActivityWorkProcessor>().ProcessPendingAsync(default);
        }
        async Task Until(Func<Task<bool>> condition, string failure)
        { for (var i = 0; i < 48; i++) { if (await condition()) return; await Tick(); } Assert.Fail(failure); }
        async Task<Guid> Todo(Guid instance) => await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance });
        async Task<Guid> Upload(Guid todo)
        {
            using var upload = new MultipartFormDataContent(); var bytes = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2e8AAAAASUVORK5CYII="));
            bytes.Headers.ContentType = new("image/png"); upload.Add(bytes, "file", "projection.png");
            using var response = await reviewer.PostAsync($"/api/v1/workflow/todos/{todo:D}/signature-uploads?expectedRevision=1", upload);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fileId").GetGuid();
        }
        async Task Approve(Guid instance, Guid signature, object patch)
        {
            var todo = await Todo(instance); var detail = await reviewer.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}");
            await PostAsync(reviewer, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = detail.GetProperty("revision").GetInt64(), expectedFormRevision = detail.GetProperty("submissionRevision").GetInt64(),
                fieldPatch = patch, signatureFileId = signature, comment = "文件明细投影验收", idempotencyKey = Guid.NewGuid().ToString("N") });
        }
        async Task<(Guid Instance, Guid File)> Start(Guid version)
        {
            var started = await PostAsync(owner, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "projection.test", businessId = Guid.NewGuid().ToString("N"),
                initialValues = new { sourceDocs = Array.Empty<Guid>(), targetDocs = Array.Empty<Guid>(), sourceRows = Array.Empty<object>(), targetRows = Array.Empty<object>() }, idempotencyKey = Guid.NewGuid().ToString("N") }, HttpStatusCode.Created);
            var instance = started.GetProperty("id").GetGuid(); var file = await Upload(await Todo(instance));
            await Approve(instance, file, new { sourceDocs = new[] { file }, sourceRows = new[] { new { label = "source", amount = 1 } } }); return (instance, file);
        }
        var version = await Publish(false); var success = await Start(version);
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = success.Instance }) == 1, "真实单子根未关联");
        var childId = await db.ExecuteScalarAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@id", new { id = success.Instance });
        var childInput = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id = childId }))!);
        Assert.AreEqual(success.File, childInput.GetProperty("inputDocs")[0].GetGuid()); Assert.AreEqual("source", childInput.GetProperty("inputRows")[0].GetProperty("label").GetString());
        Assert.AreEqual(ownerId, await db.ExecuteScalarAsync<Guid>("SELECT StartedById FROM fn_workflow_instance WHERE Id=@id", new { id = childId })); Assert.AreNotEqual(ownerId, reviewerId);
        var outputFile = await Upload(await Todo(childId)); await Approve(childId, outputFile, new { outputDocs = new[] { outputFile }, outputRows = new[] { new { label = "returned", amount = 7 } } });
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = success.Instance }) == "completed", "单子投影未完成");
        var result = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id = success.Instance }))!);
        Assert.AreEqual(outputFile, result.GetProperty("targetDocs")[0].GetGuid()); Assert.AreEqual(7, result.GetProperty("targetRows")[0].GetProperty("amount").GetInt32());
        Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_form_submission_attachment WHERE InstanceId=@id AND FieldKey='targetDocs' AND FileId=@file AND TenantScopeKey=@scopeKey", new { id = success.Instance, file = outputFile, scopeKey }));
        if (scopeKey != "host") Assert.AreEqual(reviewerId, await db.ExecuteScalarAsync<Guid>("SELECT OwnerResourceId FROM fn_workflow_form_submission_attachment WHERE InstanceId=@id AND FieldKey='targetDocs'", new { id = success.Instance }));

        var multiple = await Start(await Publish(true));
        await Until(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = multiple.Instance }) == 2, "多项文件子根未启动");
        var children = (await db.QueryAsync<Guid>("SELECT ChildInstanceId FROM fn_workflow_subprocess_item WHERE InstanceId=@id ORDER BY SequenceNo", new { id = multiple.Instance })).ToArray();
        var outputFiles = new List<Guid>();
        for (var i = 0; i < children.Length; i++) { var file = await Upload(await Todo(children[i])); outputFiles.Add(file);
            await Approve(children[i], file, new { outputDocs = new[] { file }, outputRows = new[] { new { label = "item-" + i, amount = i + 1 } } }); }
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = multiple.Instance }) == "completed", "多项文件明细未归约");
        var merged = JsonSerializer.Deserialize<JsonElement>((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@id", new { id = multiple.Instance }))!);
        CollectionAssert.AreEqual(outputFiles.ToArray(), merged.GetProperty("targetDocs").EnumerateArray().Select(value => value.GetGuid()).ToArray());
        CollectionAssert.AreEqual(new[] { "item-0", "item-1" }, merged.GetProperty("targetRows").EnumerateArray().Select(value => value.GetProperty("label").GetString()).ToArray());

        var rollback = await Start(version); fault.Arm();
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = rollback.Instance }) == "suspended", "关联失败没有挂起父根");
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = rollback.Instance }));
        if (scopeKey == "host") {
            Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@file AND State='pending'", new { file = rollback.File }));
            Assert.IsTrue(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@file AND State='released'", new { file = rollback.File }) > 0);
        }
        var missing = await Start(version);
        await db.ExecuteAsync("DELETE FROM fn_workflow_form_submission_attachment WHERE InstanceId=@id AND FieldKey='sourceDocs' AND FileId=@file", new { id = missing.Instance, file = missing.File });
        await Until(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@id", new { id = missing.Instance }) == "suspended", "缺少原字段真实引用仍启动子根");
        Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_subprocess_item WHERE InstanceId=@id AND ChildInstanceId IS NOT NULL", new { id = missing.Instance }));
        if (scopeKey != "host") Assert.AreEqual(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_file_reference_claim WHERE FileId=@file", new { file = outputFile }));
    }
    private sealed class LinkFault { private int armed; public void Arm() => Interlocked.Exchange(ref armed, 1); public bool Take() => Interlocked.Exchange(ref armed, 0) == 1; }
    private sealed class LinkFaultExecutor(ICommandExecutor inner, LinkFault fault) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
            statement.Name == "workflow.subprocess_item.link_child" && fault.Take() ? Task.FromResult(0) : inner.ExecuteAsync(statement, parameters, cancellationToken);
    }
}

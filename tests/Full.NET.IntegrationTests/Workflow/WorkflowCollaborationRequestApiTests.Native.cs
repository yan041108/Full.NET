using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Full.NET.Modules.Workflow.Execution;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod, TestCategory("NativeWorkflowApi")]
    public Task Workflow_native_api_r5_with_sql_server() => VerifyNativeWorkflowAsync(DatabaseProvider.SqlServer, apiOnly: true);
    [TestMethod, TestCategory("NativeWorkflowApi")]
    public Task Workflow_native_api_r5_with_mysql() => VerifyNativeWorkflowAsync(DatabaseProvider.MySql, apiOnly: true);
    [TestMethod, TestCategory("NativeWorkflowApi")]
    public Task Workflow_native_simulation_selections_with_sql_server() => VerifyNativeWorkflowAsync(DatabaseProvider.SqlServer, apiOnly: true, simulationSelectionsOnly: true);
    [TestMethod, TestCategory("NativeWorkflowApi")]
    public Task Workflow_native_simulation_selections_with_mysql() => VerifyNativeWorkflowAsync(DatabaseProvider.MySql, apiOnly: true, simulationSelectionsOnly: true);
    [TestMethod, TestCategory("NativeWorkflowApi")]
    public Task Workflow_native_todo_paging_with_mysql() => VerifyNativeWorkflowAsync(DatabaseProvider.MySql, apiOnly: true, pagingOnly: true);
    [TestMethod, TestCategory("NativeWorkflow")]
    public Task Workflow_native_api_worker_with_sql_server() => VerifyNativeWorkflowAsync(DatabaseProvider.SqlServer);
    [TestMethod, TestCategory("NativeWorkflow")]
    public Task Workflow_native_api_worker_with_mysql() => VerifyNativeWorkflowAsync(DatabaseProvider.MySql);
    [TestMethod, TestCategory("NativeWorkflowWindows")]
    public Task Workflow_windows_release_client_with_native_sql_server() => VerifyNativeWorkflowAsync(DatabaseProvider.SqlServer, desktopOnly: true);

    /// <summary>Windows测试控制器也必须真正启动Linux ELF；缺失产物直接失败，不以平台跳过代替运行。</summary>
    private static async Task VerifyNativeWorkflowAsync(DatabaseProvider provider, bool desktopOnly = false, bool apiOnly = false, bool pagingOnly = false, bool s3 = false, bool simulationSelectionsOnly = false)
    {
        if (desktopOnly)
        {
            Assert.IsTrue(OperatingSystem.IsWindows(), "该用例必须实际运行Windows发布客户端。");
            Assert.AreEqual("1", Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_FLUTTER_NATIVE"), "必须提供真实交互桌面，不能回退为HTTP夹具审批。");
        }
        var nativeRoot = Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_NATIVE_ROOT") ?? throw new InvalidOperationException("The owned native publication root is required.");
        var desktopExecutable = desktopOnly ? await NativeWindowsArtifactAsync(nativeRoot) : null;
        var api = await NativePublicationAsync(nativeRoot, "latest.json", "Full.NET.Host.Api");
        var worker = apiOnly ? null : await NativePublicationAsync(nativeRoot, "latest-worker.json", "Full.NET.Host.Worker");
        await using var storage = s3 ? await NativeWorkflowS3.StartAsync() : null;
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var data = Directory.CreateDirectory(Path.Combine(nativeRoot, "runtime-" + provider + "-" + Guid.CreateVersion7().ToString("N"))).FullName;
        // 引导只迁移/播种；数据库内保护载荷与实际API/Worker使用同一独享密钥目录。
        using var bootstrap = new FullNetApiFactory(provider, connection, new Dictionary<string, string?> {
            ["DataProtection:KeyRingPath"] = Path.Combine(data, "keys"), ["Files:Local:RootPath"] = Path.Combine(data, "files") });
        await bootstrap.InitializeAsync(useSchemaTemplate: false);
        await using DbConnection db = provider == DatabaseProvider.SqlServer ? new SqlConnection(connection) : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(connection));
        var linuxConnection = provider == DatabaseProvider.SqlServer ? new SqlConnectionStringBuilder(connection) {
            DataSource = "host.docker.internal" + new SqlConnectionStringBuilder(connection).DataSource[new SqlConnectionStringBuilder(connection).DataSource.LastIndexOf(',')..] }.ConnectionString
            : new MySqlConnectionStringBuilder(connection) { Server = "host.docker.internal" }.ConnectionString;
        await using var apiHost = NativeHost(api, provider, linuxConnection, data, false, storage?.NativeSettings);
        try
        {
        await StartNativeAsync(apiHost, Path.Combine(data, "api-startup.log"));
        using var owner = NativeClient(apiHost); owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin"));
        var username = "native.workflow." + Guid.CreateVersion7().ToString("N");
        await bootstrap.CreateHostIdentityAsync(username, WorkflowPermissions.All.Concat(["tenancy.tenants.switch"]).ToArray(), password: FullNetApiFactory.TestPassword);
        using var source = NativeClient(apiHost); source.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestAuthHelper.LoginAsHostUserAsync(source, username));
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        var admin = owner.DefaultRequestHeaders.Authorization!.Parameter!;
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, admin, tenant.Id));
        var memberName = "native.workflow.member." + Guid.CreateVersion7().ToString("N");
        var member = await bootstrap.CreateHostIdentityAsync(memberName, WorkflowPermissions.All.Concat(["tenancy.tenants.switch"]).ToArray(), password: FullNetApiFactory.TestPassword);
        var email = memberName + "@example.com"; await bootstrap.EnsureHostUserProfileEmailAsync(member.UserId, email, memberName);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        using var target = NativeClient(apiHost); var memberToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(target, memberName); target.DefaultRequestHeaders.Authorization = new("Bearer", memberToken);
        await PostAsync(target, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        target.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(target, memberToken, tenant.Id));
            if (apiOnly)
            {
                if (simulationSelectionsOnly)
                {
                    var hostPreview = await CreateSimulationPreviewerAsync(bootstrap, () => NativeClient(apiHost), owner, null);
                    var tenantPreview = await CreateSimulationPreviewerAsync(bootstrap, () => NativeClient(apiHost), owner, tenant.Id);
                    using (hostPreview.Client) using (tenantPreview.Client)
                    {
                        await VerifySimulationSelectionsAsync(source, hostPreview.Client, target, hostPreview.UserId, db);
                        await VerifySimulationSelectionsAsync(target, tenantPreview.Client, source, tenantPreview.UserId, db);
                    }
                }
                else if (pagingOnly)
                {
                    await NativeTodoPagingAsync(source, target, db);
                    await NativeTodoPagingAsync(target, source, db);
                }
                else
                {
                    await NativeR5Async(source, target, db);
                    await NativeR5Async(target, source, db);
                }
            }
            else
            {
            var hostUser = await source.GetFromJsonAsync<CurrentUserResponse>("/api/v1/me"); Assert.IsNotNull(hostUser);
            await NativeCollaborationAsync(target, source, db, hostUser.Id, null, worker!, provider, linuxConnection, data, true, "success", desktopOnly ? username : null, desktopExecutable, storage?.NativeSettings);
            if (!desktopOnly)
            {
                await NativeCollaborationAsync(target, source, db, hostUser.Id, null, worker!, provider, linuxConnection, data, false, "cancel", storageSettings: storage?.NativeSettings);
                await NativeCollaborationAsync(source, target, db, member.UserId, tenant.Id, worker!, provider, linuxConnection, data, false, "revoke", storageSettings: storage?.NativeSettings);
            }
            }
            Assert.AreEqual(api.Digest, await NativeDigestAsync(Path.Combine(api.Directory, "publish", "Full.NET.Host.Api")));
            if (worker is not null) Assert.AreEqual(worker.Digest, await NativeDigestAsync(Path.Combine(worker.Directory, "publish", "Full.NET.Host.Worker")));
            if (storage is not null) await storage.AssertObjectsAsync(FileBytes, expectedCount: 4);
            await StopNativeAsync(apiHost, Path.Combine(data, "api.log"));
        }
        finally
        {
            var logs = await apiHost.GetLogsAsync(); await File.WriteAllTextAsync(Path.Combine(data, "api.log"), logs.Stdout + logs.Stderr);
            Console.WriteLine("Workflow Native运行证据：" + data);
        }
    }

    /// <summary>分页物化修复仅需真实API和数据库；Host/Tenant均须有实际待办及已办，不能用零行查询代替。</summary>
    private static async Task NativeTodoPagingAsync(HttpClient client, HttpClient outsider, DbConnection db)
    {
        var asset = await R5AssetAsync(client, false);
        var version = (await PostAsync(client, $"/api/v1/workflow/definitions/{asset.Definition:D}/publish", new { expectedRevision = 1, formVersionId = asset.Form })).GetProperty("id").GetGuid();
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "native.paging", businessId = Guid.CreateVersion7().ToString("N"), initialValues = new { amount = 12 }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
        var instance = started.GetProperty("id").GetGuid();
        var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@instance AND StatusKey='active'", new { instance });
        var pendingPath = $"/api/v1/workflow/todos/mine?definitionKey={asset.Key}&page=1&pageSize=10";
        var pending = await client.GetFromJsonAsync<JsonElement>(pendingPath);
        Assert.AreEqual(1L, pending.GetProperty("total").GetInt64());
        var row = pending.GetProperty("items")[0]; Assert.AreEqual(todo, row.GetProperty("id").GetGuid());
        Assert.AreEqual(instance, row.GetProperty("instanceId").GetGuid());
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("completedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, row.GetProperty("resultActionKey").ValueKind);
        Assert.AreEqual(0L, (await outsider.GetFromJsonAsync<JsonElement>(pendingPath)).GetProperty("total").GetInt64());
        var runtime = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todo:D}/runtime");
        await PostAsync(client, $"/api/v1/workflow/todos/{todo:D}/approve", new { expectedRevision = runtime.GetProperty("revision").GetInt64(), expectedFormRevision = runtime.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = Guid.CreateVersion7().ToString("N") });
        var historyPath = $"/api/v1/workflow/todos/mine/history?definitionKey={asset.Key}&page=1&pageSize=10";
        var history = await client.GetFromJsonAsync<JsonElement>(historyPath);
        Assert.AreEqual(1L, history.GetProperty("total").GetInt64());
        Assert.AreEqual(todo, history.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.AreEqual("approve", history.GetProperty("items")[0].GetProperty("resultActionKey").GetString());
        Assert.AreEqual(JsonValueKind.String, history.GetProperty("items")[0].GetProperty("completedAtUtc").ValueKind);
        Assert.AreEqual(0L, (await outsider.GetFromJsonAsync<JsonElement>(historyPath)).GetProperty("total").GetInt64());
    }

    private static async Task NativeR5Async(HttpClient client, HttpClient outsider, DbConnection db)
    {
        var asset = await R5AssetAsync(client, false); var before = await R5FactsAsync(db);
        var result = await PostAsync(client, $"/api/v1/workflow/definitions/{asset.Definition:D}/simulate", new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = 12 } });
        Assert.AreEqual("large", result.GetProperty("trace").EnumerateArray().Single(row => row.GetProperty("nodeKey").GetString() == "gateway").GetProperty("outcomeKey").GetString());
        Assert.AreEqual(before, await R5FactsAsync(db));
        var version = (await PostAsync(client, $"/api/v1/workflow/definitions/{asset.Definition:D}/publish", new { expectedRevision = 1, formVersionId = asset.Form })).GetProperty("id").GetGuid();
        var started = await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "native.workflow", businessId = Guid.CreateVersion7().ToString("N"), initialValues = new { amount = 12 }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
        var id = started.GetProperty("id").GetGuid();
        var steps = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/operations/steps?definitionVersionId={version:D}");
        Assert.AreEqual(1L, steps.GetProperty("total").GetInt64()); Assert.AreEqual("approve", steps.GetProperty("items")[0].GetProperty("nodeKey").GetString());
        Assert.IsNotNull(steps.GetProperty("items")[0].GetProperty("dueAtUtc").GetString());
        var hidden = await outsider.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/operations/steps?definitionVersionId={version:D}"); Assert.AreEqual(0L, hidden.GetProperty("total").GetInt64());
        var queued = await R5AssetAsync(client, true); var queuedVersion = (await PostAsync(client, $"/api/v1/workflow/definitions/{queued.Definition:D}/publish", new { expectedRevision = 1, formVersionId = queued.Form })).GetProperty("id").GetGuid();
        await PostAsync(client, "/api/v1/workflow/instances", new { definitionVersionId = queuedVersion, businessType = "native.workflow", businessId = Guid.CreateVersion7().ToString("N"), initialValues = new { amount = 1 }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
        var backlog = await client.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/operations/backlog?definitionVersionId={queuedVersion:D}");
        Assert.AreEqual(1L, backlog.GetProperty("total").GetInt64()); Assert.IsTrue(backlog.GetProperty("items")[0].GetProperty("isReady").GetBoolean());
        using var stale = await client.PostAsJsonAsync($"/api/v1/workflow/definitions/{asset.Definition:D}/simulate", new { expectedRevision = 1, formVersionId = asset.Form, values = new { amount = 12 } }); Assert.AreEqual(HttpStatusCode.Conflict, stale.StatusCode);
        await PostAsync(client, $"/api/v1/workflow/instances/{id:D}/cancel", new { expectedRevision = started.GetProperty("revision").GetInt64(), reason = "原生仿真关联收尾", idempotencyKey = Guid.CreateVersion7().ToString("N") });
    }

    /// <summary>公开保存/发布协作类型20；真正ELF Worker负责Outbox、Files能力、子树结果及重启收敛。</summary>
    private static async Task NativeCollaborationAsync(HttpClient source, HttpClient target, DbConnection db, Guid starter,
        Guid? targetTenant, NativePublication workerArtifact, DatabaseProvider provider, string connection, string data, bool deep, string scenario, string? desktopUsername = null, string? desktopExecutable = null,
        IReadOnlyDictionary<string, string>? storageSettings = null)
    {
        var previousPending = await NativeExistingTodosAsync(target, history: false);
        var previousHistory = await NativeExistingTodosAsync(target, history: true);
        var sourceAsset = await AssetAsync(source, files: true);
        var targetAsset = await ChildAssetAsync(target, files: true, depth: deep ? 2 : 1, parallel: !deep);
        var original = await UploadViaWorkflowAsync(source, $"/api/v1/workflow/definition-versions/{sourceAsset.Version:D}/attachment-uploads?fieldKey=inputFiles", "source-owned.png", FileBytes);
        var proposal = await PostAsync(source, "/api/v1/workflow/collaboration-grants", new { targetTenantId = targetTenant, targetScopeKey = targetTenant is null ? "host" : "tenant", idempotencyKey = Guid.CreateVersion7().ToString("N"),
            offer = new { schemaVersion = 1, sourceDefinitionId = sourceAsset.Definition, sourceFormVersionId = sourceAsset.Form, targetWorkflowKey = targetAsset.Key,
                inputFieldKeys = new[] { "title", "inputFiles", "rows" }, outputFieldKeys = new[] { "answer", "returnedFiles", "returnedRows" }, maximumTargetActivations = 256, expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1) } });
        var grant = proposal.GetProperty("id").GetGuid();
        var accepted = await PostAsync(target, $"/api/v1/workflow/collaboration-grants/{grant:D}/accept", new { expectedRevision = 1, idempotencyKey = "native-accept", acceptance = new {
            schemaVersion = 1, targetDefinitionVersionId = targetAsset.Version, starterUserId = starter, maximumActivities = 256, allowedUserIds = new[] { starter },
            inputFields = new Dictionary<string, string> { ["title"] = "title", ["inputFiles"] = "inputFiles", ["rows"] = "rows" },
            outputFields = new Dictionary<string, string> { ["title"] = "answer", ["returnedFiles"] = "returnedFiles", ["rows"] = "returnedRows" } } });
        var definition = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/definitions/{sourceAsset.Definition:D}");
        var revision = definition.GetProperty("draftRevision").GetInt64();
        using (var updated = await source.PutAsJsonAsync($"/api/v1/workflow/definitions/{sourceAsset.Definition:D}/draft", new { expectedRevision = revision, draft = new {
            schemaVersion = 1, workflowKindKey = "automation", nodes = new object[] {
                new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "collaborate" } } },
                new { nodeKey = "collaborate", nodeTypeKey = "process.collaboration", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" }, collaborationPolicy = new {
                    schemaVersion = 1, grantId = grant, grantRevision = 2, policyHash = accepted.GetProperty("policyHash").GetString() } } },
                new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { } } } } }))
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode, await updated.Content.ReadAsStringAsync());
        var version = (await PostAsync(source, $"/api/v1/workflow/definitions/{sourceAsset.Definition:D}/publish", new { expectedRevision = revision + 1, formVersionId = sourceAsset.Form })).GetProperty("id").GetGuid();
        var started = await PostAsync(source, "/api/v1/workflow/instances", new { definitionVersionId = version, businessType = "native.collaboration", businessId = Guid.CreateVersion7().ToString("N"),
            initialValues = new { title = "shared", secret = "source-private", inputFiles = new[] { original }, rows = new[] { new { label = "source row", amount = 3 } } }, idempotencyKey = Guid.CreateVersion7().ToString("N") }, HttpStatusCode.Created);
        var instance = started.GetProperty("id").GetGuid();
        var call = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_collaboration_call WHERE InstanceId=@instance", new { instance });
        Guid targetRoot; Guid[] leaves;
        using var firstOutput = Stream.Synchronized(File.Create(Path.Combine(data, "worker-first-" + call.ToString("N") + "-pump.log")));
        await using (var first = NativeHost(workerArtifact, provider, connection, data, true, storageSettings, firstOutput))
        {
            await StartNativeAsync(first, Path.Combine(data, "worker-first-startup.log"));
            try
            {
                await NativeUntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call }) == "accepted", "Native Worker未接纳真实协作请求。");
                targetRoot = await db.ExecuteScalarAsync<Guid>("SELECT TargetInstanceId FROM fn_workflow_collaboration_admission WHERE Id=@call", new { call });
                await NativeUntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_collaboration_origin WHERE RootInstanceId=@targetRoot", new { targetRoot }) == 3, "Native Worker未建立实际三根子树。");
                await NativeUntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=todo.InstanceId WHERE origin.RootInstanceId=@targetRoot AND todo.StatusKey='active'", new { targetRoot }) == (deep ? 1 : 2), "Native子代缺少实际人工叶子。");
                leaves = (await db.QueryAsync<Guid>("SELECT todo.InstanceId FROM fn_workflow_todo AS todo INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=todo.InstanceId WHERE origin.RootInstanceId=@targetRoot AND todo.StatusKey='active' ORDER BY todo.InstanceId", new { targetRoot })).ToArray();
                Assert.AreEqual(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_files_read_capability WHERE CallId=@call AND DirectionKey='input'", new { call }));
            }
            finally { await StopNativeAsync(first, Path.Combine(data, "worker-first-" + call.ToString("N") + ".log")); }
        }
        var uploaded = new List<Guid>();
        var runtimes = new Dictionary<Guid, JsonElement>();
        await NativeTodoPagesAsync(target, leaves, baseline: previousPending);
        foreach (var leaf in leaves)
        {
            var todoId = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@leaf AND StatusKey='active'", new { leaf });
            var todo = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/{todoId:D}/runtime");
            var pending = await target.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine?page=1&pageSize=100");
            var pendingRows = pending.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("id").GetGuid() == todoId).ToArray();
            Assert.HasCount(1, pendingRows, pending.GetRawText());
            Assert.AreEqual(JsonValueKind.Null, pendingRows[0].GetProperty("completedAtUtc").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, pendingRows[0].GetProperty("resultActionKey").ValueKind);
            Assert.AreEqual(todo.GetProperty("revision").GetInt64(), pendingRows[0].GetProperty("revision").GetInt64());
            runtimes[todoId] = todo;
            using var submission = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@leaf", new { leaf }))!);
            var opaque = submission.RootElement.GetProperty("inputFiles")[0].GetGuid(); Assert.AreNotEqual(original, opaque);
            Assert.AreEqual("source-owned.png", todo.GetProperty("attachmentNames").GetProperty(opaque.ToString("D")).GetString());
            await BytesAsync(target, leaf, opaque, FileBytes);
            using (var denied = await target.GetAsync(ContentPath(leaf, original))) Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            if (scenario == "success")
            {
                if (desktopUsername is not null)
                {
                    // 真实桌面程序通过自己的登录、文件选择/保存对话框提交；不能由夹具替它调用审批接口。
                    var fixture = Path.Combine(data, "desktop-fixture.json");
                    var uploadPath = Path.Combine(data, "native-return.png"); var downloadPath = Path.Combine(data, "native-download.png");
                    await File.WriteAllBytesAsync(uploadPath, FileBytes);
                    await File.WriteAllTextAsync(fixture, JsonSerializer.Serialize(new { executablePath = desktopExecutable, username = desktopUsername, password = FullNetApiFactory.TestPassword, todoId, uploadPath, downloadPath }));
                    Console.WriteLine("WORKFLOW_DESKTOP_READY " + fixture);
                    await NativeUntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_todo WHERE Id=@todoId", new { todoId }) == "completed", "真实Windows客户端未在窗口内提交待办。", 900);
                    CollectionAssert.AreEqual(FileBytes, await File.ReadAllBytesAsync(downloadPath), "真实保存对话框输出的附件字节不同。");
                    using var desktopResult = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@leaf", new { leaf }))!);
                    uploaded.AddRange(desktopResult.RootElement.GetProperty("returnedFiles").EnumerateArray().Select(item => item.GetGuid()).Where(id => id != opaque));
                    continue;
                }
                var reply = await UploadViaWorkflowAsync(target, $"/api/v1/workflow/todos/{todoId:D}/attachment-uploads?expectedRevision={todo.GetProperty("revision").GetInt64()}&fieldKey=returnedFiles", "native-return.png", FileBytes); uploaded.Add(reply);
                // append逐项保留，首叶回传原输入，其余叶只追加自己的文件；不构造重复或超出maxCount的结果。
                Guid[] returnedFiles = leaf == leaves[0] ? [opaque, reply] : [reply];
                await PostAsync(target, $"/api/v1/workflow/todos/{todoId:D}/approve", new { expectedRevision = todo.GetProperty("revision").GetInt64(), expectedFormRevision = todo.GetProperty("submissionRevision").GetInt64(),
                    fieldPatch = new { title = "native result", returnedFiles, rows = new[] { new { label = "native row", amount = 8 } } }, idempotencyKey = Guid.CreateVersion7().ToString("N") });
            }
        }
        if (scenario == "cancel")
        {
            var current = await source.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/instances/{instance:D}");
            await PostAsync(source, $"/api/v1/workflow/instances/{instance:D}/cancel", new { expectedRevision = current.GetProperty("revision").GetInt64(), reason = "原生停机期间取消", idempotencyKey = Guid.CreateVersion7().ToString("N") });
            await NativeTodoPagesAsync(target, [], baseline: previousPending);
        }
        if (scenario == "revoke")
        {
            await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, reason = "原生停机期间撤权", idempotencyKey = Guid.CreateVersion7().ToString("N") });
            var hiddenPending = await target.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine?page=1&pageSize=100");
            Assert.IsFalse(hiddenPending.GetProperty("items").EnumerateArray().Any(item => leaves.Contains(item.GetProperty("instanceId").GetGuid())), hiddenPending.GetRawText());
            await NativeTodoPagesAsync(target, [], baseline: previousPending);
            var todo = await db.ExecuteScalarAsync<Guid>("SELECT Id FROM fn_workflow_todo WHERE InstanceId=@leaf AND StatusKey='active'", new { leaf = leaves[0] });
            using var denied = await target.GetAsync($"/api/v1/workflow/todos/{todo:D}/runtime"); Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            using var submission = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@leaf", new { leaf = leaves[0] }))!);
            using var deniedFile = await target.GetAsync(ContentPath(leaves[0], submission.RootElement.GetProperty("inputFiles")[0].GetGuid()));
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedFile.StatusCode);
            var prior = runtimes[todo];
            using var deniedApproval = await target.PostAsJsonAsync($"/api/v1/workflow/todos/{todo:D}/approve", new {
                expectedRevision = prior.GetProperty("revision").GetInt64(), expectedFormRevision = prior.GetProperty("submissionRevision").GetInt64(), fieldPatch = new { }, idempotencyKey = Guid.CreateVersion7().ToString("N") });
            Assert.AreEqual(HttpStatusCode.Forbidden, deniedApproval.StatusCode, await deniedApproval.Content.ReadAsStringAsync());
        }
        using var resumedOutput = Stream.Synchronized(File.Create(Path.Combine(data, "worker-resumed-" + call.ToString("N") + "-pump.log")));
        await using (var resumed = NativeHost(workerArtifact, provider, connection, data, true, storageSettings, resumedOutput))
        {
            await StartNativeAsync(resumed, Path.Combine(data, "worker-resumed-startup.log"));
            try
            {
                var expected = scenario == "success" ? "completed" : scenario == "cancel" ? "cancelled" : "denied";
                // 停机可留下尚未过期的耐久租约，等待须覆盖真实租约；不缩短生产租约或跳过终态断言。
                var restartWaitSeconds = new WorkflowRecoveryWorkerOptions().LeaseSeconds + 75;
                var restartAt = DateTimeOffset.UtcNow;
                var diagnosed = false;
                await NativeUntilAsync(async () =>
                {
                    var state = await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", new { call });
                    if (scenario == "success" && state != expected && await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@targetRoot", new { targetRoot }) == "suspended")
                    {
                        await NativeRestartStateAsync(db, provider, connection, instance, targetRoot, call);
                        Assert.Fail("成功场景的目标根已挂起，不能继续等待掩盖归约或执行失败。");
                    }
                    if (state != expected && !diagnosed && DateTimeOffset.UtcNow - restartAt >= TimeSpan.FromSeconds(75))
                    {
                        diagnosed = true;
                        await NativeRestartStateAsync(db, provider, connection, instance, targetRoot, call);
                    }
                    return state == expected;
                }, "原生重启未按真实结果收敛调用。", restartWaitSeconds);
                await NativeUntilAsync(async () => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_instance AS instance INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=instance.Id WHERE origin.RootInstanceId=@targetRoot AND instance.StatusKey=@status", new { targetRoot, status = scenario == "success" ? "completed" : "cancelled" }) == 3, "原生子树仍存在活动后代。");
                // 调用收件与源根后继续办分属耐久推进边界，不能在收件刚提交时提前停止Worker。
                var sourceStatus = scenario == "revoke" ? "suspended" : expected;
                await NativeUntilAsync(async () => await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }) == sourceStatus, "原生源根未完成实际后继续办：" + sourceStatus);
            }
            catch
            {
                await NativeRestartStateAsync(db, provider, connection, instance, targetRoot, call);
                throw;
            }
            finally { await StopNativeAsync(resumed, Path.Combine(data, "worker-resumed-" + call.ToString("N") + ".log")); }
        }
        using var result = JsonDocument.Parse((await db.ExecuteScalarAsync<string>("SELECT SubmissionJson FROM fn_workflow_form_submission WHERE InstanceId=@instance", new { instance }))!);
        Assert.AreEqual("source-private", result.RootElement.GetProperty("secret").GetString());
        if (scenario == "success")
        {
            var history = await target.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine/history?page=1&pageSize=100");
            Assert.IsTrue(history.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("instanceId").GetGuid() == leaves[0]
                && item.GetProperty("completedAtUtc").ValueKind == JsonValueKind.String && item.GetProperty("resultActionKey").GetString() == "approve"), history.GetRawText());
            await NativeTodoPagesAsync(target, leaves, history: true, baseline: previousHistory);
            Assert.AreEqual("native result", result.RootElement.GetProperty("answer").GetString());
            var returned = result.RootElement.GetProperty("returnedFiles").EnumerateArray().Select(item => item.GetGuid()).ToArray(); Assert.HasCount(leaves.Length + 1, returned);
            Assert.IsTrue(returned.Contains(original)); Assert.IsFalse(returned.Any(uploaded.Contains));
            foreach (var file in returned) await BytesAsync(source, instance, file, FileBytes);
            var returnedRows = result.RootElement.GetProperty("returnedRows").EnumerateArray().ToArray(); Assert.HasCount(leaves.Length, returnedRows);
            foreach (var row in returnedRows) { Assert.AreEqual("native row", row.GetProperty("label").GetString()); Assert.AreEqual(8, row.GetProperty("amount").GetInt32()); }
            // 已收回的标量结果不倒写；反向文件能力仍实时撤权，源自有文件保留合法读取。
            await PostAsync(source, $"/api/v1/workflow/collaboration-grants/{grant:D}/revoke", new { expectedRevision = 2, reason = "原生结果能力收紧", idempotencyKey = Guid.CreateVersion7().ToString("N") });
            var hiddenHistory = await target.GetFromJsonAsync<JsonElement>("/api/v1/workflow/todos/mine/history?page=1&pageSize=100");
            Assert.IsFalse(hiddenHistory.GetProperty("items").EnumerateArray().Any(item => leaves.Contains(item.GetProperty("instanceId").GetGuid())), hiddenHistory.GetRawText());
            await NativeTodoPagesAsync(target, [], history: true, baseline: previousHistory);
            foreach (var id in returned.Where(id => id != original))
            {
                using var denied = await source.GetAsync(ContentPath(instance, id));
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            await BytesAsync(source, instance, original, FileBytes);
            Assert.AreEqual("completed", await db.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_instance WHERE Id=@instance", new { instance }));
        }
    }
    private static async Task<Guid[]> NativeExistingTodosAsync(HttpClient target, bool history)
    {
        // 关联验收保留先前场景的合法待办；记录真实API基线，后续仍逐页精确核对COUNT和全部身份。
        var instances = new List<Guid>();
        for (var page = 1; page <= 11; page++)
        {
            var response = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/mine{(history ? "/history" : "")}?page={page}&pageSize=100");
            Assert.IsTrue(response.GetProperty("total").GetInt64() <= 1000, "本用例只使用有界小样本，不作为容量验收。");
            var items = response.GetProperty("items").EnumerateArray().ToArray();
            instances.AddRange(items.Select(item => item.GetProperty("instanceId").GetGuid()));
            if (items.Length == 0) { Assert.AreEqual(response.GetProperty("total").GetInt64(), (long)instances.Count); return instances.ToArray(); }
        }
        throw new InvalidOperationException("Native待办基线超出本用例边界。");
    }

    private static async Task NativeTodoPagesAsync(HttpClient target, Guid[] instances, bool history = false, Guid[]? baseline = null)
    {
        instances = (baseline ?? []).Concat(instances).ToArray();
        // COUNT与实际分页必须共享实时授权；空末页验证偏移，撤权不能留下可见计数。
        var observed = new List<Guid>();
        for (var page = 1; page <= instances.Length + 1; page++)
        {
            var response = await target.GetFromJsonAsync<JsonElement>($"/api/v1/workflow/todos/mine{(history ? "/history" : "")}?page={page}&pageSize=1");
            Assert.AreEqual((long)instances.Length, response.GetProperty("total").GetInt64(), response.GetRawText());
            var items = response.GetProperty("items").EnumerateArray().ToArray();
            Assert.HasCount(page <= instances.Length ? 1 : 0, items, response.GetRawText());
            observed.AddRange(items.Select(item => item.GetProperty("instanceId").GetGuid()));
        }
        CollectionAssert.AreEquivalent(instances, observed.ToArray());
    }

    /// <summary>独立连接仅记录本例状态与活租约数量，不输出表单、凭据或业务正文。</summary>
    private static async Task NativeRestartStateAsync(DbConnection original, DatabaseProvider provider, string connection,
        Guid instance, Guid targetRoot, Guid call)
    {
        try
        {
            await using DbConnection diagnostic = provider == DatabaseProvider.SqlServer
                ? new SqlConnection(new SqlConnectionStringBuilder(connection) { DataSource = original.DataSource }.ConnectionString)
                : new MySqlConnection(Migration114To126RecoveryTestSupport.MySqlQueryConnectionString(
                    new MySqlConnectionStringBuilder(connection) { Server = original.DataSource }.ConnectionString));
            await diagnostic.OpenAsync();
            var values = new { instance, targetRoot, call, now = DateTimeOffset.UtcNow };
            var callState = await diagnostic.ExecuteScalarAsync<string>("SELECT StatusKey FROM fn_workflow_collaboration_call WHERE Id=@call", values);
            var states = await diagnostic.QueryAsync<string>("SELECT target.StatusKey FROM fn_workflow_instance AS target INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=target.Id WHERE origin.RootInstanceId=@targetRoot", values);
            var work = await diagnostic.QueryAsync<string>("SELECT CONCAT(work.NodeKey,':',work.StatusKey) FROM fn_workflow_activity_work AS work INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=work.InstanceId WHERE origin.RootInstanceId=@targetRoot", values);
            var liveActivityLeases = await diagnostic.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_activity_work AS work INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=work.InstanceId WHERE origin.RootInstanceId=@targetRoot AND work.LeaseExpiresAtUtc>@now", values);
            var liveRecoveryLeases = await diagnostic.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fn_workflow_recovery_task WHERE (InstanceId=@instance OR InstanceId IN (SELECT Id FROM fn_workflow_collaboration_origin WHERE RootInstanceId=@targetRoot)) AND LeaseExpiresAtUtc>@now", values);
            var subprocess = await diagnostic.QueryAsync<string>("SELECT CONCAT(callState.StatusKey,':',COALESCE(callState.ErrorCode,'')) FROM fn_workflow_subprocess_call AS callState INNER JOIN fn_workflow_collaboration_origin AS origin ON origin.Id=callState.InstanceId WHERE origin.RootInstanceId=@targetRoot", values);
            Console.WriteLine($"Native restart call={callState}; target states={string.Join(',', states)}; work={string.Join(',', work)}; live activity leases={liveActivityLeases}; live recovery leases={liveRecoveryLeases}; subprocess={string.Join(',', subprocess)}");
        }
        catch (Exception error) { Console.WriteLine("Native restart diagnostic unavailable: " + error.GetType().Name); }
    }

    private static async Task NativeUntilAsync(Func<Task<bool>> condition, string message, int seconds = 75)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(seconds);
        while (!await condition()) { if (DateTimeOffset.UtcNow >= deadline) Assert.Fail(message); await Task.Delay(200); }
    }

    private static async Task StartNativeAsync(IContainer host, string logPath)
    {
        try { await host.StartAsync(); }
        catch
        {
            var logs = await host.GetLogsAsync(); await File.WriteAllTextAsync(logPath, logs.Stdout + logs.Stderr);
            Console.WriteLine("Workflow Native启动失败日志：" + logPath);
            throw;
        }
    }
    private static async Task StopNativeAsync(IContainer host, string logPath)
    {
        try { await host.StopAsync(); }
        finally { var logs = await host.GetLogsAsync(); await File.WriteAllTextAsync(logPath, logs.Stdout + logs.Stderr); }
        Assert.AreEqual(0L, await host.GetExitCodeAsync(), "原生宿主必须正常退出；不能把强制终止计为停止验收。日志：" + logPath);
    }

    private sealed record NativePublication(string Directory, string Image, string Digest, string Executable);
    private static async Task<string> NativeWindowsArtifactAsync(string root)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(root)!, "flutter-windows-release.json")));
        var item = manifest.RootElement; Assert.AreEqual("windows-x64", item.GetProperty("platform").GetString()); Assert.AreEqual("Release", item.GetProperty("configuration").GetString());
        Assert.AreEqual("http://localhost:25387", item.GetProperty("apiBaseUrl").GetString());
        var repository = FullNetApiFactory.FindRepositoryRoot(); var bundle = item.GetProperty("bundle").GetString()!;
        foreach (var source in item.GetProperty("source").EnumerateArray())
            Assert.AreEqual(source.GetProperty("sha256").GetString(), await NativeDigestAsync(Path.Combine(repository, source.GetProperty("file").GetString()!)), "Windows发布后源输入变化。");
        foreach (var binary in item.GetProperty("binaries").EnumerateArray())
            Assert.AreEqual(binary.GetProperty("sha256").GetString(), await NativeDigestAsync(Path.Combine(bundle, binary.GetProperty("file").GetString()!)), "Windows发布包变化。");
        var executable = Path.Combine(bundle, "fullnet_client.exe");
        await using var stream = File.OpenRead(executable); var magic = new byte[2]; await stream.ReadExactlyAsync(magic);
        CollectionAssert.AreEqual(new byte[] { 77, 90 }, magic, "Windows发布产物必须为PE可执行程序。");
        return executable;
    }
    private static async Task<NativePublication> NativePublicationAsync(string root, string latest, string executable)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, latest))); var item = manifest.RootElement;
        Assert.AreEqual(2, item.GetProperty("schemaVersion").GetInt32()); Assert.AreEqual("linux-x64", item.GetProperty("runtimeIdentifier").GetString());
        Assert.AreEqual(Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_NATIVE_HEAD"), item.GetProperty("head").GetString(), "原生产物必须对应本次控制器冻结的HEAD。");
        foreach (var key in new[] { "managedInputsVerified", "nativeInputsUnchanged", "managedArtifactsUnchanged", "headUnchanged", "toolingInputsUnchanged" }) Assert.IsTrue(item.GetProperty(key).GetBoolean(), key);
        var directory = item.GetProperty("directory").GetString()!; var digest = item.GetProperty("sha256").GetString()!;
        Assert.AreEqual(digest, await NativeDigestAsync(Path.Combine(directory, "publish", executable)));
        // 实际Workflow源码不能借用其它任务的宽松范围；发布后若改过任何本模块输入须重新发布。
        foreach (var changed in item.GetProperty("sourceChangesDuringNative").EnumerateArray())
            Assert.IsFalse(changed.GetString()!.Contains("Full.NET.Modules.Workflow", StringComparison.Ordinal), "Workflow源码在原生链接阶段发生变化。");
        var attempt = Directory.GetDirectories(directory, "attempt-*").Single(path => File.Exists(Path.Combine(path, "manifest.json")) && File.ReadAllText(Path.Combine(path, "manifest.json")) == manifest.RootElement.GetRawText());
        using var source = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(attempt, "source-input.json")));
        var recorded = source.RootElement.EnumerateArray().Where(entry => NativeWorkflowInput(entry.GetProperty("file").GetString()!))
            .ToDictionary(entry => entry.GetProperty("file").GetString()!, entry => entry.GetProperty("sha256").GetString()!);
        var repository = FullNetApiFactory.FindRepositoryRoot();
        // 必须证明发布进程实际冻结过所加载的工具，不能把结束时的新文件摘要贴给旧实现。
        var toolingPath = Path.Combine(attempt, "tooling-input.json");
        using var tooling = JsonDocument.Parse(await File.ReadAllTextAsync(toolingPath));
        // 发布器登记紧凑JSON摘要；落盘缩进仅便于诊断，不能把空白差异误判为工具漂移。
        var toolingDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tooling.RootElement))));
        Assert.AreEqual(item.GetProperty("toolingInputsSha256").GetString(), toolingDigest);
        var tools = tooling.RootElement.EnumerateArray().ToDictionary(entry => entry.GetProperty("file").GetString()!, entry => entry.GetProperty("sha256").GetString()!);
        Assert.IsTrue(new[] { "run-messaging-native-publish.mjs", "api-native-aot-publish-contract.mjs", "api-native-aot-publish-warnings.mjs", "messaging-native-inputs.mjs", "native-aot-sdk-policy.mjs", "test-run-context.mjs", "build-resource-pool.mjs" }.ToHashSet(StringComparer.Ordinal).SetEquals(tools.Keys));
        foreach (var (file, hash) in tools)
            Assert.AreEqual(hash, await NativeDigestAsync(Path.Combine(repository, "scripts", "testing", file)), "发布工具在冻结后变化：" + file);
        Assert.AreEqual(tools["messaging-native-inputs.mjs"], item.GetProperty("inputPolicySha256").GetString());
        foreach (var (file, hash) in recorded)
            Assert.AreEqual(hash, await NativeDigestAsync(Path.Combine(repository, file)), "原生发布后直接运行边界的输入变化：" + file);
        foreach (var project in recorded.Keys.Where(file => file.EndsWith(".csproj", StringComparison.Ordinal)))
            foreach (var file in NativeSourceFiles(Path.GetDirectoryName(Path.Combine(repository, project))!))
                Assert.IsTrue(recorded.ContainsKey(Path.GetRelativePath(repository, file).Replace('\\', '/')), "发布后增加了直接运行边界输入：" + file);
        foreach (var file in NativeSourceFiles(Path.Combine(repository, "contracts")).Concat(Directory.EnumerateFiles(repository)))
        {
            var relative = Path.GetRelativePath(repository, file).Replace('\\', '/');
            if (NativeWorkflowInput(relative)) Assert.IsTrue(recorded.ContainsKey(relative), "发布后增加了契约或构建输入：" + relative);
        }
        return new(directory, item.GetProperty("imageId").GetString()!, digest, executable);
    }
    // 包括通知、日历及共享基础设施的直接运行边界；AI产品增量不借此声明当前源码已验收。
    private static bool NativeWorkflowInput(string file) => file.StartsWith("src/", StringComparison.Ordinal)
        && !file.StartsWith("src/Modules/Full.NET.Modules.Ai", StringComparison.Ordinal) && !file.StartsWith("src/AI/", StringComparison.Ordinal)
        || file.StartsWith("contracts/", StringComparison.Ordinal) && file is not "contracts/architecture/global-sql-statements.json"
            and not "contracts/openapi/client-generation-manifest-v1.json" and not "contracts/openapi/fullnet-client-v1.openapi.json"
            and not "contracts/database/object-comments.json"
        || file.StartsWith("Directory.", StringComparison.Ordinal) || file is "global.json" or "nuget.config";
    private static IEnumerable<string> NativeSourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory)) yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory).Where(path => !new[] { "bin", "obj", ".git", "artifacts", "node_modules" }.Contains(Path.GetFileName(path), StringComparer.Ordinal)))
            foreach (var file in NativeSourceFiles(child)) yield return file;
    }
    private static async Task<string> NativeDigestAsync(string path) { await using var stream = File.OpenRead(path); return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)); }
    private static HttpClient NativeClient(IContainer container)
    {
        var client = new HttpClient(new NativeResponseEvidenceHandler()) { BaseAddress = new($"http://127.0.0.1:{container.GetMappedPublicPort(8080)}"), Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.Host = "localhost"; client.DefaultRequestHeaders.Add("Origin", "http://localhost"); return client;
    }
    private sealed class NativeResponseEvidenceHandler() : DelegatingHandler(new HttpClientHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Native HTTP {request.Method} {request.RequestUri!.PathAndQuery} {(int)response.StatusCode}: {body[..Math.Min(body.Length, 4096)]}");
            }
            return response;
        }
    }
    // 小数据功能验收可显式采用更紧的宿主限额；默认保持1GiB，未知配置直接拒绝。
    private static long NativeHostMemoryBytes() => Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_NATIVE_MEMORY_MIB") switch
    {
        null or "1024" => 1L << 30,
        "512" => 512L << 20,
        _ => throw new InvalidOperationException("Unknown Workflow Native memory profile."),
    };

    private static IContainer NativeHost(NativePublication artifact, DatabaseProvider provider, string connection, string data, bool worker,
        IReadOnlyDictionary<string, string>? storageSettings = null, Stream? output = null)
    {
        if (Environment.GetEnvironmentVariable("FULLNET_TESTCONTAINERS_BOUNDED") != "1")
            throw new InvalidOperationException("Workflow原生验收必须使用独立、有界且已握手的资源会话。");
        var builder = new ContainerBuilder(artifact.Image).WithEntrypoint("/app/" + artifact.Executable).WithCommand(Array.Empty<string>()).WithWorkingDirectory("/app")
            .WithLabel("fullnet.workflow.runtime.role", worker ? "worker" : "api")
            .WithBindMount(Path.Combine(artifact.Directory, "publish"), "/app", AccessMode.ReadOnly).WithBindMount(data, "/data", AccessMode.ReadWrite)
            .WithExtraHost("host.docker.internal", "host-gateway").WithPortBinding(!worker && provider == DatabaseProvider.SqlServer && Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_FLUTTER_NATIVE") == "1" ? 25387 : 0, 8080)
            .WithEnvironment("DOTNET_PROCESSOR_COUNT", "2").WithEnvironment("DOTNET_ENVIRONMENT", "Development").WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("ASPNETCORE_URLS", "http://0.0.0.0:8080").WithEnvironment("ASPNETCORE_CONTENTROOT", "/app")
            .WithEnvironment("Database__Provider", provider.ToString()).WithEnvironment("Database__ConnectionString", connection).WithEnvironment("Database__MySqlGuidStorageMode", "Binary16")
            .WithEnvironment("Identity__AllowDevelopmentEphemeralSigningKey", "true").WithEnvironment("Identity__RequireSecureCookies", "false")
            .WithEnvironment("Identity__SessionLoginPolicy", "AllowMultiple").WithEnvironment("Identity__EnableRemoteSuperAdministratorManagement", "true")
            .WithEnvironment("Identity__AccessTokenMinutes", Environment.GetEnvironmentVariable("FULLNET_WORKFLOW_FLUTTER_NATIVE") == "1" ? "30" : "10")
            .WithEnvironment("Identity__Oidc__SigningPrepublicationSeconds", "0")
            .WithEnvironment("Tenancy__HostDomains__0", "localhost").WithEnvironment("Identity__AllowedOrigins__0", "http://localhost")
            .WithEnvironment("Files__Local__RootPath", "/data/files").WithEnvironment("DataProtection__KeyRingPath", "/data/keys").WithEnvironment("Realtime__Enabled", "false")
            .WithEnvironment("OutboxWorker__PollMilliseconds", "100").WithEnvironment("OutboxWorker__MaximumIdlePollMilliseconds", "1000")
            .WithEnvironment("Workflow__RecoveryWorker__PollMilliseconds", "100").WithEnvironment("Workflow__RecoveryWorker__ScanIntervalSeconds", "1")
            .WithCreateParameterModifier(p =>
            {
                SharedDatabaseFixture.ApplyBoundedOwnership(p.Labels ??= new Dictionary<string, string>());
                var limits = p.HostConfig ??= new(); limits.Memory = NativeHostMemoryBytes(); limits.MemorySwap = limits.Memory; limits.NanoCPUs = 1_000_000_000;
                if (limits.Memory == 512L << 20)
                {
                    // 给原生库、线程栈和网络缓冲留出余量；内存不足仍由真实进程及OOM守卫报告失败。
                    p.Env = (p.Env ?? []).Where(value => !value.StartsWith("DOTNET_GCHeapHardLimit=", StringComparison.Ordinal))
                        .Append("DOTNET_GCHeapHardLimit=0x10000000").ToList();
                }
            })
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8080).ForPath("/health/live")));
        if (output is not null) builder = builder.WithOutputConsumer(Consume.RedirectStdoutAndStderrToStream(output, output));
        foreach (var item in storageSettings ?? new Dictionary<string, string>()) builder = builder.WithEnvironment(item.Key, item.Value);
        return builder.Build();
    }
}

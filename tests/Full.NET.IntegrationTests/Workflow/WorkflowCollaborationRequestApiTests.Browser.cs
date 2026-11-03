using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Dapper;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Features.ManageHostMenus;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Workflow.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Workflow;

public sealed partial class WorkflowCollaborationRequestApiTests
{
    [TestMethod]
    [TestCategory("BrowserUi")]
    public async Task Collaboration_files_real_browser_with_sql_server() => await VerifyFileBrowserAsync(false);

    [TestMethod]
    [TestCategory("BrowserUi")]
    public async Task Collaboration_files_live_revoke_browser_with_sql_server() => await VerifyFileBrowserAsync(true);

    [TestMethod]
    [TestCategory("BrowserUi")]
    public async Task Collaboration_public_node_real_browser_with_sql_server() => await VerifyFileBrowserAsync(false, true);

    [TestMethod]
    [TestCategory("BrowserUi")]
    public async Task Collaboration_files_h5_browser_with_sql_server() => await VerifyFileBrowserAsync(false, mobile: true);

    [TestMethod]
    [TestCategory("BrowserUi")]
    public async Task Collaboration_files_h5_live_revoke_browser_with_sql_server() => await VerifyFileBrowserAsync(true, mobile: true);

    private static async Task VerifyFileBrowserAsync(bool revokeVisible, bool publicSourceBrowser = false, bool mobile = false)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var origin = $"http://localhost:{port}"; var fault = new InsertFault();
        var connection = await SharedDatabaseFixture.CreateSqlServerDatabaseAsync();
        using var factory = new FullNetApiFactory(DatabaseProvider.SqlServer, connection,
            new Dictionary<string, string?> { ["Identity:AllowedOrigins:0"] = origin, ["Identity:AllowedOrigins:1"] = "http://localhost",
                ["Tenancy:HostDomains:1"] = "127.0.0.1", ["Identity:RequireSecureCookies"] = "false" }, configureTestServices: services =>
            {
                var executor = services.Last(item => item.ServiceType == typeof(ICommandExecutor)).ImplementationFactory!;
                services.Replace(ServiceDescriptor.Scoped<ICommandExecutor>(scope => new FaultExecutor((ICommandExecutor)executor(scope), fault)));
            });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await factory.InitializeAsync(useSchemaTemplate: false);
        string[] permissions = ["identity.navigation.read", "platform.dashboard.read", "tenancy.tenants.read", "tenancy.tenants.switch",
            WorkflowPermissions.FormsCreate, WorkflowPermissions.FormsPublish, WorkflowPermissions.DefinitionsCreate, WorkflowPermissions.DefinitionsPublish,
            WorkflowPermissions.InstancesStart, WorkflowPermissions.InstancesRead, WorkflowPermissions.InstancesList, WorkflowPermissions.FormAttachmentsUpload,
            WorkflowPermissions.TodosRead, WorkflowPermissions.TodosApprove, WorkflowPermissions.RecoveryTasksRead,
            WorkflowPermissions.CollaborationGrantsRead, WorkflowPermissions.CollaborationGrantsPropose, WorkflowPermissions.CollaborationGrantsAccept, WorkflowPermissions.CollaborationGrantsRevoke];
        var sourceName = "files.browser.source." + Guid.CreateVersion7().ToString("N");
        var sourcePermissions = publicSourceBrowser ? permissions.Concat([WorkflowPermissions.FormsRead, WorkflowPermissions.DefinitionsRead, WorkflowPermissions.DefinitionsUpdate]).ToArray() : permissions;
        if (mobile) sourcePermissions = sourcePermissions.Concat(["notifications.inbox.read"]).ToArray();
        var source = await factory.CreateHostIdentityAsync(sourceName, sourcePermissions, password: FullNetApiFactory.TestPassword);
        using var host = BrowserSocketClient(factory); host.DefaultRequestHeaders.Authorization = new("Bearer", source.AccessToken);
        using var owner = BrowserSocketClient(factory); owner.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var administrator = await IntegrationTestAuthHelper.LoginAsHostUserAsync(owner, "admin"); owner.DefaultRequestHeaders.Authorization = new("Bearer", administrator);
        var tenant = (await owner.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(value => value.Identifier == "acme");
        owner.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(owner, administrator, tenant.Id));
        var username = "files.browser.member." + Guid.CreateVersion7().ToString("N");
        var identity = await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
        var email = username + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, username);
        var invitation = await PostAsync(owner, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member));
        using var member = BrowserSocketClient(factory); member.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username); member.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await PostAsync(member, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { });
        member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, token, tenant.Id));
        await using DbConnection db = new SqlConnection(connection);
        await VerifyWorkflowDirectoryRepairAsync(factory, db);
        if (mobile)
            await CaseAsync(factory, db, member, host, tenant.Id, null, source.UserId, fault, true, "files",
                browser: new(origin, sourceName, "", Guid.Empty, revokeVisible, true, connection, source.UserId));
        else if (publicSourceBrowser)
            await CaseAsync(factory, db, host, member, null, tenant.Id, identity.UserId, fault, true, "worker_success",
                worker: new(true, true, WorkerTreeShape.Parallel, true, new(origin, sourceName)));
        else
            await CaseAsync(factory, db, host, member, null, tenant.Id, identity.UserId, fault, true, "files",
                browser: new(origin, username, tenant.Name, tenant.Id, revokeVisible));
    }

    private static async Task VerifyWorkflowDirectoryRepairAsync(FullNetApiFactory factory, DbConnection db)
    {
        const string route = "module-workflow";
        await db.ExecuteAsync("UPDATE fn_identity_navigation SET RequiredPermission=@permission WHERE RouteName=@route AND ScopeKey='host'",
            new { route, permission = WorkflowPermissions.DefinitionsRead });
        var version = await db.ExecuteScalarAsync<int>("SELECT Version FROM fn_identity_navigation WHERE RouteName=@route AND ScopeKey='host'", new { route });
        async Task Sync()
        {
            await using var scope = factory.Services.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>().SetHost();
            await scope.ServiceProvider.GetRequiredService<HostNavigationCatalogSyncService>().SyncMissingCatalogEntriesAsync();
        }
        await Sync(); await Sync();
        Assert.AreEqual("identity.navigation.read", await db.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE RouteName=@route AND ScopeKey='host'", new { route }));
        Assert.AreEqual(version + 1, await db.ExecuteScalarAsync<int>("SELECT Version FROM fn_identity_navigation WHERE RouteName=@route AND ScopeKey='host'", new { route }));
        // 人工维护的目录不是系统修复对象，不能借同步重写自定义授权。
        await db.ExecuteAsync("UPDATE fn_identity_navigation SET IsSystem=0,RequiredPermission=@permission WHERE RouteName=@route AND ScopeKey='host'", new { route, permission = WorkflowPermissions.DefinitionsRead });
        await Sync();
        Assert.AreEqual(WorkflowPermissions.DefinitionsRead, await db.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE RouteName=@route AND ScopeKey='host'", new { route }));
        await db.ExecuteAsync("UPDATE fn_identity_navigation SET IsSystem=1 WHERE RouteName=@route AND ScopeKey='host'", new { route }); await Sync();
        Assert.AreEqual(WorkflowPermissions.TodosRead, await db.ExecuteScalarAsync<string>("SELECT RequiredPermission FROM fn_identity_navigation WHERE RouteName='workflow-todos' AND ScopeKey='host'"));
    }

    private static HttpClient BrowserSocketClient(FullNetApiFactory factory)
    {
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Host = "localhost"; return client;
    }

    /// <summary>真实成员独立浏览器办理；临时传递内容和非敏感身份，不导出Token或复用其他会话资源。</summary>
    private sealed class BrowserFileFlow(string origin, string username, string tenantName, Guid tenantId, bool revokeVisible = false, bool mobile = false,
        string? fixtureConnection = null, Guid? recipientId = null)
    {
        internal bool RevokeVisible => revokeVisible;
        internal async Task RenewRecipientClientAsync(HttpClient client)
        {
            // SingleSessionPerClient下浏览器登录已撤销准备阶段会话；浏览器退出后通过正常登录重新取得本人的租户会话。
            client.DefaultRequestHeaders.Authorization = null;
            if (!client.DefaultRequestHeaders.Contains("Origin")) client.DefaultRequestHeaders.Add("Origin", "http://localhost");
            var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username);
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            if (tenantId != Guid.Empty) client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, token, tenantId));
        }

        internal Task<Guid> ApproveAsync(FullNetApiFactory factory, Guid todo, Guid capability, Guid original, byte[] targetBytes) =>
            RunAsync(factory, todo, capability, original, targetBytes, null);

        internal async Task VerifyRevocationAsync(FullNetApiFactory factory, Guid todo, Guid capability, Guid original, Func<Task> revoke)
        {
            Assert.AreEqual(Guid.Empty, await RunAsync(factory, todo, capability, original, FileBytes, revoke));
        }

        private async Task<Guid> RunAsync(FullNetApiFactory factory, Guid todo, Guid capability, Guid original, byte[] targetBytes, Func<Task>? revoke)
        {
            var repository = FullNetApiFactory.FindRepositoryRoot(); var temporary = Directory.CreateTempSubdirectory("fullnet-workflow-files-ui-").FullName;
            var passed = false; Process? process = null;
            try
            {
                await File.WriteAllBytesAsync(Path.Combine(temporary, "input.png"), FileBytes);
                await File.WriteAllBytesAsync(Path.Combine(temporary, "target-owned.png"), targetBytes);
                var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository, "tests", "e2e", mobile ? "uniapp-h5" : "admin-real-stack"),
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (var argument in new[] { Path.Combine(start.WorkingDirectory, "node_modules", "@playwright", "test", "cli.js"),
                    "test", "workflow-collaboration-files.spec.mjs", "--project", mobile ? "workflow-files-h5" : "vue-admin", "--workers", "1", "--output", Path.Combine(temporary, "browser") }) start.ArgumentList.Add(argument);
                if (mobile) { start.ArgumentList.Add("--config"); start.ArgumentList.Add("playwright.workflow.config.mjs"); }
                foreach (var key in new[] { "BOOTSTRAP", "VIEWER_PROVISION", "OIDC_FIXTURES" }) start.Environment["FULLNET_E2E_SKIP_" + key] = "1";
                start.Environment["FULLNET_E2E_API_URL"] = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                start.Environment["FULLNET_E2E_ADMIN_ORIGIN"] = origin;
                start.Environment["FULLNET_E2E_H5_ORIGIN"] = origin;
                start.Environment["FULLNET_E2E_PASSWORD"] = FullNetApiFactory.TestPassword;
                start.Environment["FULLNET_E2E_FILES_USERNAME"] = username; start.Environment["FULLNET_E2E_FILES_TENANT"] = tenantName;
                start.Environment["FULLNET_E2E_FILES_TODO"] = todo.ToString("D"); start.Environment["FULLNET_E2E_FILES_CAPABILITY"] = capability.ToString("D");
                start.Environment["FULLNET_E2E_FILES_ORIGINAL"] = original.ToString("D"); start.Environment["FULLNET_E2E_FILES_DIRECTORY"] = temporary;
                start.Environment["FULLNET_E2E_FILES_MODE"] = revoke is null ? "approve" : "revoke";
                if (mobile)
                {
                    Assert.IsNotNull(fixtureConnection); Assert.IsNotNull(recipientId);
                    await using var data = new SqlConnection(fixtureConnection);
                    var instanceId = await data.ExecuteScalarAsync<Guid>("SELECT InstanceId FROM fn_workflow_todo WHERE Id=@todo", new { todo });
                    foreach (var (kind, resourceId, suffix, revoked) in new[] { ("workflow_todo", todo, "TODO", false),
                        ("workflow_instance", instanceId, "INSTANCE", false), ("workflow_todo", todo, "REVOKED", true) })
                    {
                        // 只准备通知读取夹具，实际深链目的资源仍由生产Workflow API重新授权。
                        var messageId = Guid.CreateVersion7();
                        await data.ExecuteAsync("""
                            INSERT INTO fn_notifications_inbox_message
                              (Id,TenantId,RecipientUserId,Title,Content,Status,ReadAtUtc,CreatedAtUtc,CreatedByUserId,ScopeKey,TenantScopeKey,IntentId,DeepLinkJson,RevokedAtUtc)
                            VALUES (@messageId,NULL,@recipientId,'Workflow link','notification-only body','unread',NULL,@Now,NULL,'host','host',NULL,@link,@RevokedAtUtc)
                            """, new { messageId, recipientId, Now = DateTimeOffset.UtcNow,
                                link = JsonSerializer.Serialize(new { resourceTypeKey = kind, resourceId }),
                                RevokedAtUtc = revoked ? DateTimeOffset.UtcNow : (DateTimeOffset?)null });
                        start.Environment["FULLNET_E2E_MESSAGE_" + suffix] = messageId.ToString("D");
                    }
                    start.Environment["FULLNET_E2E_FILES_INSTANCE"] = instanceId.ToString("D");
                }
                start.Environment["FULLNET_E2E_L3_JSON_REPORT"] = Path.Combine(temporary, "browser.json");
                process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
                if (revoke is not null)
                {
                    // 只交换本例就绪/撤销信号；真正撤销仍由持有原会话的C#客户端调用，不能向子进程导出Token。
                    var ready = Path.Combine(temporary, "ready-to-revoke.json");
                    while (!File.Exists(ready) && !process.HasExited) await Task.Delay(100, timeout.Token);
                    if (File.Exists(ready))
                    {
                        await revoke();
                        await File.WriteAllTextAsync(Path.Combine(temporary, "revoked.json"), "{}", timeout.Token);
                    }
                }
                await process.WaitForExitAsync(timeout.Token);
                Assert.AreEqual(0, process.ExitCode, await output + Environment.NewLine + await errors + Environment.NewLine + temporary);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "browser.json")));
                var stats = report.RootElement.GetProperty("stats"); Assert.AreEqual(1, stats.GetProperty("expected").GetInt32());
                Assert.AreEqual(0, stats.GetProperty("unexpected").GetInt32()); Assert.AreEqual(0, stats.GetProperty("skipped").GetInt32());
                using var result = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temporary, "result.json")));
                if (revoke is not null)
                {
                    Assert.IsTrue(result.RootElement.GetProperty("revoked").GetBoolean()); passed = true; return Guid.Empty;
                }
                var file = result.RootElement.GetProperty("fileId").GetGuid(); passed = true; return file;
            }
            finally
            {
                if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process?.Dispose();
                if (passed) Directory.Delete(temporary, recursive: true); else Console.WriteLine("Files真实浏览器证据：" + temporary);
            }
        }
    }
}

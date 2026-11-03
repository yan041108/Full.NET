using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Contracts;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Features.NormalizeCrudSchema;
using Full.NET.Modules.CodeGeneration.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Tests.CodeGeneration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.CodeGeneration;

/// <summary>通过真实回环 HTTP、登录、鉴权、双库及文件工作区验收整改后的完整运行协议。</summary>
[TestClass]
public sealed class CodeGenerationHttpLifecycleTests
{
    private const string Runs = "/api/v1/code-generation/runs";
    private static readonly string[] OperatorPermissions =
    [
        CodeGenerationPreviewPermissions.Read,
        CodeGenerationRunPermissions.Read, CodeGenerationRunPermissions.Execute, CodeGenerationRunPermissions.Apply,
        CodeGenerationRunPermissions.Rollback, CodeGenerationRunPermissions.Download,
        CodeGenerationTemplatePermissions.Read, CodeGenerationTemplatePermissions.Create,
        CodeGenerationTemplatePermissions.Update, CodeGenerationTemplatePermissions.Delete,
    ];

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Kestrel_login_review_apply_replay_cross_entity_rollback_and_saved_download_survive_restart(DatabaseProvider provider)
    {
        using var workspace = CodeGenerationApplyTestWorkspace.Create();
        var connection = await ConnectionAsync(provider);
        var clock = new ArchiveClock();
        var target = await PrepareApplicationAsync(workspace.RootPath);
        var originals = BusinessFiles(workspace.RootPath);
        var username = "cg-http-" + Guid.NewGuid().ToString("N");
        Guid previewId;
        byte[] previewBytes;
        Guid appliedId;
        Guid actorId;
        byte[] appliedBytes;
        using (var factory = Factory(provider, connection, workspace.Settings, clock))
        {
            await factory.InitializeAsync();
            using var client = SocketClient(factory);
            using (var anonymous = await client.GetAsync(Runs)) Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            actorId = (await factory.CreateHostIdentityAsync(username, OperatorPermissions, password: FullNetApiFactory.TestPassword)).UserId;
            var token = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username);
            var schema = Schema("http_product", "HttpProduct", "http-products");
            var template = await JsonAsync<CodeGenerationTemplateResponse>(client, HttpMethod.Post, "/api/v1/code-generation/templates/", token,
                new CreateCodeGenerationTemplateRequest("HTTP lifecycle", null, schema), HttpStatusCode.Created);
            var preview = await JsonAsync<CodeGenerationRunPreviewResponse>(client, HttpMethod.Post, Runs + "/preview", token,
                new CodeGenerationRunPreviewRequest(template.Id, template.Version, null));
            previewId = preview.RunId;
            previewBytes = await DownloadAsync(client, token, previewId);
            AssertZipContains(previewBytes, "backend/HttpProductFeature.g.cs");

            var plan = await ReviewAsync(client, token, previewId, target, workspace.RootPath, schema);
            Assert.IsTrue(plan.Files.Any(file => file.RelativePath == "module/CatalogModule.cs"));
            Assert.IsTrue(plan.Files.Any(file => file.RelativePath == "module/CatalogAuthorizationContributor.cs"));
            Assert.IsTrue(plan.Files.Any(file => file.RelativePath == "ui/views/HttpProductsView.vue"));
            Assert.IsTrue(plan.Files.Any(file => file.RelativePath == "composition/Composition.csproj"));
            AssertFiles(originals, BusinessFiles(workspace.RootPath));
            var before = await JsonAsync<PagedResult<CodeGenerationRunResponse>>(client, HttpMethod.Get, Runs, token);
            var request = new CodeGenerationRunApplyRequest(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7());
            await ProblemAsync(client, HttpMethod.Post, Runs + "/apply", token, request with { PlanSha256 = new string('a', 64) },
                HttpStatusCode.Conflict, CodeGenerationRunErrorCodes.StaleApplyPreview);
            await ProblemAsync(client, HttpMethod.Post, Runs + "/apply", token,
                new { request.PlanId, request.PlanSha256, request.IdempotencyKey, schema }, HttpStatusCode.BadRequest);
            await BindingProblemAsync(client, token, "{\"sensitive-body-marker\":", "application/json", HttpStatusCode.BadRequest);
            await BindingProblemAsync(client, token, "sensitive-body-marker", "text/plain", HttpStatusCode.UnsupportedMediaType);
            var after = await JsonAsync<PagedResult<CodeGenerationRunResponse>>(client, HttpMethod.Get, Runs, token);
            Assert.AreEqual(before.Total, after.Total, "未确认的错误请求不能登记运行。");

            // 审查后人工修改必须拒绝整链写入；恢复测试自己的改动后重新准备计划。
            var entry = Path.Combine(workspace.RootPath, "module/CatalogModule.cs");
            await File.AppendAllTextAsync(entry, "\n// 审查后的人工修改必须保留。\n");
            await ProblemAsync(client, HttpMethod.Post, Runs + "/apply", token, request, HttpStatusCode.Conflict);
            Assert.IsFalse(File.Exists(Path.Combine(workspace.RootPath, GenerationWorkspaceStore.ManifestRelativePath)));
            await File.WriteAllTextAsync(entry, originals["module/CatalogModule.cs"]);
            plan = await JsonAsync<CodeGenerationRunApplyPlanResponse>(client, HttpMethod.Post, Runs + "/apply-plan", token,
                new CodeGenerationRunApplyPlanRequest(previewId, target));
            request = new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7());
            var applied = await JsonAsync<CodeGenerationRunApplyResponse>(client, HttpMethod.Post, Runs + "/apply", token, request);
            appliedId = applied.RunId;
            var replay = await JsonAsync<CodeGenerationRunApplyResponse>(client, HttpMethod.Post, Runs + "/apply", token, request);
            Assert.AreEqual(applied, replay);
            Assert.AreEqual("disabled", applied.PublicationStatus);
            await ProblemAsync(client, HttpMethod.Post, Runs + "/apply", token, request with { IdempotencyKey = Guid.CreateVersion7() }, HttpStatusCode.Conflict);
            appliedBytes = await DownloadAsync(client, token, appliedId);
            AssertZipContains(appliedBytes, "module/CatalogModule.cs");
            AssertZipContains(appliedBytes, "ui/views/HttpProductsView.vue");

            // 同一共享入口加入第二个实体，旧实体保留；回滚必须遵守实际工作区顺序。
            var second = await JsonAsync<CodeGenerationRunPreviewResponse>(client, HttpMethod.Post, Runs + "/preview", token,
                new CodeGenerationRunPreviewRequest(null, null, Schema("http_label", "HttpLabel", "http-labels")));
            var secondTarget = target with { ClientRoute = new("/catalog/http-labels", "catalog-http-labels", "ui/views/HttpLabelsView.vue") };
            var secondPlan = await JsonAsync<CodeGenerationRunApplyPlanResponse>(client, HttpMethod.Post, Runs + "/apply-plan", token,
                new CodeGenerationRunApplyPlanRequest(second.RunId, secondTarget));
            var secondApply = await JsonAsync<CodeGenerationRunApplyResponse>(client, HttpMethod.Post, Runs + "/apply", token,
                new CodeGenerationRunApplyRequest(secondPlan.PlanId, secondPlan.PlanSha256, Guid.CreateVersion7()));
            Assert.IsTrue(File.Exists(Path.Combine(workspace.RootPath, "module/Generated/HttpProduct/HttpProductFeature.g.cs")));
            await ProblemAsync(client, HttpMethod.Post, Runs + "/rollback", token, new CodeGenerationRunRollbackRequest(appliedId), HttpStatusCode.Conflict);
            var chain = await JsonAsync<CodeGenerationRunRollbackChainResponse>(client, HttpMethod.Post, Runs + "/rollback-chain", token,
                new CodeGenerationRunRollbackChainRequest([secondApply.RunId, appliedId]));
            Assert.HasCount(2, chain.Rollbacks);
            AssertFiles(originals, BusinessFiles(workspace.RootPath));

            var updated = await JsonAsync<CodeGenerationTemplateResponse>(client, HttpMethod.Put, $"/api/v1/code-generation/templates/{template.Id:D}", token,
                new UpdateCodeGenerationTemplateRequest("Changed after archive", null, schema with { Columns = schema.Columns.Append(new("Notes", "Notes", "notes", "String", true, 64, null, null)).ToArray() }, template.Version));
            using (var deleted = await SendAsync(client, HttpMethod.Post, $"/api/v1/code-generation/templates/{template.Id:D}/delete", token,
                new DeleteCodeGenerationTemplateRequest(updated.Version))) Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
            CollectionAssert.AreEqual(previewBytes, await DownloadAsync(client, token, previewId));
            CollectionAssert.AreEqual(appliedBytes, await DownloadAsync(client, token, appliedId));

            var restricted = await LoginOperatorAsync(factory, client, "cg-reader-" + Guid.NewGuid().ToString("N"), [CodeGenerationRunPermissions.Read]);
            await ProblemAsync(client, HttpMethod.Get, $"{Runs}/{previewId:D}/artifacts.zip", restricted, null, HttpStatusCode.Forbidden);
            await ProblemAsync(client, HttpMethod.Post, Runs + "/workspace/recover", restricted,
                new CodeGenerationWorkspaceRecoveryRequest(appliedId), HttpStatusCode.Forbidden);
            await ProblemAsync(client, HttpMethod.Post, $"{Runs}/{appliedId:D}/publication/retry", restricted, new { }, HttpStatusCode.Forbidden);
            await ProblemAsync(client, HttpMethod.Post, $"{Runs}/{appliedId:D}/publication/retry", token, new { },
                HttpStatusCode.Conflict, CodeGenerationRunErrorCodes.ApplyDisabled);
        }

        // 首个宿主已释放，再从相同真实数据库与独立归档目录启动；Apply 关闭仍可读取历史。
        var settings = workspace.Settings.ToDictionary(pair => pair.Key, pair => pair.Value);
        settings["CodeGeneration:Apply:Enabled"] = "false";
        using var restarted = Factory(provider, connection, settings, clock);
        await restarted.InitializeAsync();
        using var restartedClient = SocketClient(restarted);
        var restartedToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(restartedClient, username);
        CollectionAssert.AreEqual(previewBytes, await DownloadAsync(restartedClient, restartedToken, previewId));
        CollectionAssert.AreEqual(appliedBytes, await DownloadAsync(restartedClient, restartedToken, appliedId));
        var administrator = await IntegrationTestAuthHelper.LoginAsHostUserAsync(restartedClient, "admin");
        var roles = await JsonAsync<PagedResult<HostRoleResponse>>(restartedClient, HttpMethod.Get, "/api/v1/identity/roles?pageSize=100", administrator);
        var role = roles.Items.Single(item => item.Code == $"test-{actorId:N}");
        await JsonAsync<HostRoleResponse>(restartedClient, HttpMethod.Put, $"/api/v1/identity/roles/{role.Id:D}/permissions", administrator,
            new ReplaceHostRolePermissionsRequest(role.PermissionCodes.Where(permission => permission != CodeGenerationRunPermissions.Download).ToArray(), role.Version));
        // 已签发令牌也必须受当前权限约束；重新登录同样不能恢复已撤销的下载权限。
        await ProblemAsync(restartedClient, HttpMethod.Get, $"{Runs}/{previewId:D}/artifacts.zip", restartedToken, null, HttpStatusCode.Unauthorized);
        var revoked = await IntegrationTestAuthHelper.LoginAsHostUserAsync(restartedClient, username);
        await ProblemAsync(restartedClient, HttpMethod.Get, $"{Runs}/{previewId:D}/artifacts.zip", revoked, null, HttpStatusCode.Forbidden);
        CollectionAssert.AreEqual(previewBytes, await DownloadAsync(restartedClient, administrator, previewId));
        clock.AdvanceDays(31);
        await ProblemAsync(restartedClient, HttpMethod.Get, $"{Runs}/{previewId:D}/artifacts.zip", administrator, null,
            HttpStatusCode.Conflict, CodeGenerationRunErrorCodes.ArchiveExpired);
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Kestrel_recovers_the_same_operation_after_database_completion_failure_and_new_host(DatabaseProvider provider)
    {
        using var workspace = CodeGenerationApplyTestWorkspace.Create();
        var connection = await ConnectionAsync(provider);
        var fault = new CompletionFault();
        var clock = new ArchiveClock();
        var username = "cg-recovery-" + Guid.NewGuid().ToString("N");
        Guid operationId;
        CodeGenerationRunApplyRequest applyRequest;
        string manifest;
        using (var factory = Factory(provider, connection, workspace.Settings, clock, fault))
        {
            await factory.InitializeAsync();
            using var client = SocketClient(factory);
            var token = await LoginOperatorAsync(factory, client, username, OperatorPermissions);
            var preview = await JsonAsync<CodeGenerationRunPreviewResponse>(client, HttpMethod.Post, Runs + "/preview", token,
                new CodeGenerationRunPreviewRequest(null, null, Schema("recovery_item", "RecoveryItem", "recovery-items")));
            var plan = await JsonAsync<CodeGenerationRunApplyPlanResponse>(client, HttpMethod.Post, Runs + "/apply-plan", token,
                new CodeGenerationRunApplyPlanRequest(preview.RunId));
            applyRequest = new(plan.PlanId, plan.PlanSha256, Guid.CreateVersion7());
            fault.Armed = true;
            await ProblemAsync(client, HttpMethod.Post, Runs + "/apply", token, applyRequest,
                HttpStatusCode.Conflict, CodeGenerationRunErrorCodes.RecoveryRequired);
            var state = await JsonAsync<CodeGenerationWorkspaceStateResponse>(client, HttpMethod.Get, Runs + "/workspace", token);
            operationId = state.PendingOperationId!.Value;
            Assert.AreEqual(plan.PlanId, operationId);
            var run = await JsonAsync<CodeGenerationRunResponse>(client, HttpMethod.Get, $"{Runs}/{operationId:D}", token);
            Assert.AreEqual("running", run.Status);
            manifest = await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, GenerationWorkspaceStore.ManifestRelativePath));
        }

        using var recoveredHost = Factory(provider, connection, workspace.Settings, clock);
        await recoveredHost.InitializeAsync();
        using var recoveredClient = SocketClient(recoveredHost);
        var recoveredToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(recoveredClient, username);
        var applyOnly = await LoginOperatorAsync(recoveredHost, recoveredClient, "cg-apply-only-" + Guid.NewGuid().ToString("N"), [CodeGenerationRunPermissions.Apply]);
        await ProblemAsync(recoveredClient, HttpMethod.Post, Runs + "/workspace/recover", applyOnly,
            new CodeGenerationWorkspaceRecoveryRequest(operationId), HttpStatusCode.Forbidden);
        var recovered = await JsonAsync<CodeGenerationWorkspaceStateResponse>(recoveredClient, HttpMethod.Post, Runs + "/workspace/recover", recoveredToken,
            new CodeGenerationWorkspaceRecoveryRequest(operationId));
        Assert.IsNull(recovered.PendingOperationId);
        Assert.AreEqual("idle", recovered.Phase);
        var replay = await JsonAsync<CodeGenerationRunApplyResponse>(recoveredClient, HttpMethod.Post, Runs + "/apply", recoveredToken, applyRequest);
        Assert.AreEqual(operationId, replay.RunId);
        Assert.AreEqual(manifest, await File.ReadAllTextAsync(Path.Combine(workspace.RootPath, GenerationWorkspaceStore.ManifestRelativePath)));
        var finished = await JsonAsync<CodeGenerationRunResponse>(recoveredClient, HttpMethod.Get, $"{Runs}/{operationId:D}", recoveredToken);
        Assert.AreEqual("succeeded", finished.Status);
        var all = await JsonAsync<PagedResult<CodeGenerationRunResponse>>(recoveredClient, HttpMethod.Get, Runs + "?pageSize=100", recoveredToken);
        Assert.AreEqual(1, all.Items.Count(row => row.OperationKind == "apply"));
        var bytes = await DownloadAsync(recoveredClient, recoveredToken, operationId);
        AssertZipContains(bytes, GenerationWorkspaceStore.ManifestRelativePath);
        await JsonAsync<CodeGenerationRunRollbackResponse>(recoveredClient, HttpMethod.Post, Runs + "/rollback", recoveredToken,
            new CodeGenerationRunRollbackRequest(operationId));
    }

    private static async Task<string> ConnectionAsync(DatabaseProvider provider) => provider == DatabaseProvider.SqlServer
        ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();

    private static FullNetApiFactory Factory(DatabaseProvider provider, string connection, IReadOnlyDictionary<string, string?> settings,
        ArchiveClock clock, CompletionFault? fault = null)
    {
        var factory = new FullNetApiFactory(provider, connection, settings, configureTestServices: services =>
        {
            // 仅下载包时钟可推进；身份、JWT、数据库查询和业务服务均使用真实实现。
            services.RemoveAll<CodeGenerationArtifactStore>();
            services.AddScoped(provider => new CodeGenerationArtifactStore(provider.GetRequiredService<IQueryExecutor>(),
                provider.GetRequiredService<ICommandExecutor>(), provider.GetRequiredService<IOptions<CodeGenerationArtifactOptions>>(), clock));
            if (fault is not null)
            {
                var execute = services.Last(descriptor => descriptor.ServiceType == typeof(ICommandExecutor)).ImplementationFactory
                    ?? throw new InvalidOperationException("真实执行器缺少预期的显式注册工厂。");
                services.RemoveAll<ICommandExecutor>();
                services.AddScoped<ICommandExecutor>(provider => new FailCompletion((ICommandExecutor)execute(provider), fault));
            }
        });
        factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        return factory;
    }

    private static HttpClient SocketClient(FullNetApiFactory factory)
    {
        var addresses = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var address = new Uri(addresses.Single());
        Assert.IsTrue(IPAddress.IsLoopback(IPAddress.Parse(address.Host)) && address.Port != 0);
        var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = address };
        client.DefaultRequestHeaders.Host = "localhost";
        return client;
    }

    private static async Task<string> LoginOperatorAsync(FullNetApiFactory factory, HttpClient client, string username, string[] permissions)
    {
        await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
        return await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, username);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        return SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    private static async Task<T> JsonAsync<T>(HttpClient client, HttpMethod method, string path, string token, object? body = null,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(client, method, path, token, body);
        Assert.AreEqual(expected, response.StatusCode, $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<CodeGenerationRunApplyPlanResponse> ReviewAsync(HttpClient client, string token, Guid previewId,
        CodeGenerationIntegrationTargetRequest target, string root, CodeGenerationPreviewRequest schema)
    {
        using var response = await SendAsync(client, HttpMethod.Post, Runs + "/apply-plan", token, new CodeGenerationRunApplyPlanRequest(previewId, target));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            // 公开响应不暴露源码或绝对路径；失败时由测试在自己的工作区复现并输出已脱敏的编译诊断。
            var normalized = new CodeGenerationSchemaNormalizer().Normalize(schema).Value!.Schema;
            var destination = ModuleIntegrationTarget.Create(target.ModuleName, target.ModuleProjectPath, target.ModuleEntryPointPath,
                target.CompositionProjectPath, target.CompositionCatalogPath, target.VueRouterPath, null,
                ModuleClientRouteTarget.Create(target.ClientRoute!.RoutePath, target.ClientRoute.VueRouteName, target.ClientRoute.VueComponentPath),
                target.AuthorizationContributorPath);
            var plan = await ModuleIntegrationFullPlanner.PlanAsync(root, normalized, destination, await CrudGenerationWorkspace.PlanAsync(root, normalized));
            var compilation = await ModuleIntegrationFullCompilation.ValidateAsync(root, normalized, destination, plan);
            Assert.Fail($"HTTP 审查失败：{response.StatusCode}；候选诊断：{string.Join("\n", compilation.Diagnostics)}");
        }
        return (await response.Content.ReadFromJsonAsync<CodeGenerationRunApplyPlanResponse>())!;
    }

    private static async Task ProblemAsync(HttpClient client, HttpMethod method, string path, string token, object? body,
        HttpStatusCode status, string? code = null)
    {
        using var response = await SendAsync(client, method, path, token, body);
        Assert.AreEqual(status, response.StatusCode, $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        if (code is not null)
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.AreEqual(code, problem.RootElement.GetProperty("code").GetString());
        }
    }

    private static async Task BindingProblemAsync(HttpClient client, string token, string body, string mediaType, HttpStatusCode status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Runs + "/apply") { Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);
        Assert.AreEqual(status, response.StatusCode);
        Assert.AreEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual((int)status, problem.RootElement.GetProperty("status").GetInt32());
        Assert.IsFalse(problem.RootElement.GetRawText().Contains("sensitive-body-marker", StringComparison.Ordinal));
    }

    private static async Task<byte[]> DownloadAsync(HttpClient client, string token, Guid id)
    {
        using var response = await SendAsync(client, HttpMethod.Get, $"{Runs}/{id:D}/artifacts.zip", token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static void AssertZipContains(byte[] bytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.IsNotNull(zip.GetEntry(name), name);
    }

    private static Dictionary<string, string> BusinessFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).Replace('\\', '/').StartsWith(".fullnet/", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);

    private static void AssertFiles(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actual.Keys.ToArray());
        foreach (var file in expected) Assert.AreEqual(file.Value, actual[file.Key], file.Key);
    }

    private static CodeGenerationPreviewRequest Schema(string key, string type, string resource) => new("acme", "catalog", key,
        "acme_catalog_" + key, "Acme.Modules.Catalog", type, resource, resource.Replace('-', '_'), "host.only", null,
        [new("Id", "Id", "id", "Uuid", false, null, null, null), new("Name", "Name", "name", "String", false, 64, null, null),
         new("Version", "Version", "version", "Int64", false, null, null, null)], new("hard.delete", false, false, false, true, "none"), "single", []);

    private static async Task<CodeGenerationIntegrationTargetRequest> PrepareApplicationAsync(string root)
    {
        var repository = CodeGenerationRemediationSchemas.RepositoryRoot().Replace('\\', '/');
        async Task Write(string path, string text) { var full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); await File.WriteAllTextAsync(full, text); }
        await Write("Directory.Build.props", $"<Project><Import Project=\"{repository}/Directory.Build.props\" /></Project>");
        await Write("Directory.Packages.props", $"<Project><Import Project=\"{repository}/Directory.Packages.props\" /></Project>");
        await Write("module/Catalog.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
                <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj" />
                <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj" />
                <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj" />
                <ProjectReference Include="{{repository}}/src/Modules/Full.NET.Modules.Identity.Contracts/Full.NET.Modules.Identity.Contracts.csproj" />
              </ItemGroup>
            </Project>
            """);
        await Write("composition/Composition.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="{{repository}}/src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj" />
              </ItemGroup>
            </Project>
            """);
        await Write("module/CatalogModule.cs", """
            using Full.NET.Modularity.Modules;
            using Microsoft.AspNetCore.Routing;
            using Microsoft.Extensions.Configuration;
            using Microsoft.Extensions.DependencyInjection;
            using Full.NET.Modules.Identity.Contracts;
            namespace Acme.Modules.Catalog;
            public sealed class CatalogModule : IFullNetModule
            {
                public string Name => "Catalog";
                public IReadOnlyCollection<string> Dependencies => [];
                public void AddServices(IServiceCollection services, IConfiguration configuration)
                {
                    services.AddOptions();
                    services.AddSingleton<IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();
                }
                public void MapEndpoints(IEndpointRouteBuilder endpoints)
                {
                }
            }
            """);
        await Write("composition/Catalog.cs", """
            using Full.NET.Modularity.Modules;
            namespace Acme.Composition;
            public static class Catalog
            {
                private static IReadOnlyList<IFullNetModule> CreateModules() =>
                [
                ];
            }
            """);
        await Write("module/CatalogAuthorizationContributor.cs", (await File.ReadAllTextAsync(Path.Combine(repository,
            "tests/templates/support/fixtures/application-catalog-authorization-contributor.cs.fixture"))).Replace("Demo.Modules.Catalog", "Acme.Modules.Catalog", StringComparison.Ordinal));
        await Write("ui/router.ts", """
            export function createAppRouter() {
              return createRouter({
                routes: [
                  { path: '/403', component: loadStatusView }
                ]
              });
            }
            """);
        return new("Catalog", "module/Catalog.csproj", "module/CatalogModule.cs", "composition/Composition.csproj", "composition/Catalog.cs", "ui/router.ts",
            ClientRoute: new("/catalog/http-products", "catalog-http-products", "ui/views/HttpProductsView.vue"),
            AuthorizationContributorPath: "module/CatalogAuthorizationContributor.cs");
    }

    private sealed class ArchiveClock : IClock
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public DateTimeOffset UtcNow => _now;
        internal void AdvanceDays(int days) => _now = _now.AddDays(days);
    }

    private sealed class CompletionFault
    {
        internal bool Armed { get; set; }
    }

    // 仅故障点替换一次终态写入；插入、查询、事务、迁移及后续恢复都由真实执行器承担。
    private sealed class FailCompletion(ICommandExecutor inner, CompletionFault fault) : ICommandExecutor
    {
        public Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
        {
            if (fault.Armed && statement == CodeGenerationRunSql.CompleteApply) { fault.Armed = false; throw new IOException("模拟终态写入暂时中断。"); }
            return inner.ExecuteAsync(statement, parameters, cancellationToken);
        }
    }
}

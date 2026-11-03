using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using Full.NET.AI.Abstractions.Models;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Ai;
using Full.NET.IntegrationTests.Api;
using Full.NET.IntegrationTests.Migrations;
using Full.NET.Modules.Ai;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>真实 Native 发布/历史证明与人工完整代次；生产 JIT 执行器使用受控向量模型，答案文字为明确测试数据。</summary>
[TestClass]
[DoNotParallelize]
public sealed class NativeHumanKnowledgePublicationE2ETests
{
    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public Task Native_human_generation_publication_and_v4_history_preserve_complete_provenance(DatabaseProvider provider) => VerifyAsync(provider, true);

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, "csv")]
    [DataRow(DatabaseProvider.MySql, "csv")]
    [DataRow(DatabaseProvider.SqlServer, "png")]
    [DataRow(DatabaseProvider.MySql, "png")]
    public Task Native_structured_source_edit_index_publication_and_history_preserve_real_positions(DatabaseProvider provider, string format) => VerifyAsync(provider, true, format);

    internal static async Task VerifyAsync(DatabaseProvider provider, bool native, string format = "text", bool verifySearch = false, bool searchPolicyOnly = false, bool verifyContext = false, bool verifySearchAnswer = false, bool verifyComparison = false, bool verifyRelations = false, bool verifyRefinement = false, bool verifyFaqDraft = false, bool verifyFaqOrdinaryBoundary = false, bool verifyFaqLifecycle = false, bool verifyFaqSearch = false, bool verifyFaqTransfer = false, bool verifyFaqParents = false, bool verifyFaqFeedback = false, bool verifyEvaluationSources = false, bool verifyEvaluationRuns = false, bool verifyEvaluationPrivateRuns = false, bool verifyPersonalScheduleAnswer = false, bool verifyPersonalScheduleFaq = false, bool verifyPersonalScheduleFeedback = false)
    {
        var artifact = native ? NativeApiArtifactLocator.RequireArtifact() : null;
        var scheduleBridge = native && provider == DatabaseProvider.MySql && verifyPersonalScheduleAnswer && !verifyPersonalScheduleFaq && !verifyPersonalScheduleFeedback
            ? Environment.GetEnvironmentVariable("FULLNET_K06_ANSWER_BROWSER_BRIDGE") : null;
        var businessFaqBridge = native && provider == DatabaseProvider.MySql && verifyPersonalScheduleFaq
            ? Environment.GetEnvironmentVariable("FULLNET_K06_FAQ_BROWSER_BRIDGE") : null;
        var browserBridge = native && provider == DatabaseProvider.MySql ? verifyFaqFeedback ? Environment.GetEnvironmentVariable("FULLNET_K04_FEEDBACK_BROWSER_BRIDGE") : verifyFaqParents ? Environment.GetEnvironmentVariable("FULLNET_K04_PARENTS_BROWSER_BRIDGE") : verifyFaqTransfer ? Environment.GetEnvironmentVariable("FULLNET_K04_TRANSFER_BROWSER_BRIDGE") : verifyFaqSearch ? Environment.GetEnvironmentVariable("FULLNET_K04_SEARCH_BROWSER_BRIDGE") : verifyFaqLifecycle ? Environment.GetEnvironmentVariable("FULLNET_K04_LIFECYCLE_BROWSER_BRIDGE") : verifyFaqDraft ? Environment.GetEnvironmentVariable("FULLNET_K04_DRAFT_BROWSER_BRIDGE") : verifyRefinement ? Environment.GetEnvironmentVariable("FULLNET_K03_REFINE_BROWSER_BRIDGE") : verifyRelations ? Environment.GetEnvironmentVariable("FULLNET_K03_RELATIONS_BROWSER_BRIDGE") : verifyComparison ? Environment.GetEnvironmentVariable("FULLNET_K03_COMPARE_BROWSER_BRIDGE") : verifySearchAnswer ? Environment.GetEnvironmentVariable("FULLNET_K03_ANSWER_BROWSER_BRIDGE") : verifyContext ? Environment.GetEnvironmentVariable("FULLNET_K03_CONTEXT_BROWSER_BRIDGE")
            : verifySearch && !searchPolicyOnly ? Environment.GetEnvironmentVariable("FULLNET_K03_BROWSER_BRIDGE") : null : null;
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        var settings = new Dictionary<string, string?>
        {
            ["DataProtection:KeyRingPath"] = Path.Combine(Path.GetTempPath(), "fullnet-human-native-keys", Guid.CreateVersion7().ToString("N")),
            ["Files:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "fullnet-human-native-files", Guid.CreateVersion7().ToString("N")),
            ["FullNet:Ai:KnowledgeIndexRebuild:Enabled"] = "true"
        };
        if (verifyPersonalScheduleFeedback && Environment.GetEnvironmentVariable("FULLNET_K06_FEEDBACK_DATABASE_REGISTRY") is { Length: > 0 } businessFeedbackRegistry)
        {
            await using var registered = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            await File.AppendAllTextAsync(businessFeedbackRegistry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = registered.Database,
                native, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        }
        if (verifyPersonalScheduleFaq && Environment.GetEnvironmentVariable("FULLNET_K06_FAQ_DATABASE_REGISTRY") is { Length: > 0 } faqRegistry)
        {
            await using var registered = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            await File.AppendAllTextAsync(faqRegistry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = registered.Database,
                native, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        }
        if (scheduleBridge is not null) { settings["Identity:AllowedOrigins:1"] = "http://localhost:34492"; settings["Tenancy:HostDomains:1"] = "127.0.0.1"; }
        if (businessFaqBridge is not null) { settings["Identity:AllowedOrigins:1"] = "http://localhost:34492"; settings["Tenancy:HostDomains:1"] = "127.0.0.1"; }
        if (verifyPersonalScheduleAnswer && Environment.GetEnvironmentVariable("FULLNET_K06_ANSWER_DATABASE_REGISTRY") is { Length: > 0 } scheduleRegistry)
        {
            await using var registered = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            await File.AppendAllTextAsync(scheduleRegistry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = registered.Database,
                native, filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        }
        if(verifyEvaluationRuns)
        {
            settings["Identity:AllowedOrigins:1"]="http://localhost:34492";settings["Tenancy:HostDomains:1"]="127.0.0.1";
            if(Environment.GetEnvironmentVariable("FULLNET_K05_EVALUATION_DATABASE_REGISTRY") is {Length:>0} runRegistry)
            {
                await using var owned=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection);
                await File.AppendAllTextAsync(runRegistry,JsonSerializer.Serialize(new{provider=provider.ToString(),database=owned.Database,native,filesRoot=settings["Files:Local:RootPath"],keysRoot=settings["DataProtection:KeyRingPath"]})+"\n");
            }
        }
        if ((verifyFaqDraft || verifyFaqOrdinaryBoundary || verifyFaqLifecycle || verifyFaqTransfer) && Environment.GetEnvironmentVariable(verifyEvaluationSources ? "FULLNET_K05_EVALUATION_DATABASE_REGISTRY" : verifyFaqFeedback ? "FULLNET_K04_FEEDBACK_DATABASE_REGISTRY" : verifyFaqParents ? "FULLNET_K04_PARENTS_DATABASE_REGISTRY" : verifyFaqTransfer ? "FULLNET_K04_TRANSFER_DATABASE_REGISTRY" : verifyFaqLifecycle ? "FULLNET_K04_LIFECYCLE_DATABASE_REGISTRY" : "FULLNET_K04_DRAFT_DATABASE_REGISTRY") is { Length: > 0 } registry)
        {
            await using var ownedDatabase = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
            await File.AppendAllTextAsync(registry, JsonSerializer.Serialize(new { provider = provider.ToString(), database = ownedDatabase.Database, native,
                filesRoot = settings["Files:Local:RootPath"], keysRoot = settings["DataProtection:KeyRingPath"] }) + "\n");
        }
        if (!string.IsNullOrWhiteSpace(browserBridge))
        {
            Directory.CreateDirectory(browserBridge);
            settings["Identity:AllowedOrigins:1"] = Environment.GetEnvironmentVariable("FULLNET_K03_BROWSER_ORIGIN") ?? "http://localhost:25173";
            settings["Tenancy:HostDomains:1"] = "127.0.0.1";
        }
        var fileName = format == "docx-image" ? "correction-parent-child.docx" : format == "csv" ? "correction-table.csv" : format == "png" ? "correction-ocr.png" : "source.txt";
        var mime = format == "docx-image" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document" : format == "csv" ? "text/csv" : format == "png" ? "image/png" : "text/plain";
        var sourceBytes = Encoding.UTF8.GetBytes("原文件😀识别内容。");
        if (format != "text")
        {
            var image = Environment.GetEnvironmentVariable("FULLNET_K02_PARSER_IMAGE");
            var fixtures = Environment.GetEnvironmentVariable("FULLNET_K02_STRUCTURED_FIXTURES");
            Assert.IsFalse(string.IsNullOrWhiteSpace(image), "结构化来源必须提供实际验收的不可变解析镜像。");
            Assert.IsFalse(string.IsNullOrWhiteSpace(fixtures), "先运行限定的 K02 来源生成器。");
            sourceBytes = await File.ReadAllBytesAsync(Path.Combine(fixtures!, fileName));
            settings["FullNet:Ai:KnowledgeDocuments:Enabled"] = "true";
            settings["FullNet:Ai:KnowledgeDocuments:NodeExecutablePath"] = "/usr/local/bin/node";
            settings["FullNet:Ai:KnowledgeDocuments:CliScriptPath"] = "/repo/scripts/ai/document-sandbox-cli.mjs";
            settings["FullNet:Ai:KnowledgeDocuments:ImageId"] = image;
        }
        var model = new AiKnowledgeIndexEmbeddingAssertions.ControlledFactory();
        var refinementModel = new KnowledgeRefinementModel(model);
        await using var answerModelServer = native && (verifySearchAnswer || verifyComparison || verifyRefinement || verifyFaqSearch || verifyEvaluationRuns) ? await NativeKnowledgeModelServer.StartAsync() : null;
        using var factory = new FullNetApiFactory(provider, connection, settingsOverrides: settings, configureTestServices: services =>
        {
            if(verifyFaqTransfer) { services.RemoveAll<AiDataProcessingPolicy>(); services.AddSingleton(new AiDataProcessingPolicy(new Dictionary<string,IReadOnlyList<string>> { ["export"] = ["public","internal"] })); }
            if(verifyFaqLifecycle)services.Insert(0,new ServiceDescriptor(typeof(Microsoft.AspNetCore.Diagnostics.IExceptionHandler),typeof(FaqLifecycleDiagnostic),ServiceLifetime.Singleton));
            new AiModule().AddBackgroundServices(services, new ConfigurationBuilder().Build());
            if (verifyFaqFeedback || verifyEvaluationSources || verifyPersonalScheduleFaq) FaqLifecycleQueryTrace.Attach(services);
            services.RemoveAll<IAiModelClientFactory>(); services.AddSingleton<IAiModelClientFactory>(verifyRefinement || verifyFaqSearch || verifyEvaluationRuns ? refinementModel : verifySearchAnswer ? new SearchAnswerFactory(model) : model);
            if (searchPolicyOnly)
            {
                // 测试宿主的即时启动层会携带无值父节；在装配处注入同一冻结策略，原生进程仍读取正式配置。
                services.RemoveAll<AiDataProcessingPolicy>();
                services.AddSingleton(new AiDataProcessingPolicy(new Dictionary<string, IReadOnlyList<string>> { ["rerank"] = ["public"] }));
            }
        });
        await factory.InitializeAsync();
        if (verifyPersonalScheduleFaq && !native && Environment.GetEnvironmentVariable("FULLNET_K06_FAQ_OPENAPI_EXPORT_DIR") is { Length: > 0 } faqExport)
        {
            Directory.CreateDirectory(faqExport); using var exporter = factory.CreateClientForHost("localhost");
            using var response = await exporter.GetAsync("/openapi/v1.json"); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            await File.WriteAllTextAsync(Path.Combine(faqExport,"faq-"+provider.ToString().ToLowerInvariant()+".json"),await response.Content.ReadAsStringAsync());
        }
        if(verifyEvaluationRuns&&!native&&Environment.GetEnvironmentVariable("FULLNET_K05_RUN_OPENAPI_EXPORT_DIR") is {Length:>0} runExport)
        {
            Directory.CreateDirectory(runExport);using var exporter=factory.CreateClientForHost("localhost");
            using var response=await exporter.GetAsync("/openapi/v1.json");Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);
            await File.WriteAllTextAsync(Path.Combine(runExport,"run-"+provider.ToString().ToLowerInvariant()+".json"),await response.Content.ReadAsStringAsync());
        }
        var readerName = "human-native-reader-" + Guid.CreateVersion7().ToString("N");
        var reader = await factory.CreateHostIdentityAsync(readerName,
            [AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead,
                AiKnowledgeDocumentPermissions.IndexRead, .. (verifyRelations || verifyRefinement || verifyFaqDraft ? Array.Empty<string>() : new[] { AiChatPermissions.Read, AiChatPermissions.Send }), AiKnowledgePublicationPermissions.Read,
                AiKnowledgePublicationPermissions.Review, "tenancy.tenants.switch", .. (verifySearch || verifyContext || verifySearchAnswer || verifyComparison || verifyRelations || verifyRefinement || verifyFaqDraft || verifyFaqOrdinaryBoundary || verifyFaqLifecycle ? new[] { AiKnowledgeSearchPermissions.Read } : Array.Empty<string>()),
                .. (verifyFaqDraft || verifyFaqLifecycle || verifyPersonalScheduleFaq ? new[] { AiKnowledgeFaqPermissions.Read } : Array.Empty<string>()),
                .. (verifyPersonalScheduleFaq ? new[] { AiKnowledgeFaqPermissions.Review } : Array.Empty<string>()),
                .. (verifyPersonalScheduleFeedback ? new[] { AiKnowledgeAnswerFeedbackPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Submit,
                    AiKnowledgeAnswerFeedbackPermissions.Withdraw, AiKnowledgeAnswerFeedbackReviewPermissions.Read,
                    AiKnowledgeAnswerFeedbackReviewPermissions.Review } : Array.Empty<string>()),
                .. (verifyFaqLifecycle ? new[] { AiKnowledgeFaqPermissions.Review, "identity.navigation.read", "platform.dashboard.read" } : Array.Empty<string>()),
                .. (verifyEvaluationSources ? new[] { AiKnowledgeAnswerFeedbackPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Submit, AiKnowledgeAnswerFeedbackPermissions.Withdraw, AiKnowledgeAnswerFeedbackReviewPermissions.Read } : Array.Empty<string>()),
                .. (verifyEvaluationRuns ? new[] {AiKnowledgeSearchPermissions.Read,AiKnowledgeEvaluationPermissions.Read,AiKnowledgeEvaluationPermissions.Approve,AiKnowledgeEvaluationRunPermissions.Read} : Array.Empty<string>()),
                .. (verifyFaqFeedback ? new[] { AiKnowledgeAnswerFeedbackPermissions.Read, AiKnowledgeAnswerFeedbackPermissions.Submit, AiKnowledgeAnswerFeedbackPermissions.Withdraw, AiKnowledgeAnswerFeedbackReviewPermissions.Read, AiKnowledgeFaqPermissions.Feedback } : Array.Empty<string>()),
                .. (verifySearchAnswer || verifyFaqSearch ? new[] { AiChatPermissions.Create } : Array.Empty<string>()),
                .. (verifyPersonalScheduleAnswer ? new[] { Full.NET.Modules.Calendar.Contracts.CalendarPermissions.Read,
                    Full.NET.Modules.Calendar.Contracts.CalendarPermissions.Create, Full.NET.Modules.Calendar.Contracts.CalendarPermissions.Update,
                    Full.NET.Modules.Calendar.Contracts.CalendarPermissions.Delete, Full.NET.Modules.Calendar.Contracts.PersonalScheduleKnowledgePermissions.Read } : Array.Empty<string>()),
                .. (verifyRefinement || verifyFaqSearch ? new[] { AiKnowledgeSearchPermissions.Compare, AiKnowledgeSearchPermissions.Refine } : verifyComparison ? new[] { AiKnowledgeSearchPermissions.Compare } : Array.Empty<string>())], password: FullNetApiFactory.TestPassword, identityId: verifyFaqLifecycle ? Guid.CreateVersion7() : null);
        var email = readerName + "@example.test"; await factory.EnsureHostUserProfileEmailAsync(reader.UserId, email, "原生人工来源读者");
        var limitedName = "feedback-label-reader-" + Guid.CreateVersion7().ToString("N");
        var runBridge=native&&provider==DatabaseProvider.MySql&&verifyEvaluationRuns?Environment.GetEnvironmentVariable("FULLNET_K05_RUN_BROWSER_BRIDGE"):null;
        var runBrowserUsers=new List<(string Username,Guid UserId)>();
        var scheduleBrowserUsers = new List<(string Username, Guid UserId)>();
        var businessFaqUsers = new List<(string Username, Guid UserId)>();
        if (businessFaqBridge is not null)
            foreach (var suffix in new[] { "desktop", "mobile" })
            {
                var name = "k06-faq-" + suffix + "-" + Guid.CreateVersion7().ToString("N");
                var identity = await factory.CreateHostIdentityAsync(name, PersonalScheduleFaqBrowserFixture.Permissions, password: FullNetApiFactory.TestPassword);
                businessFaqUsers.Add((name, identity.UserId));
            }
        if (scheduleBridge is not null)
            foreach (var suffix in new[] { "desktop", "mobile" })
            {
                var name = "k06-answer-" + suffix + "-" + Guid.CreateVersion7().ToString("N");
                var identity = await factory.CreateHostIdentityAsync(name, PersonalScheduleAnswerBrowserFixture.Permissions, password: FullNetApiFactory.TestPassword);
                scheduleBrowserUsers.Add((name, identity.UserId));
            }
        if(runBridge is not null)
        {
            foreach(var suffix in new[]{"desktop","mobile"})
            {
                var username="run-browser-"+suffix+"-"+Guid.CreateVersion7().ToString("N");
                var identity=await factory.CreateHostIdentityAsync(username,KnowledgeEvaluationRunBrowserFixture.Permissions,password:FullNetApiFactory.TestPassword);
                runBrowserUsers.Add((username,identity.UserId));
            }
        }
        var limited = !(verifyFaqFeedback || verifyEvaluationSources) ? null : await factory.CreateHostIdentityAsync(limitedName,
            [AiKnowledgePermissions.Create, AiKnowledgePermissions.PolicyUpdate, AiKnowledgePermissions.Read, AiKnowledgeFaqPermissions.Read, AiKnowledgeSearchPermissions.Read,
                AiKnowledgeFaqPermissions.Write, AiKnowledgeAnswerFeedbackReviewPermissions.Read, AiChatPermissions.Read,
                .. (verifyEvaluationSources ? new[] { AiKnowledgeEvaluationPermissions.Read, AiKnowledgeEvaluationPermissions.Approve, AiKnowledgeEvaluationPermissions.Feedback, AiFeedbackEvaluationPermissions.Admit } : Array.Empty<string>()),
                AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead, "tenancy.tenants.switch"],
            password: FullNetApiFactory.TestPassword, identityId: Guid.CreateVersion7());
        if (limited is not null) await factory.EnsureHostUserProfileEmailAsync(limited.UserId, limitedName + "@example.test", "仅结构化标签评审者");
        var browserName = "search-browser-" + Guid.CreateVersion7().ToString("N");
        var browserIdentity = string.IsNullOrWhiteSpace(browserBridge) ? null : await factory.CreateHostIdentityAsync(browserName,
            ["identity.navigation.read", "platform.dashboard.read", .. (verifyFaqFeedback ? new[] { AiChatPermissions.Read, AiKnowledgeAnswerFeedbackReviewPermissions.Read, AiKnowledgePermissions.Create, AiKnowledgePermissions.PolicyUpdate, AiKnowledgeFaqPermissions.Read, AiKnowledgeFaqPermissions.Write, AiKnowledgeFaqPermissions.Feedback } : verifyFaqParents ? new[] { AiKnowledgePermissions.Create, AiKnowledgeFaqPermissions.Read, AiKnowledgeFaqPermissions.Write, AiKnowledgeFaqPermissions.Import, AiKnowledgeFaqPermissions.Export } : verifyFaqTransfer ? new[] { AiKnowledgePermissions.Create, AiKnowledgePermissions.PolicyUpdate, AiKnowledgeFaqPermissions.Read, AiKnowledgeFaqPermissions.Write, AiKnowledgeFaqPermissions.Import, AiKnowledgeFaqPermissions.Export } : verifyFaqSearch ? new[] { AiKnowledgeFaqPermissions.Read, AiChatPermissions.Create, AiChatPermissions.Read, AiChatPermissions.Send, AiKnowledgeSearchPermissions.Compare, AiKnowledgeSearchPermissions.Refine } : verifyFaqLifecycle ? new[] { AiKnowledgePermissions.Create, AiKnowledgePermissions.PolicyUpdate, AiKnowledgePermissions.MembersUpdate, AiKnowledgeFaqPermissions.Read, AiKnowledgeFaqPermissions.Write, AiKnowledgeFaqPermissions.Index, AiKnowledgeFaqPermissions.Publish, AiKnowledgeDocumentPermissions.Upload, AiKnowledgeDocumentPermissions.IndexEnqueue, AiKnowledgePublicationPermissions.Read, AiKnowledgePublicationPermissions.Request, AiKnowledgePublicationPermissions.Publish, AiKnowledgePublicationPermissions.Withdraw } : verifyFaqDraft ? new[] { AiKnowledgePermissions.Create, AiKnowledgeFaqPermissions.Read, AiKnowledgeFaqPermissions.Write } : Array.Empty<string>()), .. (verifyFaqSearch ? Array.Empty<string>() : new[] { AiModelPermissions.Read }), AiKnowledgeSearchPermissions.Read, AiKnowledgePermissions.Read,
                AiKnowledgeDocumentPermissions.Read, AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.VersionsRead,
                AiKnowledgeDocumentPermissions.IndexRead, .. (verifySearchAnswer ? new[] { AiChatPermissions.Read, AiChatPermissions.Send } : Array.Empty<string>()),
                .. (verifyRefinement ? new[] { AiKnowledgeSearchPermissions.Compare, AiKnowledgeSearchPermissions.Refine } : verifyComparison ? new[] { AiKnowledgeSearchPermissions.Compare } : Array.Empty<string>())], password: FullNetApiFactory.TestPassword, identityId: verifyFaqLifecycle ? Guid.CreateVersion7() : null);
        // 浏览器会多次真实登录；独立审核账号不能挤掉后端用于最终来源断言的原读者会话。
        var browserReviewer = verifyFaqSearch || !verifyFaqLifecycle || browserIdentity is null ? null : await factory.CreateHostIdentityAsync(
            "faq-browser-reviewer-" + Guid.CreateVersion7().ToString("N"),
            ["identity.navigation.read", "platform.dashboard.read", AiKnowledgePermissions.Read, AiKnowledgeDocumentPermissions.Read,
                AiKnowledgeDocumentPermissions.SourceRead, AiKnowledgeDocumentPermissions.IndexRead, AiKnowledgeFaqPermissions.Read,
                AiKnowledgeFaqPermissions.Review, AiKnowledgePublicationPermissions.Read, AiKnowledgePublicationPermissions.Review],
            password: FullNetApiFactory.TestPassword, identityId: Guid.CreateVersion7());
        var browserPort = Environment.GetEnvironmentVariable("FULLNET_K03_BROWSER_PORT") ?? "64160";
        var nativeSettings = new Dictionary<string, string?>(settings);
        if (verifyFaqTransfer) { nativeSettings["FullNet:Ai:DataPolicy:export:0"] = "public"; nativeSettings["FullNet:Ai:DataPolicy:export:1"] = "internal"; }
        if (searchPolicyOnly) nativeSettings["FullNet:Ai:DataPolicy:rerank:0"] = "public";
        await using var api = artifact is null ? null : await NativeApiProcessHost.StartAsync(artifact, provider, connection, nativeSettings,
            NativeAotTestTimeouts.ProcessStartup, listenAddress:runBridge is not null || scheduleBridge is not null || businessFaqBridge is not null?new Uri("http://0.0.0.0:64215/"):browserIdentity is null ? null : new Uri("http://0.0.0.0:" + browserPort + "/"));
        if (api is not null) Console.WriteLine($"Native human publication log: {api.LogFilePath}");
        using var client = api?.CreateClient() ?? factory.CreateClientForHost("localhost"); using var readerClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        if ((verifySearch || verifyContext || verifySearchAnswer || verifyComparison || verifyRelations || verifyRefinement || verifyFaqDraft) && !native && Environment.GetEnvironmentVariable(verifyFaqDraft ? "FULLNET_K04_DRAFT_OPENAPI_EXPORT_DIR" : verifyRefinement ? "FULLNET_K03_REFINE_OPENAPI_EXPORT_DIR" : verifyRelations ? "FULLNET_K03_RELATIONS_OPENAPI_EXPORT_DIR" : verifyComparison ? "FULLNET_K03_COMPARE_OPENAPI_EXPORT_DIR" : verifySearchAnswer ? "FULLNET_K03_ANSWER_OPENAPI_EXPORT_DIR" : verifyContext ? "FULLNET_K03_CONTEXT_OPENAPI_EXPORT_DIR" : "FULLNET_K03_OPENAPI_EXPORT_DIR") is { Length: > 0 } export)
        {
            Directory.CreateDirectory(export);
            using var openapi = await client.GetAsync("/openapi/v1.json"); openapi.EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(Path.Combine(export, (verifyFaqDraft ? "k04-draft-openapi-" : verifyRefinement ? "k03-refine-openapi-" : verifyRelations ? "k03-relations-openapi-" : verifyComparison ? "k03-compare-openapi-" : verifySearchAnswer ? "k03-answer-openapi-" : verifyContext ? "k03-context-openapi-" : "k03-openapi-") + provider.ToString().ToLowerInvariant() + ".json"), await openapi.Content.ReadAsStringAsync());
        }
        var owner = await NativeApiE2EAssertions.LoginAsync(client, api?.LogFilePath ?? "JIT human publication");
        var readerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, readerName);
        using var limitedClient = api?.CreateClient() ?? factory.CreateClientForHost("localhost");
        var limitedToken = limited is null ? null : await IntegrationTestAuthHelper.LoginAsHostUserAsync(limitedClient, limitedName);
        var embedding = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", owner,
            new CreateAiModelConfigRequest(null, "人工原生向量", "openai_compatible", "https://api.openai.com/v1", "text-embedding-3-small", "native-test-placeholder", null, false, true), HttpStatusCode.Created);
        var generation = await Send<AiModelConfigResponse>(HttpMethod.Post, "/api/v1/ai/model-configs", owner,
            new CreateAiModelConfigRequest(null, "人工原生历史", "openai_compatible", "https://api.openai.com/v1", "gpt-4.1-mini", "native-test-placeholder", null, false, true), HttpStatusCode.Created);
        await using var db = Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider == DatabaseProvider.SqlServer, connection);
        AiKnowledgeFaqTransferPackage? hostTransferPackage = null;
        foreach (var tenant in verifySearchAnswer && !verifyPersonalScheduleAnswer ? new[] { false } : new[] { false, true })
        {
            if (tenant)
            {
                var target = (await Send<TenantContextSummary[]>(HttpMethod.Get, "/api/v1/tenancy/available", owner)).Single(t => t.Identifier == "acme");
                owner = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", owner, new ChangeTenantContextRequest(target.Id))).AccessToken;
                var invitation = await Send<CreateTenantInvitationResult>(HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", owner, new CreateTenantInvitationRequest(email, TenantMemberRoles.Admin));
                await Send<AcceptTenantInvitationResponse>(HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept", readerToken, new AcceptTenantInvitationRequest(invitation.InvitationToken));
                readerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, readerName);
                readerToken = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", readerToken, new ChangeTenantContextRequest(target.Id))).AccessToken;
                if (limited is not null)
                {
                    var limitedInvitation = await Send<CreateTenantInvitationResult>(HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", owner,
                        new CreateTenantInvitationRequest(limitedName + "@example.test", TenantMemberRoles.Admin));
                    await Send<AcceptTenantInvitationResponse>(HttpMethod.Post, "/api/v1/identity/tenant-invitations/accept", limitedToken!, new AcceptTenantInvitationRequest(limitedInvitation.InvitationToken));
                    limitedToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(limitedClient, limitedName);
                    limitedToken = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", limitedToken, new ChangeTenantContextRequest(target.Id))).AccessToken;
                }
            }
            using var ownerClaims = Payload(owner);
            var actor = Guid.Parse(ownerClaims.RootElement.GetProperty("sub").GetString()!);
            Guid? tenantId = tenant ? Guid.Parse(ownerClaims.RootElement.GetProperty(FullNetIdentityClaimTypes.TenantId).GetString()!) : null;
            var knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", owner, new CreateAiKnowledgeBaseRequest("人工原生发布", null), HttpStatusCode.Created);
            var root = $"/api/v1/ai/knowledge-bases/{knowledge.Id}";
            await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, root + "/policy", owner, new UpdateAiKnowledgePolicyRequest("internal", embedding.Id, embedding.Version, generation.Id, generation.Version, knowledge.Version));
            knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, root, owner);
            Guid[] readMembers = !tenant && browserIdentity is not null
                ? browserReviewer is not null ? [reader.UserId, browserIdentity.UserId, browserReviewer.UserId] : [reader.UserId, browserIdentity.UserId]
                : [reader.UserId];
            if (limited is not null) readMembers = [.. readMembers, limited.UserId];
            if(!tenant&&runBrowserUsers.Count>0)readMembers=[..readMembers,..runBrowserUsers.Select(value=>value.UserId)];
            if (!tenant && scheduleBrowserUsers.Count > 0) readMembers = [.. readMembers, .. scheduleBrowserUsers.Select(value => value.UserId)];
            if (!tenant && businessFaqUsers.Count > 0) readMembers = [.. readMembers, .. businessFaqUsers.Select(value => value.UserId)];
            await Send<AiKnowledgeMembersResponse>(HttpMethod.Put, root + "/members", owner, new SetAiKnowledgeMembersRequest(readMembers, knowledge.Version));
            var document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Post, root + "/documents", owner, new CreateAiKnowledgeDocumentRequest("人工原生原文", null), HttpStatusCode.Created);
            var path = root + "/documents/" + document.Id;
            await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest(readMembers, document.Version));
            document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
            using var upload = new HttpRequestMessage(HttpMethod.Post, path + $"/versions?version={document.Version}&requestKey={Guid.CreateVersion7()}&fileName={fileName}") { Content = new ByteArrayContent(sourceBytes) };
            upload.Content.Headers.ContentType = new(mime);
            upload.Headers.Authorization = new("Bearer", owner); using var uploaded = await client.SendAsync(upload); await Require(uploaded, HttpStatusCode.Accepted);
            var version = (await uploaded.Content.ReadFromJsonAsync<AiKnowledgeImportResponse>())!.VersionId;
            document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
            var correction = path + $"/versions/{version}/corrections"; var indexing = path + $"/versions/{version}/index"; var publications = path + "/publications";
            var prepared = await Send<AiKnowledgeCorrectionPreparationResponse>(HttpMethod.Post, correction + "/prepare", owner, new AiKnowledgeCorrectionPrepareRequest(Guid.CreateVersion7(), document.Version), HttpStatusCode.Accepted);
            using (var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection, new Dictionary<string, string?>(settings)
            {
                [$"{AiKnowledgeIndexWorkerOptions.SectionName}:CorrectionPreparationEnabled"] = "true",
                [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"] = "250"
            }, "NativeHumanSource", nameof(AiKnowledgeIndexHostedProcessor), settings["Files:Local:RootPath"]))
            {
                await worker.StartAsync();
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(format == "text" ? 45 : 150));
                    while (true)
                    {
                        var state = await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_correction_preparation WHERE Id=@Id", new { prepared.Id });
                        if (state == "completed") break;
                        Assert.IsFalse(state is "failed" or "cancelled", $"Actual {format} preparation ended as {state}.");
                        await Task.Delay(100, deadline.Token);
                    }
                }
                finally { await worker.StopAsync(); }
            }
            var current = await Workspace();
            var relationChildren = new HashSet<int>();
            var original = current.Block.Parts[0].OriginalText!; Assert.IsFalse(string.IsNullOrWhiteSpace(original));
            if (format == "text") Assert.AreEqual(Encoding.UTF8.GetString(sourceBytes), original);
            else if (format == "csv") Assert.IsNotNull(current.Block.Parts[0].SourceLocation!.Grid);
            else
            {
                if (format == "docx-image") Assert.AreEqual("docx_paragraph", current.Block.Parts[0].SourceLocation!.Kind);
                else Assert.IsNotNull(current.Block.Parts[0].SourceLocation!.Image);
                var blocks = current.TotalBlocks;
                // OCR 先逐段人工确认，包含低置信度片段；不靠放宽索引门槛接受原识别。
                for (var block = 0; block < blocks; block++)
                {
                    current = await Workspace(block);
                    if (verifyRelations)
                        foreach (var part in current.Block.Parts.Where(part => part.SourceLocation?.Image?.Parent is not null))
                        { Assert.AreEqual(0, part.SourceLocation!.Image!.Parent!.BlockSequence); relationChildren.Add(part.SourceLocation!.BlockSequence); }
                    foreach (var part in current.Block.Parts.Where(p => p.AnchorId.HasValue).ToArray()) await Edit("confirm", part.Id, null);
                }
                current = await Workspace();
            }
            await Edit("replace", current.Block.Parts[0].Id, "人工核对后的事实。");
            await Edit("append", null, "明确人工补写的说明。"); await Edit("merge", null, null);
            for (var i = 0; i < (verifyPersonalScheduleAnswer ? 0 : 16); i++) await Edit("append", null, "明确补写条目 " + i);
            var revision = current.RevisionId!.Value; var calls = model.Calls;
            var task = await Generate(partial: !verifyPersonalScheduleAnswer); Assert.AreEqual(calls + (verifyPersonalScheduleAnswer ? 1 : 2), model.Calls);
            var request = new RequestAiKnowledgePublication(Guid.CreateVersion7(), version, 0, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var published = await Publish(request); var firstPublication = published;
            var searchRequest = new SearchAiKnowledgeRequest([knowledge.Id], [document.Id], embedding.Id, embedding.Version, "明确 补写", 2, false, null);
            if (verifyPersonalScheduleAnswer)
            {
                if (verifyPersonalScheduleFeedback)
                {
                    await PersonalScheduleFeedbackAssertions.VerifyAsync(client, db, owner, readerToken, knowledge.Id, embedding.Id, embedding.Version,
                        generation.Id, generation.Version, tenant ? ids => SaveHistory(personalScheduleIds: ids) : null);
                    continue;
                }
                if (verifyPersonalScheduleFaq)
                {
                    await PersonalScheduleFaqAssertions.VerifyAsync(client, db, owner, readerToken, knowledge.Id, document.Id, embedding.Id, embedding.Version, generation.Id, generation.Version,
                        async admission =>
                        {
                            using var worker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection,
                                new Dictionary<string,string?>(settings) { [$"{AiKnowledgeIndexWorkerOptions.SectionName}:Enabled"] = "true", [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"] = "250" },
                                "PersonalScheduleFaqIndex", nameof(AiKnowledgeIndexHostedProcessor), settings["Files:Local:RootPath"],
                                configureServices: services => { services.RemoveAll<IAiModelClientFactory>(); services.AddSingleton<IAiModelClientFactory>(model); FaqLifecycleQueryTrace.Attach(services); });
                            await using (var diagnostic = worker.Services.CreateAsyncScope())
                            {
                                var current = diagnostic.ServiceProvider.GetRequiredService<Full.NET.Abstractions.Tenancy.CurrentTenantAccessor>();
                                var session = await db.QuerySingleAsync<AiKnowledgeIndexSessionRecord>("SELECT * FROM fn_ai_knowledge_index_session WHERE IndexTaskId=@Id",new{Id=admission.TaskId});
                                if(session.TenantId is {} sourceTenant)current.SetTenant(new(sourceTenant,"faq-business-probe","当前业务来源验证"));else current.SetHost();
                                var parameters = new Dictionary<string,object?> { ["KnowledgeBaseId"] = await db.QuerySingleAsync<Guid>("SELECT KnowledgeBaseId FROM fn_ai_knowledge_faq WHERE Id=@Id",new{Id=admission.FaqId}),
                                    ["FaqId"] = admission.FaqId,["RevisionId"] = admission.RevisionId,["OwnerUserId"] = session.ActorUserId };
                                var record = await diagnostic.ServiceProvider.GetRequiredService<IQueryExecutor>().QuerySingleOrDefaultAsync<AiKnowledgeFaqDraftRecord>(session.TenantId.HasValue?AiKnowledgeFaqDraftSql.RevisionTenant:AiKnowledgeFaqDraftSql.RevisionHost,parameters);
                                Assert.IsNotNull(record);var origin=AiKnowledgeFaqProvenanceCodec.Decode(record);
                                var original=new SessionBindingSnapshot(session.ActorUserId,session.TenantId,session.SessionId,session.SecurityStamp,session.ActorScope,session.EffectiveScope,session.SessionKind);
                                var ownerAllowed=await diagnostic.ServiceProvider.GetRequiredService<Full.NET.Modules.Calendar.Contracts.IPersonalScheduleKnowledgeSource>().VerifyBackgroundAsync(original,origin.PersonalSchedules!);
                                Console.WriteLine("Business FAQ original-session Calendar proof: "+ownerAllowed);Assert.IsTrue(ownerAllowed,"原会话Calendar权威复核必须真实通过。");
                                var allowed=await diagnostic.ServiceProvider.GetRequiredService<AiKnowledgeFaqDraftSourceGuard>().AuthorizeIndexBindingsAsync(record,original,default);
                                Console.WriteLine("Business FAQ complete binding proof: "+allowed.Contains(record.Id));Assert.IsTrue(allowed.Contains(record.Id),"完整FAQ元数据准入必须真实通过。");current.Clear();
                            }
                            await worker.StartAsync();
                            try
                            {
                                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                                while (true)
                                {
                                    var state = await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id",new { Id = admission.TaskId });
                                    if(state is "failed" or "cancelled")FaqLifecycleQueryTrace.Dump(admission.TaskId);
                                    Assert.IsFalse(state is "failed" or "cancelled", "Actual business FAQ Worker ended as " + state);
                                    if (state == "completed") break;
                                    await Task.Delay(150, wait.Token);
                                }
                            }
                            finally { await worker.StopAsync(); FaqLifecycleQueryTrace.Save(); }
                        });
                    if (!tenant && businessFaqBridge is not null)
                        await PersonalScheduleFaqBrowserFixture.VerifyAsync(client,db,businessFaqBridge,businessFaqUsers,knowledge.Name,embedding.Id,embedding.Version,generation.Id,generation.Version);
                    continue;
                }
                if (tenant)
                {
                    // 租户写聊天仍为 403；这里只建立由当前真实来源证明支持的只读持久历史夹具。
                    var start = DateTimeOffset.UtcNow;
                    var personal = await Send<Full.NET.Modules.Calendar.Contracts.PersonalScheduleResponse>(HttpMethod.Post,
                        "/api/v1/calendar/my-personal-schedules", readerToken, new Full.NET.Modules.Calendar.Contracts.CreatePersonalScheduleRequest("租户本人日程", start, start), HttpStatusCode.Created);
                    var fees = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"); var modelCalls = model.Calls;
                    var tenantSaved = await SaveHistory(personalScheduleIds: [personal.Id]);
                    var tenantHistoryPath = $"/api/v1/ai/chat/sessions/{tenantSaved.SessionId}";
                    var tenantHistory = await Send<AiChatSessionResponse>(HttpMethod.Get, tenantHistoryPath, readerToken);
                    Assert.IsFalse(string.IsNullOrWhiteSpace(tenantHistory.Messages.Single(value => value.Id == tenantSaved.MessageId).Content));
                    await Expect(HttpMethod.Post, "/api/v1/ai/chat/sessions", readerToken, new CreateAiChatSessionRequest(generation.Id, null), HttpStatusCode.Forbidden);
                    await Expect(HttpMethod.Post, tenantHistoryPath + "/knowledge/ask", readerToken,
                        new AskAiKnowledgeRequest([knowledge.Id], embedding.Id, "不扩大租户模型派发"), HttpStatusCode.Forbidden);
                    await Send<Full.NET.Modules.Calendar.Contracts.PersonalScheduleResponse>(HttpMethod.Put, "/api/v1/calendar/my-personal-schedules/" + personal.Id,
                        readerToken, new Full.NET.Modules.Calendar.Contracts.UpdatePersonalScheduleRequest("租户日程已变化", start, start, personal.Version));
                    tenantHistory = await Send<AiChatSessionResponse>(HttpMethod.Get, tenantHistoryPath, readerToken);
                    var hidden = tenantHistory.Messages.Single(value => value.Id == tenantSaved.MessageId);
                    Assert.AreEqual("", hidden.Content); Assert.AreEqual(AiErrorCodes.KnowledgeAnswerUnavailable, hidden.KnowledgeUnavailableCode);
                    Assert.AreEqual(fees, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget")); Assert.AreEqual(modelCalls, model.Calls);
                }
                else await PersonalScheduleAnswerAssertions.VerifyAsync(client, db, readerToken, knowledge.Id, embedding.Id, generation.Id,
                    native ? () => answerModelServer!.PreferPersonalScheduleCitation = true : null,
                    native ? () => answerModelServer!.StreamingInputs.Last() : null);
                if (!tenant && scheduleBridge is not null)
                    await PersonalScheduleAnswerBrowserFixture.VerifyAsync(client, db, scheduleBridge, scheduleBrowserUsers, knowledge.Id, knowledge.Name, embedding.Id, generation.Id);
                continue;
            }
            if(verifyEvaluationRuns)
            {
                await KnowledgeEvaluationRunAssertions.VerifyAsync(client,db,knowledge.Id,document.Id,embedding.Id,embedding.Version,generation.Id,generation.Version,
                    owner,readerToken,()=>model.Calls+refinementModel.ChatCalls+(answerModelServer?.ChatCalls??0)+(answerModelServer?.EmbeddingInputs.Count??0),
                    (fault,unknown)=>{if(answerModelServer is not null){answerModelServer.RefinementFault=fault;answerModelServer.OmitChatUsage=unknown;answerModelServer.OmitEmbeddingUsage=unknown;}
                        else{refinementModel.Fault=fault;refinementModel.OmitUsage=unknown;model.InputTokens=unknown?null:7;}},
                    async()=>{await Edit("replace",current.Block.Parts[0].Id,"运行后替换来源。");_=await Generate(partial:false);_=await Publish(request with{RequestKey=Guid.CreateVersion7(),ExpectedHeadVersion=1});},nativeHttp:native).ConfigureAwait(false);
                if(!tenant&&runBridge is not null)
                {
                    await KnowledgeEvaluationRunBrowserFixture.VerifyAsync(client,runBridge,runBrowserUsers,knowledge.Id,knowledge.Name,embedding.Id,embedding.Version,generation.Id,generation.Version,
                        reader.UserId,readerToken,async()=>
                        {
                            // 浏览器操作者与后台原来源所有者分开登录；不能把401当作来源替换的清空证明。
                            owner=await NativeApiE2EAssertions.LoginAsync(client,api!.LogFilePath);
                            // 页面集中验收可能跨越访问令牌期限；切租户前为独立审核者正式续取会话。
                            readerToken=await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient,readerName);
                            var actual=await Send<AiKnowledgeBaseResponse>(HttpMethod.Get,root,owner);
                            await Send<AiKnowledgeBaseResponse>(HttpMethod.Put,root,owner,new UpdateAiKnowledgeBaseRequest(actual.Name,actual.Description,false,actual.Version));
                        }).ConfigureAwait(false);
                }
                continue;
            }
            if (verifyFaqTransfer)
            {
                var transferred = await FaqTransferAssertions.VerifyAsync(client, db, connection, owner, readerToken, knowledge.Id, document.Id, embedding.Id, embedding.Version,
                    () => model.Calls, async () =>
                    {
                        await Edit("replace", current.Block.Parts[0].Id, "替换传输依赖来源。"); _ = await Generate(partial: false);
                        published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                    }, tenant ? hostTransferPackage : null, !tenant && browserIdentity is not null ? async _ =>
                    {
                        var browserToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, browserName);
                        var browserBase = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", browserToken, new CreateAiKnowledgeBaseRequest("受控 FAQ 传输管理", null), HttpStatusCode.Created);
                        var browserDraftRoot = $"/api/v1/ai/knowledge-bases/{browserBase.Id}/faq-drafts";
                        var browserContent = new AiKnowledgeFaqContent("传输原始独立问题", [], "独立传输正文", "制度", null, null);
                        await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, browserDraftRoot + "/independent", browserToken, new CreateAiKnowledgeFaqIndependentDraft(Guid.CreateVersion7(), browserContent, "明确的独立编写依据"));
                        var browserSources = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", browserToken, searchRequest);
                        await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, browserDraftRoot + "/from-documents", browserToken, new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(), browserContent with { Question = "传输原始派生问题", Answer = "完整文档派生传输正文" }, new(embedding.Id, embedding.Version, browserSources.Sources)));
                        var lowerBase = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", browserToken, new CreateAiKnowledgeBaseRequest("分类不足的 FAQ 目标库", null), HttpStatusCode.Created);
                        await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, $"/api/v1/ai/knowledge-bases/{lowerBase.Id}/policy", browserToken, new UpdateAiKnowledgePolicyRequest("public", null, null, null, null, lowerBase.Version));
                        await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new { username = browserName, password = FullNetApiFactory.TestPassword, knowledgeBaseId = browserBase.Id, knowledgeName = browserBase.Name, lowerKnowledgeName = lowerBase.Name }));
                        await WaitBrowser("advance.json", "advance");
                    } : null, !tenant && browserIdentity is not null ? async () =>
                    {
                        await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed");
                    } : null);
                if (!tenant) hostTransferPackage = transferred;
                continue;
            }
            if (verifyFaqLifecycle)
            {
                var sourceSelection = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                var faqPath = root + "/faq-drafts";
                var faqText = new AiKnowledgeFaqContent("真实 FAQ 标准问题", ["真实 FAQ 同义问法"], "数据库修订的正式答案。", "制度", DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddDays(1));
                var faq = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqPath + "/from-documents", owner,
                    new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(), faqText, new(embedding.Id, embedding.Version, sourceSelection.Sources)));
                var managed = faqPath + "/" + faq.Workspace.Id; var publishedPath = root + "/faqs/" + faq.Workspace.Id;
                var filesBefore = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file");
                var budgetBefore = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                await Expect(HttpMethod.Post, managed + "/index", owner, new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(), faq.Workspace.Revision.Id, 1, false), HttpStatusCode.BadRequest);
                Assert.AreEqual(budgetBefore, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                var indexRequest = new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(), faq.Workspace.Revision.Id, 1, true);
                var indexed = await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post, managed + "/index", owner, indexRequest, HttpStatusCode.Accepted);
                var replayIndex = await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post, managed + "/index", owner, indexRequest, HttpStatusCode.Accepted);
                Assert.IsTrue(replayIndex.Replayed); Assert.AreEqual(indexed.TaskId, replayIndex.TaskId); Assert.AreEqual(indexed.VersionId, replayIndex.VersionId);
                var savedContent = faq.Workspace.Revision.Content;
                var proposalRequest = new RequestAiKnowledgeFaqPublication(Guid.CreateVersion7(), faq.Workspace.Revision.Id, indexed.VersionId, 1, 0, savedContent.ValidFromUtc!.Value, savedContent.ExpiresAtUtc!.Value);
                await Expect(HttpMethod.Post, managed + "/publication-requests", owner, proposalRequest, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get, publishedPath, readerToken, null, HttpStatusCode.NotFound);
                Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_version WHERE Id=@Id AND FileId IS NULL AND FaqRevisionId=@Revision", new { Id=indexed.VersionId, Revision=faq.Workspace.Revision.Id }));
                Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_import WHERE VersionId=@Id", new { Id=indexed.VersionId }));
                await Expect(HttpMethod.Get, root + "/documents/" + faq.Workspace.ReservedDocumentId, owner, null, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Get, root + $"/documents/{faq.Workspace.ReservedDocumentId}/versions/{indexed.VersionId}/source", owner, null, HttpStatusCode.NotFound);
                using var faqWorker = await IntegrationWorkerHostFactory.BuildAsync(provider, connection,
                    new Dictionary<string,string?>(settings) { [$"{AiKnowledgeIndexWorkerOptions.SectionName}:Enabled"]="true", [$"{AiKnowledgeIndexWorkerOptions.SectionName}:PollMilliseconds"]="250" },
                    "FaqLifecycleIndex", nameof(AiKnowledgeIndexHostedProcessor), settings["Files:Local:RootPath"], configureServices: services => { services.RemoveAll<IAiModelClientFactory>(); services.AddSingleton<IAiModelClientFactory>(model); FaqLifecycleQueryTrace.Attach(services); });
                await using(var closure=faqWorker.Services.CreateAsyncScope())
                {
                    _=closure.ServiceProvider.GetRequiredService<AiKnowledgeIndexTaskStore>();
                    _=closure.ServiceProvider.GetRequiredService<AiKnowledgeIndexPersistenceService>();
                    if (!verifyFaqSearch) await FaqLifecycleRootLockAssertions.VerifyAsync(provider, connection, closure.ServiceProvider, sourceSelection.Sources.Single(), embedding.Id);
                }
                await faqWorker.StartAsync();
                try
                {
                    await Ready(indexed, managed);
                    var status = await Send<AiKnowledgeFaqLifecycle>(HttpMethod.Get, managed + "/lifecycle", owner);
                    Assert.AreEqual("ready", status.IndexState); Assert.AreEqual("unpublished", status.PublicationState); Assert.IsNull(status.PublicationId);
                    var proposal = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + "/publication-requests", owner, proposalRequest, HttpStatusCode.Created);
                    Assert.AreEqual(proposal.Id, (await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + "/publication-requests", owner, proposalRequest, HttpStatusCode.Created)).Id);
                    Assert.IsTrue((await Send<AiKnowledgeFaqPublication>(HttpMethod.Get, managed + "/publications/" + proposal.Id, readerToken)).Current);
                    await Expect(HttpMethod.Post, managed + $"/publications/{proposal.Id}/review", owner, new ReviewAiKnowledgePublication(true, proposal.Version, proposal.SnapshotHash), HttpStatusCode.Conflict);
                    await Expect(HttpMethod.Post, managed + $"/publications/{proposal.Id}/publish", owner, new PublishAiKnowledgePublication(proposal.Version, proposal.SnapshotHash), HttpStatusCode.Conflict);
                    proposal = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + $"/publications/{proposal.Id}/review", readerToken, new ReviewAiKnowledgePublication(true, proposal.Version, proposal.SnapshotHash));
                    var approvedVersion = proposal.Version;
                    proposal = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + $"/publications/{proposal.Id}/publish", owner, new PublishAiKnowledgePublication(approvedVersion, proposal.SnapshotHash));
                    Assert.AreEqual("published", proposal.State); Assert.IsTrue(proposal.Current);
                    Assert.AreEqual(proposal.Id, (await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + $"/publications/{proposal.Id}/publish", owner, new PublishAiKnowledgePublication(approvedVersion, proposal.SnapshotHash))).Id);
                    var delivered = await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get, publishedPath, readerToken);
                    Assert.AreEqual(faqText.Answer, delivered.Content.Answer); Assert.AreEqual("document_derived", delivered.Origin); Assert.AreEqual(faq.Workspace.SourceHash, delivered.SourceHash); Assert.AreEqual(1, delivered.Sources.Count);
                    if (verifyEvaluationSources)
                    {
                        await KnowledgeEvaluationSourceAssertions.VerifyAsync(client, db, knowledge.Id, faq.Workspace, embedding.Id, embedding.Version,
                            generation.Id, generation.Version, owner, readerToken, limitedToken!, readMembers,
                            () => model.Calls + refinementModel.ChatCalls + (answerModelServer?.ChatCalls ?? 0) + (answerModelServer?.EmbeddingInputs.Count ?? 0),
                            tenant ? source => SaveHistory(source) : null, verifyRuns:verifyEvaluationPrivateRuns);
                        continue;
                    }
                    if (verifyFaqFeedback)
                    {
                        async Task<(string Owner, string Reader, string LabelOnly)> RenewFaqActors()
                        {
                            var previous = new[] { owner, readerToken, limitedToken! };
                            // 仅在全部索引与发布完成后重登，保持单会话策略，不让旧后台任务的原会话被提前撤销。
                            owner = await NativeApiE2EAssertions.LoginAsync(client, api?.LogFilePath ?? "JIT human publication");
                            readerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, readerName);
                            limitedToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(limitedClient, limitedName);
                            if (tenantId is { } currentTenant)
                            {
                                var context = new ChangeTenantContextRequest(currentTenant);
                                owner = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", owner, context)).AccessToken;
                                readerToken = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", readerToken, context)).AccessToken;
                                limitedToken = (await Send<TenantContextTokenResponse>(HttpMethod.Put, "/api/v1/tenancy/context", limitedToken, context)).AccessToken;
                            }
                            var renewed = new[] { owner, readerToken, limitedToken! };
                            for (var index = 0; index < previous.Length; index++)
                            {
                                using var before = Payload(previous[index]); using var after = Payload(renewed[index]);
                                foreach (var claim in new[] { "sub", FullNetIdentityClaimTypes.Scope, FullNetIdentityClaimTypes.TenantId })
                                {
                                    var oldValue = before.RootElement.TryGetProperty(claim, out var oldClaim) ? oldClaim.GetString() : null;
                                    var newValue = after.RootElement.TryGetProperty(claim, out var newClaim) ? newClaim.GetString() : null;
                                    Assert.AreEqual(oldValue, newValue, "正式续取令牌必须保持原操作者和可信作用域。");
                                }
                            }
                            Console.WriteLine($"FAQ feedback actors renewed through login: tenant={tenant}; identity_scope_preserved=true");
                            return (owner, readerToken, limitedToken!);
                        }
                        // Host 与 Tenant 各自从新会话开始，不能把上一作用域耗时带入本轮完整来源验证。
                        await RenewFaqActors();
                        await FaqFeedbackAssertions.VerifyAsync(client, db, connection, knowledge.Id, document.Id, faq.Workspace, embedding.Id, embedding.Version,
                            generation.Id, generation.Version, owner, readerToken, limitedToken!, readMembers,
                            () => model.Calls + refinementModel.ChatCalls + (answerModelServer?.ChatCalls ?? 0) + (answerModelServer?.EmbeddingInputs.Count ?? 0), async child =>
                            {
                                var childPath = $"/api/v1/ai/knowledge-bases/{child.KnowledgeBaseId}/faq-drafts/{child.Id}";
                                var admission = await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post, childPath + "/index", owner,
                                    new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(), child.Revision.Id, child.HeadVersion, true), HttpStatusCode.Accepted);
                                await Ready(admission, childPath);
                                var proposed = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, childPath + "/publication-requests", owner,
                                    new RequestAiKnowledgeFaqPublication(Guid.CreateVersion7(), child.Revision.Id, admission.VersionId, child.HeadVersion, 0,
                                        child.Revision.Content.ValidFromUtc!.Value, child.Revision.Content.ExpiresAtUtc!.Value), HttpStatusCode.Created);
                                proposed = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, childPath + $"/publications/{proposed.Id}/review", readerToken,
                                    new ReviewAiKnowledgePublication(true, proposed.Version, proposed.SnapshotHash));
                                return await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, childPath + $"/publications/{proposed.Id}/publish", owner,
                                    new PublishAiKnowledgePublication(proposed.Version, proposed.SnapshotHash));
                            }, tenant ? source => SaveHistory(source) : null, !tenant && browserIdentity is not null ? async feedbackReady =>
                            {
                                var browserToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(limitedClient, browserName);
                                var target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", browserToken,
                                    new CreateAiKnowledgeBaseRequest("私人反馈草稿保存目标", null), HttpStatusCode.Created);
                                target = await Send<AiKnowledgeBaseResponse>(HttpMethod.Put, $"/api/v1/ai/knowledge-bases/{target.Id}/policy", browserToken,
                                    new UpdateAiKnowledgePolicyRequest("restricted", null, null, null, null, target.Version));
                                await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new
                                {
                                    username = browserName, password = FullNetApiFactory.TestPassword, targetName = target.Name, targetId = target.Id, lowTargetName = knowledge.Name,
                                    feedbackId = feedbackReady.FeedbackId, feedbackVersion = feedbackReady.FeedbackVersion, reviewVersion = feedbackReady.ReviewVersion
                                }));
                                await WaitBrowser("advance.json", "advance");
                                owner = await NativeApiE2EAssertions.LoginAsync(client, api?.LogFilePath ?? "JIT human publication");
                                readerToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, readerName);
                                limitedToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(limitedClient, limitedName);
                                return (owner, readerToken, limitedToken!);
                            } : null, !tenant && browserIdentity is not null ? async () =>
                            {
                                await File.WriteAllTextAsync(Path.Combine(browserBridge!, "revoked.json"), "{}"); await WaitBrowser("complete.json", "passed");
                            } : null, RenewFaqActors);
                        continue;
                    }
                    if (verifyFaqParents)
                    {
                        await FaqParentAssertions.VerifyAsync(client,db,knowledge.Id,document.Id,faq.Workspace,proposal.Id,embedding.Id,embedding.Version,generation.Id,generation.Version,
                            owner,readerToken,readMembers,async child =>
                            {
                                var childPath=root+"/faq-drafts/"+child.Id;
                                var admission=await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post,childPath+"/index",owner,new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(),child.Revision.Id,child.HeadVersion,true),HttpStatusCode.Accepted);
                                await Ready(admission,childPath);
                                var proposed=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,childPath+"/publication-requests",owner,
                                    new RequestAiKnowledgeFaqPublication(Guid.CreateVersion7(),child.Revision.Id,admission.VersionId,child.HeadVersion,0,child.Revision.Content.ValidFromUtc!.Value,child.Revision.Content.ExpiresAtUtc!.Value),HttpStatusCode.Created);
                                proposed=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,childPath+$"/publications/{proposed.Id}/review",readerToken,new ReviewAiKnowledgePublication(true,proposed.Version,proposed.SnapshotHash));
                                return await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,childPath+$"/publications/{proposed.Id}/publish",owner,new PublishAiKnowledgePublication(proposed.Version,proposed.SnapshotHash));
                            },!tenant&&browserIdentity is not null?async source=>
                            {
                                var browserToken=await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient,browserName);
                                var target=await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",browserToken,new CreateAiKnowledgeBaseRequest("父 FAQ 派生保存目标",null),HttpStatusCode.Created);
                                await File.WriteAllTextAsync(Path.Combine(browserBridge!,"ready.json"),JsonSerializer.Serialize(new{username=browserName,password=FullNetApiFactory.TestPassword,knowledgeName=knowledge.Name,targetName=target.Name,source}));
                                await WaitBrowser("advance.json","advance");
                            }:null,!tenant&&browserIdentity is not null?async()=>
                            {
                                await File.WriteAllTextAsync(Path.Combine(browserBridge!,"revoked.json"),"{}");await WaitBrowser("complete.json","passed");
                            }:null,tenant?source=>SaveHistory(source):null);
                        continue;
                    }
                    if (verifyFaqSearch)
                    {
                        await FaqSearchAssertions.VerifyAsync(client, db, knowledge.Id, document.Id, faq.Workspace.Id,
                            faq.Workspace.ReservedDocumentId, faq.Workspace.Revision.Id, proposal.Id, embedding.Id, embedding.Version,
                            generation.Id, generation.Version, owner, readerToken, readMembers, async () =>
                            {
                                // FAQ 已完成索引；先等待后台领取循环退出，再由夹具独占推进替换来源的人工任务。
                                await faqWorker.StopAsync();
                                Assert.IsTrue(faqWorker.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                                    .OfType<AiKnowledgeIndexHostedProcessor>().Single().ExecuteTask?.IsCompleted == true);
                                // 撤权/恢复场景已推进文档并发版本，替换来源前重新读取真实校对头。
                                current = await Workspace(current.BlockIndex);
                                await Edit("replace", current.Block.Parts[0].Id, "替换后的 FAQ 叶来源。");
                                _ = await Generate(partial: false);
                                _ = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                            }, !tenant && browserIdentity is not null ? async source =>
                            {
                                var browserToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, browserName);
                                var sessions = new List<Guid>();
                                foreach (var label in new[] { "桌面", "移动", "移动历史" })
                                    sessions.Add((await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", browserToken,
                                        new CreateAiChatSessionRequest(generation.Id, "FAQ 来源" + label), HttpStatusCode.Created)).Id);
                                await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new
                                {
                                    username = browserName, password = FullNetApiFactory.TestPassword, knowledgeBaseId = knowledge.Id,
                                    knowledgeName = knowledge.Name, faqId = source.FaqId, documentId = source.DocumentId,
                                    revisionId = source.FaqRevisionId, publicationId = source.PublicationId, sessions
                                }));
                                await WaitBrowser("advance.json", "advance");
                            } : null, !tenant && browserIdentity is not null ? async () =>
                            {
                                await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}");
                                await WaitBrowser("complete.json", "passed");
                            } : null, tenant ? source => SaveHistory(source) : null);
                        continue;
                    }
                    foreach(var afterDispatch in new[]{false,true})
                    {
                        // 保留全部身份/摘要，只破坏真实叶载荷，证明阶段绑定不能代替收费和交付边界的完整核证。
                        var guarded=await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,faqPath+"/from-documents",owner,
                            new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(),faqText with{Question=afterDispatch?"收费后来源完整性":"收费前来源完整性",Aliases=[]},new(embedding.Id,embedding.Version,sourceSelection.Sources)));
                        var originalPayload=await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_document_manifest WHERE Id=@Id",new{Id=sourceSelection.Sources.Single().GenerationId});
                        var damaged=originalPayload.ToArray();damaged[^1]^=1;var injected=false;var callsAtFence=model.Calls;
                        async Task Corrupt()
                        {
                            // 回调与状态轮询在不同异步执行链，不能并发复用同一 MySQL 连接。
                            await using var faultConnection=Migration246AiKnowledgeIndexTaskRecoveryTests.Connection(provider==DatabaseProvider.SqlServer,connection);
                            injected=true;await faultConnection.ExecuteAsync("UPDATE fn_ai_knowledge_document_manifest SET Payload=@Payload WHERE Id=@Id",new{Id=sourceSelection.Sources.Single().GenerationId,Payload=damaged});
                        }
                        model.Create=afterDispatch?null:Corrupt;model.Generate=afterDispatch?Corrupt:null;model.InputTokens=afterDispatch?null:7;
                        try
                        {
                            var guardedIndex=await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post,faqPath+"/"+guarded.Workspace.Id+"/index",owner,new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(),guarded.Workspace.Revision.Id,1,true),HttpStatusCode.Accepted);
                            using var fenceDeadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                            while(await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id",new{Id=guardedIndex.TaskId}) is "queued" or "running")await Task.Delay(150,fenceDeadline.Token);
                            Assert.IsTrue(injected,"必须真实经过客户端创建或模型派发，不能用较早拒绝冒充最后门禁。");
                            Assert.AreEqual(callsAtFence+(afterDispatch?1:0),model.Calls);
                            Assert.AreEqual("failed",await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id",new{Id=guardedIndex.TaskId}));
                            var usage=afterDispatch?"unknown":"known";
                            Assert.AreEqual(1,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id WHERE batch.IndexTaskId=@Id AND ledger.Outcome='cancelled' AND ledger.UsageStatus=@Usage AND ledger.SettledAtUtc IS NOT NULL AND "+(afterDispatch?"ledger.ChargedTokens=ledger.ReservedTokens":"ledger.ChargedTokens=0"),new{Id=guardedIndex.TaskId,Usage=usage}));
                            Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_active_generation WHERE DocumentId=@Id",new{Id=guarded.Workspace.ReservedDocumentId}));
                            await Expect(HttpMethod.Get,publishedPath,readerToken,null,HttpStatusCode.Conflict);
                        }
                        finally{model.Create=null;model.Generate=null;model.InputTokens=7;await db.ExecuteAsync("UPDATE fn_ai_knowledge_document_manifest SET Payload=@Payload WHERE Id=@Id",new{Id=sourceSelection.Sources.Single().GenerationId,Payload=originalPayload});}
                        Assert.AreEqual(proposal.Id,(await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get,publishedPath,readerToken)).PublicationId);
                    }
                    var firstFaqPublication = proposal;
                    var revised = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put, managed, owner,
                        new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(), faq.Workspace.Revision.Id, 1, faqText with { Answer="新修订尚未发布。" }));
                    Assert.AreEqual(faqText.Answer, (await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get, publishedPath, readerToken)).Content.Answer);
                    var secondIndex = await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post, managed + "/index", owner,
                        new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(), revised.Workspace.Revision.Id, 2, true), HttpStatusCode.Accepted);
                    await Ready(secondIndex, managed);
                    Assert.AreEqual(firstFaqPublication.Id, (await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get, publishedPath, readerToken)).PublicationId);
                    var secondRequest = proposalRequest with { RequestKey=Guid.CreateVersion7(), ExpectedRevisionId=revised.Workspace.Revision.Id, VersionId=secondIndex.VersionId, ExpectedHeadVersion=2, ExpectedPublicationVersion=1 };
                    var secondProposal = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + "/publication-requests", owner, secondRequest, HttpStatusCode.Created);
                    secondProposal = await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, managed + $"/publications/{secondProposal.Id}/review", readerToken, new ReviewAiKnowledgePublication(true, secondProposal.Version, secondProposal.SnapshotHash));
                    var collision = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqPath + "/independent", owner,
                        new CreateAiKnowledgeFaqIndependentDraft(Guid.CreateVersion7(), faqText with { Question="  真实 FAQ 标准问题  ", Aliases=[], Answer="另一条独立权威答案。" }, "维护者独立依据，仍需单独审核。"));
                    var collisionPath=faqPath+"/"+collision.Workspace.Id;
                    var collisionIndex = await Send<AiKnowledgeFaqIndexAdmission>(HttpMethod.Post, collisionPath + "/index", owner,
                        new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(), collision.Workspace.Revision.Id, 1, true), HttpStatusCode.Accepted);
                    await Ready(collisionIndex, collisionPath);
                    var collisionRequest = proposalRequest with { RequestKey=Guid.CreateVersion7(), ExpectedRevisionId=collision.Workspace.Revision.Id, VersionId=collisionIndex.VersionId };
                    var collisionProposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, collisionPath + "/publication-requests", owner, collisionRequest, HttpStatusCode.Created);
                    collisionProposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post, collisionPath+$"/publications/{collisionProposal.Id}/review",readerToken,new ReviewAiKnowledgePublication(true,collisionProposal.Version,collisionProposal.SnapshotHash));
                    var beforeConflict=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"); var beforeConflictCalls=model.Calls;
                    await Expect(HttpMethod.Post, collisionPath+$"/publications/{collisionProposal.Id}/publish",owner,new PublishAiKnowledgePublication(collisionProposal.Version,collisionProposal.SnapshotHash),HttpStatusCode.Conflict);
                    Assert.IsNull((await Send<AiKnowledgeFaqLifecycle>(HttpMethod.Get,collisionPath+"/lifecycle",owner)).PublicationId);
                    Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq_query WHERE FaqId=@Id",new{Id=collision.Workspace.Id}));
                    Assert.AreEqual(firstFaqPublication.Id,(await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get,publishedPath,readerToken)).PublicationId);
                    Assert.AreEqual(beforeConflict,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget")); Assert.AreEqual(beforeConflictCalls,model.Calls);
                    await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Post,managed+"/publications/withdraw",owner,new WithdrawAiKnowledgePublication(1,firstFaqPublication.Id));
                    await Expect(HttpMethod.Get,publishedPath,readerToken,null,HttpStatusCode.NotFound);
                    await Expect(HttpMethod.Post,managed+$"/publications/{secondProposal.Id}/publish",owner,new PublishAiKnowledgePublication(secondProposal.Version,secondProposal.SnapshotHash),HttpStatusCode.Conflict);
                    collisionProposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,collisionPath+$"/publications/{collisionProposal.Id}/publish",owner,new PublishAiKnowledgePublication(collisionProposal.Version,collisionProposal.SnapshotHash));
                    await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Post,collisionPath+"/publications/withdraw",owner,new WithdrawAiKnowledgePublication(1,collisionProposal.Id));
                    var finalProposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,managed+"/publication-requests",owner,secondRequest with { RequestKey=Guid.CreateVersion7(),ExpectedPublicationVersion=2 },HttpStatusCode.Created);
                    finalProposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,managed+$"/publications/{finalProposal.Id}/review",readerToken,new ReviewAiKnowledgePublication(true,finalProposal.Version,finalProposal.SnapshotHash));
                    finalProposal=await Send<AiKnowledgeFaqPublication>(HttpMethod.Post,managed+$"/publications/{finalProposal.Id}/publish",owner,new PublishAiKnowledgePublication(finalProposal.Version,finalProposal.SnapshotHash));
                    if(!tenant&&browserIdentity is not null)
                    {
                        // 浏览器维护者真实创建自己的库；审核者只拥有显式读成员资格，不能借根管理员代办。
                        var browserToken=await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient,browserName);
                        var browserBase=await Send<AiKnowledgeBaseResponse>(HttpMethod.Post,"/api/v1/ai/knowledge-bases",browserToken,new CreateAiKnowledgeBaseRequest("FAQ 发布管理",null),HttpStatusCode.Created);
                        var browserRoot=$"/api/v1/ai/knowledge-bases/{browserBase.Id}";
                        await Send<AiKnowledgeBaseResponse>(HttpMethod.Put,browserRoot+"/policy",browserToken,new UpdateAiKnowledgePolicyRequest("internal",embedding.Id,embedding.Version,generation.Id,generation.Version,browserBase.Version));
                        browserBase=await Send<AiKnowledgeBaseResponse>(HttpMethod.Get,browserRoot,browserToken);
                        await Send<AiKnowledgeMembersResponse>(HttpMethod.Put,browserRoot+"/members",browserToken,new SetAiKnowledgeMembersRequest([reader.UserId,browserReviewer!.UserId],browserBase.Version));
                        var browserFaqs=new List<Guid>();
                        foreach(var suffix in new[]{"桌面","移动"})
                        {
                            var draft=await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post,browserRoot+"/faq-drafts/from-documents",browserToken,
                                new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(),faqText with{Question="浏览器完整发布"+suffix,Aliases=[],Answer="通过正式页面审核的答案。"},new(embedding.Id,embedding.Version,sourceSelection.Sources)),HttpStatusCode.OK);
                            browserFaqs.Add(draft.Workspace.Id);
                        }
                        await File.WriteAllTextAsync(Path.Combine(browserBridge!,"ready.json"),JsonSerializer.Serialize(new{username=browserName,reviewerUsername=browserReviewer!.Username,password=FullNetApiFactory.TestPassword,knowledgeBaseId=browserBase.Id,knowledgeName=browserBase.Name,faqIds=browserFaqs,effectiveFromUtc=savedContent.ValidFromUtc,effectiveToUtc=savedContent.ExpiresAtUtc}));
                        await WaitBrowser("advance.json","advance");
                    }
                    Assert.AreEqual("新修订尚未发布。",(await Send<AiKnowledgeFaqPublishedContent>(HttpMethod.Get,publishedPath,readerToken)).Content.Answer);
                    Assert.AreEqual(filesBefore,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_file"));
                    Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_files_tenant_resource_file WHERE ResourceId IN (SELECT Id FROM fn_ai_knowledge_faq_source)"));
                    Assert.AreEqual(3,await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_publication_head WHERE Id=@Id",new{Id=faq.Workspace.ReservedDocumentId}));
                    Assert.AreEqual(finalProposal.Id,await db.QuerySingleAsync<Guid>("SELECT PublicationId FROM fn_ai_knowledge_faq WHERE Id=@Id",new{Id=faq.Workspace.Id}));
                    var afterIndexes=await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");var afterIndexCalls=model.Calls;
                    await Publish(request with { RequestKey=Guid.CreateVersion7(),ExpectedHeadVersion=1 });
                    await Expect(HttpMethod.Get,publishedPath,readerToken,null,HttpStatusCode.Conflict);
                    await Expect(HttpMethod.Get,managed,owner,null,HttpStatusCode.Conflict);
                    await Expect(HttpMethod.Get,managed+"/publications/"+finalProposal.Id,readerToken,null,HttpStatusCode.Conflict);
                    await Expect(HttpMethod.Post,managed+"/index",owner,new IndexAiKnowledgeFaqRevision(Guid.CreateVersion7(),revised.Workspace.Revision.Id,2,true),HttpStatusCode.Conflict);
                    Assert.AreEqual(afterIndexes,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));Assert.AreEqual(afterIndexCalls,model.Calls);
                    if(!tenant&&browserIdentity is not null)
                    {
                        await File.WriteAllTextAsync(Path.Combine(browserBridge!,"replaced.json"),"{}");
                        await WaitBrowser("complete.json","passed");
                    }
                    // 来源失效不能阻止关闭服务；该操作只使用当前所有者与发布身份，不返回正文。
                    await Send<AiKnowledgePublicationHeadResponse>(HttpMethod.Post,managed+"/publications/withdraw",owner,new WithdrawAiKnowledgePublication(3,finalProposal.Id));
                    await Expect(HttpMethod.Get,publishedPath,readerToken,null,HttpStatusCode.NotFound);
                    Assert.AreEqual(0,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq_query WHERE FaqId=@Id",new{Id=faq.Workspace.Id}));
                }
                finally { await faqWorker.StopAsync(); FaqLifecycleQueryTrace.Save(); }
                continue;
                async Task Ready(AiKnowledgeFaqIndexAdmission admission,string target)
                {
                    using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    while(true)
                    {
                        var taskState=await db.QuerySingleAsync<string>("SELECT StateKey FROM fn_ai_knowledge_index_task WHERE Id=@Id",new{Id=admission.TaskId});
                        if(taskState is "failed" or "cancelled")FaqLifecycleQueryTrace.Dump(admission.TaskId);
                        Assert.IsFalse(taskState is "failed" or "cancelled", "Actual FAQ Worker ended as "+taskState);
                        if(taskState=="completed")break;
                        await Task.Delay(150,deadline.Token);
                    }
                    Assert.AreEqual("ready",(await Send<AiKnowledgeFaqLifecycle>(HttpMethod.Get,target+"/lifecycle",owner)).IndexState);
                    Assert.IsTrue(await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id WHERE batch.IndexTaskId=@Id AND ledger.Outcome='succeeded' AND ledger.UsageStatus IN ('known','unknown') AND ledger.SettledAtUtc IS NOT NULL",new{Id=admission.TaskId})>0);
                }
            }
            if (verifyFaqOrdinaryBoundary)
            {
                // 人为建立未来物化/损坏状态：已发布内容和全部旧入口都真实存在，不能依赖找不到普通文档来拒绝。
                var draft = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, root + "/faq-drafts/independent", owner,
                    new CreateAiKnowledgeFaqIndependentDraft(Guid.CreateVersion7(), new("受管标准问题", [], "只能经 FAQ 谱系协议读取。", "制度", null, null), "独立权威依据"));
                var control = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Post, root + "/documents", owner, new CreateAiKnowledgeDocumentRequest("普通对照文档", null), HttpStatusCode.Created);
                var beforeSearch = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                Assert.AreEqual(document.Id, beforeSearch.Sources.Single().DocumentId);
                var derivedBoundaryDraft = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, root + "/faq-drafts/from-documents", owner,
                    new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(), new("来源受管后必须拒读", [], "保留完整文档依赖。", "制度", null, null), new(embedding.Id, embedding.Version, beforeSearch.Sources)));
                var historyIdentity = await SaveHistory();
                Assert.AreEqual("依据人工校对与明确补写", (await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{historyIdentity.SessionId}", readerToken)).Messages.Single().Content);
                var beforeBudget = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"); var beforeCalls = model.Calls;
                var imported = await db.QuerySingleAsync<Guid>("SELECT Id FROM fn_ai_knowledge_import WHERE VersionId=@Id", new { Id = version });
                var importedFile = await db.QuerySingleAsync<Guid>("SELECT FileId FROM fn_ai_knowledge_version WHERE Id=@Id", new { Id = version });
                var docVersion = current.DocumentVersion; knowledge = await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, root, owner);
                var lease = Guid.CreateVersion7(); var binding = new AiKnowledgeIndexTaskBinding(knowledge.Id, document.Id, version, docVersion, knowledge.Version, actor);
                await using var boundaryScope = Scope(); var services = boundaryScope.ServiceProvider;
                var taskStore = new AiKnowledgeIndexTaskStore(services.GetRequiredService<IQueryExecutor>(), services.GetRequiredService<ICommandExecutor>(),
                    services.GetRequiredService<ICurrentTenant>(), services.GetRequiredService<IClock>(), services.GetRequiredService<IIdGenerator>());
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='running',LeaseToken=@Lease,LeaseExpiresAtUtc=@Expiry WHERE Id=@Id", new { Id = task, Lease = lease, Expiry = DateTimeOffset.UtcNow.AddMinutes(5) });
                Assert.IsNotNull(await taskStore.ReadDispatchAsync(binding, task, lease, default), "未绑定 FAQ 时，同一来源必须确实可以派发。");
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='completed',LeaseToken=NULL,LeaseExpiresAtUtc=NULL WHERE Id=@Id", new { Id = task });
                Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_knowledge_faq SET ReservedDocumentId=@DocumentId WHERE Id=@Id", new { Id = draft.Workspace.Id, DocumentId = document.Id }));
                foreach (var uri in new[] { path, path + "/versions", path + $"/versions/{version}/source", path + "/members", path + $"/imports/{imported}", indexing, indexing + "/reconciliation",
                    correction + "/workspace", correction + "/revisions", correction + "/preparations/latest", correction + $"/preparations/{prepared.Id}", publications + "/head", publications + $"/{published.Id}" })
                    await Expect(HttpMethod.Get, uri, owner, null, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Get, path, readerToken, null, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Get, root + "/faq-drafts/" + derivedBoundaryDraft.Workspace.Id, owner, null, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Put, path, owner, new UpdateAiKnowledgeDocumentRequest("旁路修改", null, docVersion), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Delete, path, owner, new DeleteAiKnowledgeDocumentRequest(docVersion), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], docVersion), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, path + $"/imports/{imported}/retry", owner, null, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, path + "/imports/cleanup", owner, null, HttpStatusCode.NotFound);
                using (var attempt = new HttpRequestMessage(HttpMethod.Post, path + $"/versions?version={docVersion}&requestKey={Guid.CreateVersion7()}&fileName=forged.txt") { Content = new ByteArrayContent(sourceBytes) })
                {
                    attempt.Content.Headers.ContentType = new("text/plain"); attempt.Headers.Authorization = new("Bearer", owner);
                    using var response = await client.SendAsync(attempt); await Require(response, HttpStatusCode.NotFound);
                }
                await Expect(HttpMethod.Post, indexing, owner, null, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, indexing + $"/{task}/retry", owner, null, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, indexing + "/rebuild", owner, new AiKnowledgeIndexRebuildRequest(Guid.CreateVersion7(), task, docVersion, knowledge.Version, true), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, correction + "/prepare", owner, new AiKnowledgeCorrectionPrepareRequest(Guid.CreateVersion7(), docVersion), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, publications, owner, request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 }, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, publications + $"/{published.Id}/publish", owner, new PublishAiKnowledgePublication(published.Version, published.SnapshotHash), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, publications + $"/{published.Id}/review", readerToken, new ReviewAiKnowledgePublication(true, published.Version, published.SnapshotHash), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, publications + "/withdraw", owner, new WithdrawAiKnowledgePublication(1, published.Id), HttpStatusCode.NotFound);
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest, HttpStatusCode.Conflict);
                var hiddenHistory = (await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{historyIdentity.SessionId}", readerToken)).Messages.Single();
                Assert.AreEqual("", hiddenHistory.Content); Assert.AreEqual(0, hiddenHistory.Citations.Count); Assert.IsNotNull(hiddenHistory.KnowledgeUnavailableCode);
                var listing = await Send<JsonElement>(HttpMethod.Get, root + "/documents", owner);
                CollectionAssert.AreEqual(new[] { control.Id }, listing.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray());
                var directory = await Send<JsonElement>(HttpMethod.Get, root + "/directory", owner);
                Assert.IsFalse(directory.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == document.Id));
                Assert.AreEqual(control.Id, (await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, root + "/documents/" + control.Id, owner)).Id);
                await Send<AiKnowledgeDocumentResponse>(HttpMethod.Put, root + "/documents/" + control.Id, owner, new UpdateAiKnowledgeDocumentRequest("普通文档仍可编辑", null, control.Version));
                Assert.AreEqual(document.Id, (await Send<AiKnowledgeFaqDraftWorkspace>(HttpMethod.Get, root + "/faq-drafts/" + draft.Workspace.Id, owner)).ReservedDocumentId);
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_index_task SET StateKey='running',LeaseToken=@Lease,LeaseExpiresAtUtc=@Expiry WHERE Id=@Id", new { Id = task, Lease = lease, Expiry = DateTimeOffset.UtcNow.AddMinutes(5) });
                Assert.IsNull(await taskStore.ReadDispatchAsync(binding, task, lease, default), "已领取的普通任务也不能读取受管 FAQ 的派发正文。");
                var query = services.GetRequiredService<IQueryExecutor>(); var parameters = new Dictionary<string, object?> { ["KnowledgeBaseId"] = knowledge.Id, ["DocumentId"] = document.Id, ["MetadataVersion"] = docVersion };
                Assert.IsNull(await query.QuerySingleOrDefaultAsync<Guid?>(AiKnowledgeMetadataSourceSql.For(provider, tenant), parameters));
                Assert.IsTrue(await query.QuerySingleOrDefaultAsync<long>(tenant ? AiKnowledgeImportSql.ReferenceTenant : AiKnowledgeImportSql.ReferenceHost, new Dictionary<string, object?> { ["VersionId"] = version, ["FileId"] = importedFile }) > 0, "拒读不得误撤销仍持有的原文件引用。");
                Assert.AreEqual(beforeCalls, model.Calls); Assert.AreEqual(beforeBudget, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_version WHERE DocumentId=@Id", new { Id = document.Id }));
                Assert.AreEqual(docVersion, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id AND IsDeleted=0", new { Id = document.Id }));
                Assert.AreEqual(published.Id, await db.QuerySingleAsync<Guid>("SELECT PublicationId FROM fn_ai_knowledge_publication_head WHERE Id=@Id", new { Id = document.Id }));
                continue;
            }
            if (verifyFaqDraft)
            {
                var faqRoot = root + "/faq-drafts"; var faqCalls = model.Calls; var budgets = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                var text = new AiKnowledgeFaqContent("标准问题？", ["同义问法？"], "标准答案，尚未发布。", "制度", DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
                var independentRequest = new CreateAiKnowledgeFaqIndependentDraft(Guid.CreateVersion7(), text, "维护者独立编写依据，需要另行审核。");
                var independent = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqRoot + "/independent", owner, independentRequest);
                Assert.AreEqual("independent", independent.Workspace.Origin); Assert.AreEqual("draft", independent.Workspace.State); Assert.AreEqual(0, independent.Workspace.Sources.Count);
                Assert.IsFalse(independent.Replayed); Assert.AreEqual(independent.Workspace.Id, (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqRoot + "/independent", owner, independentRequest)).Workspace.Id);
                await Expect(HttpMethod.Post, faqRoot + "/independent", owner, independentRequest with { Basis = "改变已用请求的依据" }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, faqRoot + "/independent", owner, independentRequest with { RequestKey = Guid.CreateVersion7(), Content = text with { Aliases = [text.Question] } }, HttpStatusCode.BadRequest);
                var maximumContent = new AiKnowledgeFaqContent(new string('问',512), Enumerable.Range(0,16).Select(i=>new string('别',511)+(char)('A'+i)).ToArray(), new string('答',8000), new string('类',64), null, null);
                var maximumDraft = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqRoot + "/independent", owner, independentRequest with { RequestKey = Guid.CreateVersion7(), Content = maximumContent });
                var maximumRead = await Send<AiKnowledgeFaqDraftWorkspace>(HttpMethod.Get, faqRoot + "/" + maximumDraft.Workspace.Id, owner);
                Assert.AreEqual(maximumContent.Answer,maximumRead.Revision.Content.Answer); CollectionAssert.AreEqual(maximumContent.Aliases.ToArray(),maximumRead.Revision.Content.Aliases.ToArray());
                await Expect(HttpMethod.Post, faqRoot + "/independent", readerToken, independentRequest, HttpStatusCode.Forbidden);
                await Expect(HttpMethod.Get, faqRoot + "/" + independent.Workspace.Id, readerToken, null!, HttpStatusCode.NotFound);
                await Expect(HttpMethod.Get, root + "/documents/" + independent.Workspace.ReservedDocumentId, owner, null!, HttpStatusCode.NotFound);
                var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", owner, searchRequest);
                var fromDocuments = new CreateAiKnowledgeFaqDocumentDraft(Guid.CreateVersion7(), text, new(embedding.Id, embedding.Version, found.Sources));
                var derived = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqRoot + "/from-documents", owner, fromDocuments);
                Assert.AreEqual("document_derived", derived.Workspace.Origin); Assert.IsNull(derived.Workspace.Basis); CollectionAssert.AreEqual(found.Sources.ToArray(), derived.Workspace.Sources.ToArray());
                var originalDraft = derived.Workspace; var draftPath = faqRoot + "/" + derived.Workspace.Id;
                var save = new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(), derived.Workspace.Revision.Id, 1, text with { Answer = "人工改写仍必须保留原始文档依赖。" });
                derived = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put, draftPath, owner, save);
                Assert.AreEqual(2, derived.Workspace.HeadVersion); Assert.AreEqual(originalDraft.SourceHash, derived.Workspace.SourceHash); Assert.AreNotEqual(originalDraft.Revision.Id, derived.Workspace.Revision.Id);
                var replay = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put, draftPath, owner, save); Assert.IsTrue(replay.Replayed); Assert.AreEqual(derived.SavedRevisionId, replay.SavedRevisionId);
                await Expect(HttpMethod.Put, draftPath, owner, save with { Content = text }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Put, draftPath, owner, save with { RequestKey = Guid.CreateVersion7() }, HttpStatusCode.Conflict);
                var restore = new RestoreAiKnowledgeFaqDraft(Guid.CreateVersion7(), derived.Workspace.Revision.Id, 2, originalDraft.Revision.Id);
                derived = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, draftPath + "/restore", owner, restore);
                Assert.AreEqual(3, derived.Workspace.HeadVersion); Assert.AreEqual(originalDraft.Revision.Content.Answer, derived.Workspace.Revision.Content.Answer); Assert.AreEqual(originalDraft.SourceHash, derived.Workspace.SourceHash);
                replay = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Put, draftPath, owner, save); Assert.IsTrue(replay.Replayed); Assert.AreEqual(3, replay.Workspace.HeadVersion); Assert.AreNotEqual(save.ExpectedRevisionId, replay.SavedRevisionId);
                var createReplay = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqRoot + "/from-documents", owner, fromDocuments);
                Assert.IsTrue(createReplay.Replayed); Assert.AreEqual(originalDraft.Revision.Id, createReplay.SavedRevisionId); Assert.AreEqual(3, createReplay.Workspace.HeadVersion);
                var copyRequest = new CopyAiKnowledgeFaqDraft(Guid.CreateVersion7(), derived.Workspace.Revision.Id, 3);
                var copied = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, draftPath + "/copy", owner, copyRequest);
                Assert.AreEqual("document_derived", copied.Workspace.Origin); Assert.AreEqual(derived.Workspace.SourceHash, copied.Workspace.SourceHash); Assert.AreNotEqual(derived.Workspace.Id, copied.Workspace.Id);
                Assert.AreEqual(copied.Workspace.Id, (await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, draftPath + "/copy", owner, copyRequest)).Workspace.Id);
                await Expect(HttpMethod.Post, draftPath + "/restore", owner, restore with { RequestKey = Guid.CreateVersion7(), ExpectedRevisionId = derived.Workspace.Revision.Id, ExpectedHeadVersion = 3, RevisionId = independent.Workspace.Revision.Id }, HttpStatusCode.NotFound);
                var racing = new SaveAiKnowledgeFaqDraft(Guid.CreateVersion7(), derived.Workspace.Revision.Id, 3, text with { Answer = "并发修改甲" });
                var racingOther = racing with { RequestKey = Guid.CreateVersion7(), Content = text with { Answer = "并发修改乙" } };
                async Task<HttpStatusCode> Race(SaveAiKnowledgeFaqDraft body)
                { using var req = new HttpRequestMessage(HttpMethod.Put, draftPath) { Content = JsonContent.Create(body) }; req.Headers.Authorization = new("Bearer", owner); using var response = await client.SendAsync(req); return response.StatusCode; }
                var statuses = await Task.WhenAll(Race(racing), Race(racingOther)); CollectionAssert.AreEquivalent(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, statuses);
                var faqHistory = await Send<AiKnowledgeFaqDraftRevisionPage>(HttpMethod.Get, draftPath + "/revisions", owner);
                CollectionAssert.AreEqual(new[] { 4,3,2,1 }, faqHistory.Items.Select(item => item.Number).ToArray()); Assert.AreEqual(4, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_faq_revision WHERE FaqId=@Id", new { Id = derived.Workspace.Id }));
                Assert.AreEqual(budgets, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget")); Assert.AreEqual(faqCalls, model.Calls);
                if (!tenant && browserIdentity is not null)
                {
                    var browserToken = await IntegrationTestAuthHelper.LoginAsHostUserAsync(readerClient, browserName);
                    var browserBase = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", browserToken, new CreateAiKnowledgeBaseRequest("FAQ 草稿管理", null), HttpStatusCode.Created);
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new { username = browserName, password = FullNetApiFactory.TestPassword,
                        knowledgeBaseId = browserBase.Id, knowledgeName = browserBase.Name, sourceKnowledgeBaseId = knowledge.Id, sourceKnowledgeName = knowledge.Name, documentId = document.Id }));
                    await WaitBrowser("advance.json", "advance");
                }
                await Edit("replace", current.Block.Parts[0].Id, "下一份 FAQ 来源发布。"); _ = await Generate(partial: false);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                await Expect(HttpMethod.Get, draftPath, owner, null!, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get, draftPath + "/revisions", owner, null!, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get, faqRoot + "/" + copied.Workspace.Id, owner, null!, HttpStatusCode.Conflict);
                var page = await Send<AiKnowledgeFaqDraftPage>(HttpMethod.Get, faqRoot, owner); Assert.IsTrue(page.Items.All(item => item.Origin == "independent"));
                if (!tenant && browserIdentity is not null) { await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed"); }
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", owner, searchRequest);
                var adopted = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, faqRoot + "/from-documents", owner, fromDocuments with { RequestKey = Guid.CreateVersion7(), Selection = fromDocuments.Selection with { Sources = found.Sources } });
                Assert.AreNotEqual(originalDraft.SourceHash, adopted.Workspace.SourceHash);
                var readerBase = await Send<AiKnowledgeBaseResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-bases", owner, new CreateAiKnowledgeBaseRequest("来源隔离的 FAQ 管理库", null), HttpStatusCode.Created);
                var readerFaqPath = $"/api/v1/ai/knowledge-bases/{readerBase.Id}/faq-drafts";
                var readerDraft = await Send<AiKnowledgeFaqDraftMutation>(HttpMethod.Post, readerFaqPath + "/from-documents", owner, fromDocuments with { RequestKey = Guid.CreateVersion7(), Selection = fromDocuments.Selection with { Sources = found.Sources } });
                // 只在场景数据中移交目标库；来源库所有权不变，验证两套权限必须求交集。
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET OwnerUserId=@Actor WHERE Id=@Id", new { Actor = reader.UserId, Id = readerBase.Id });
                await Send<AiKnowledgeFaqDraftWorkspace>(HttpMethod.Get, readerFaqPath + "/" + readerDraft.Workspace.Id, readerToken);
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
                await Expect(HttpMethod.Get, readerFaqPath + "/" + readerDraft.Workspace.Id, readerToken, null!, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Get, readerFaqPath + "/" + readerDraft.Workspace.Id + "/revisions", readerToken, null!, HttpStatusCode.Conflict);
                Assert.AreEqual(0, (await Send<AiKnowledgeFaqDraftPage>(HttpMethod.Get, readerFaqPath, readerToken)).Items.Count);
                Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_document WHERE Id=@Id", new { Id = adopted.Workspace.ReservedDocumentId }));
                continue;
            }
            if (verifyRelations)
            {
                const string relationsPath = "/api/v1/ai/knowledge-search/relations";
                Assert.IsTrue(relationChildren.Count is >= 1 and <= 4, "必须实际解析出嵌入图片及父锚点。");
                var relationSearch = searchRequest with { Query = "FULLNET", Limit = 20 };
                var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, relationSearch);
                var child = found.Hits.First(hit => hit.Correction!.Spans.Any(span => span.SourceLocation?.Image?.Parent is not null));
                Assert.IsTrue(child.Sequence > 1, "必须跨过无关补写块，不能用邻接窗口代替关系。");
                var relationRequest = new ReadAiKnowledgeSearchRelationsRequest(found.Sources.Single(), embedding.Id, embedding.Version, child.Sequence);
                var before = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"); var beforeModels = model.Calls;
                await Expect(HttpMethod.Post, relationsPath, readerToken, relationRequest with { AnchorSequence = 256 }, HttpStatusCode.BadRequest);
                await Expect(HttpMethod.Post, relationsPath, readerToken, relationRequest with { Source = relationRequest.Source with { PublicationId = Guid.CreateVersion7() } }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, relationsPath, readerToken, relationRequest with { EmbeddingModelVersion = embedding.Version + 1 }, HttpStatusCode.Conflict);
                var related = await Send<AiKnowledgeSearchRelationsResponse>(HttpMethod.Post, relationsPath, readerToken, relationRequest);
                Assert.AreEqual("embedded_image_parent_v1", related.Strategy); Assert.AreEqual("complete", related.RelationState);
                Assert.AreEqual(relationRequest.Source, related.Source); Assert.AreEqual(relationChildren.Count + 1, related.Blocks.Count);
                CollectionAssert.AreEquivalent(relationChildren.ToArray(), related.Relations.Select(edge => edge.ChildLocation.BlockSequence).Distinct().ToArray());
                Assert.IsTrue(related.Relations.All(edge => edge.ParentSequence == 0 && edge.ParentLocation == edge.ChildLocation.Image!.Parent));
                Assert.IsTrue(related.Blocks.All(block => block.Correction is not null && block.Offset is null && block.Length is null));
                var parentRelated = await Send<AiKnowledgeSearchRelationsResponse>(HttpMethod.Post, relationsPath, readerToken, relationRequest with { AnchorSequence = 0 });
                CollectionAssert.AreEqual(related.Relations.ToArray(), parentRelated.Relations.ToArray());
                var unrelated = await Send<AiKnowledgeSearchRelationsResponse>(HttpMethod.Post, relationsPath, readerToken, relationRequest with { AnchorSequence = 1 });
                Assert.AreEqual("none", unrelated.RelationState); Assert.AreEqual(0, unrelated.Relations.Count); Assert.AreEqual(1, unrelated.Blocks.Count);
                Assert.AreEqual(before, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget")); Assert.AreEqual(beforeModels, model.Calls);
                if (!tenant && browserIdentity is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new { username = browserName, password = FullNetApiFactory.TestPassword,
                        knowledgeBaseId = knowledge.Id, knowledgeName = knowledge.Name, documentId = document.Id, original, query = relationSearch.Query, blocks = related.Blocks.Count }));
                    await WaitBrowser("advance.json", "advance");
                }
                await Edit("replace", current.Block.Parts[0].Id, "下一份真实关系父正文。"); _ = await Generate(partial: false);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                var afterPublish = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                await Expect(HttpMethod.Post, relationsPath, readerToken, relationRequest, HttpStatusCode.Conflict);
                if (!tenant && browserIdentity is not null) { await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed"); }
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, relationSearch);
                relationRequest = relationRequest with { Source = found.Sources.Single() };
                var adopted = await Send<AiKnowledgeSearchRelationsResponse>(HttpMethod.Post, relationsPath, readerToken, relationRequest);
                Assert.AreEqual(published.Id, adopted.Source.PublicationId); StringAssert.Contains(adopted.Blocks.Single(block => block.Sequence == 0).Text, "下一份真实关系父正文");
                // 通过实际修订/索引/发布排除父正文；原文锚点存在不表示该正文已进入索引。
                await Edit("exclude", null, null, true); _ = await Generate(partial: false);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 2 });
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, relationSearch);
                child = found.Hits.First(hit => hit.Correction!.Spans.Any(span => span.SourceLocation?.Image?.Parent is not null));
                relationRequest = relationRequest with { Source = found.Sources.Single(), AnchorSequence = child.Sequence };
                afterPublish = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                var missingParent = await Send<AiKnowledgeSearchRelationsResponse>(HttpMethod.Post, relationsPath, readerToken, relationRequest);
                Assert.AreEqual("parent_not_indexed", missingParent.RelationState); Assert.AreEqual(relationChildren.Count, missingParent.Blocks.Count);
                Assert.AreEqual(relationChildren.Count, missingParent.Relations.Count);
                Assert.IsTrue(missingParent.Relations.All(edge => edge.ParentSequence is null));
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
                await Expect(HttpMethod.Post, relationsPath, readerToken, relationRequest, HttpStatusCode.Conflict);
                Assert.AreEqual(afterPublish, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                continue;
            }
            if (verifyRefinement)
            {
                const string refinePath = "/api/v1/ai/knowledge-search/refine";
                var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                var refine = new RefineAiKnowledgeSearchRequest([knowledge.Id], embedding.Id, generation.Id, generation.Version, searchRequest.Query, "rewrite_rerank", 5, false,
                    new([document.Id], embedding.Version, found.Sources));
                var before = await Count();
                await Expect(HttpMethod.Post, refinePath, readerToken, refine with { Query = " " }, HttpStatusCode.BadRequest);
                await Expect(HttpMethod.Post, refinePath, readerToken, refine with { ProcessingModelVersion = generation.Version + 1 }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, refinePath, readerToken, refine with { SearchScope = refine.SearchScope with { Sources = [found.Sources.Single() with { PublicationId = Guid.CreateVersion7() }] } }, HttpStatusCode.Conflict);
                if (!tenant) await Expect(HttpMethod.Post, refinePath, readerToken, refine with { IncludeDiagnostics = true }, HttpStatusCode.Forbidden);
                Assert.AreEqual(before, await Count());
                foreach (var mode in new[] { "rewrite", "rerank", "rewrite_rerank" })
                {
                    var value = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, refinePath, readerToken, refine with { Mode = mode });
                    Assert.AreEqual(refine.Query, value.Query); Assert.AreEqual(mode == "rerank" ? refine.Query : "补写 明确", value.EffectiveQuery);
                    Assert.AreEqual(mode == "rewrite" ? 3 : mode == "rerank" ? 2 : 4, value.Operations.Count);
                    Assert.AreEqual(value.Operations.Count, value.Operations.Select(row => row.OperationId).Distinct().Count());
                    CollectionAssert.AreEqual(found.Sources.ToArray(), value.Sources.ToArray()); Assert.IsNull(value.Diagnostics);
                    Assert.AreEqual(5, value.OriginalKeyword.Hits.Count); Assert.AreEqual(5, value.OriginalVector.Hits.Count);
                    Assert.AreEqual("keyword_occurrences", value.OriginalKeyword.ScoreKind); Assert.AreEqual("cosine_similarity", value.OriginalVector.ScoreKind);
                    if (mode != "rewrite") { Assert.AreEqual(8, value.RerankCandidateCount); Assert.AreEqual("ordinal_priority", value.Reranked!.ScoreKind); Assert.AreEqual(8d, value.Reranked.Hits[0].Score); }
                    foreach (var receipt in value.Operations)
                    {
                        Assert.AreEqual("reported", receipt.Status);
                        Assert.AreEqual("succeeded", await db.QuerySingleAsync<string>("SELECT Outcome FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = receipt.OperationId }));
                    }
                }
                Assert.AreEqual(before + 9, await Count());
                SetFault(null, unknown: true);
                var unknown = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, refinePath, readerToken, refine);
                Assert.IsTrue(unknown.Operations.All(row => row.Status == "unknown"));
                foreach (var operation in unknown.Operations) Assert.AreEqual("unknown", await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = operation.OperationId }));
                SetFault("rewrite");
                var refused = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, refinePath, readerToken, refine);
                Assert.AreEqual("rewrite", refused.RefusedStage); Assert.AreEqual(2, refused.Operations.Count); Assert.AreEqual(refine.Query, refused.EffectiveQuery); Assert.IsNull(refused.Reranked);
                SetFault("rerank");
                refused = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, refinePath, readerToken, refine);
                Assert.AreEqual("rerank", refused.RefusedStage); Assert.AreEqual(4, refused.Operations.Count); Assert.IsNull(refused.Reranked);
                foreach (var fault in new[] { "malformed", "reject" })
                {
                    SetFault(fault); var refinementCalls = answerModelServer?.ChatCalls ?? refinementModel.ChatCalls; var costs = await Count();
                    await Expect(HttpMethod.Post, refinePath, readerToken, refine, HttpStatusCode.Conflict);
                    Assert.AreEqual(refinementCalls + 1, answerModelServer?.ChatCalls ?? refinementModel.ChatCalls, "协议或 503 失败不得重试收费聊天。");
                    Assert.AreEqual(costs + 2, await Count());
                }
                SetFault(null);
                var diagnostic = await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, refinePath, owner, refine with { IncludeDiagnostics = true });
                Assert.AreEqual(17, diagnostic.Diagnostics!.CandidateCount); Assert.AreEqual(1, diagnostic.Diagnostics.SourceCount);
                if (!tenant && browserIdentity is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new { username = browserName, password = FullNetApiFactory.TestPassword,
                        knowledgeBaseId = knowledge.Id, knowledgeName = knowledge.Name, documentId = document.Id, original = Encoding.UTF8.GetString(sourceBytes) }));
                    await WaitBrowser("advance.json", "advance");
                }
                await Edit("replace", current.Block.Parts[0].Id, "下一份实验发布。"); _ = await Generate(partial: false);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                var afterPublish = await Count(); await Expect(HttpMethod.Post, refinePath, readerToken, refine, HttpStatusCode.Conflict); Assert.AreEqual(afterPublish, await Count());
                if (!tenant && browserIdentity is not null) { await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed"); }
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                refine = refine with { SearchScope = refine.SearchScope with { Sources = found.Sources } };
                Assert.AreEqual(published.Id, (await Send<AiKnowledgeSearchRefinementResponse>(HttpMethod.Post, refinePath, readerToken, refine)).Sources.Single().PublicationId);
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
                var revoked = await Count(); await Expect(HttpMethod.Post, refinePath, readerToken, refine, HttpStatusCode.Conflict); Assert.AreEqual(revoked, await Count());
                Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_chat_message"));
                continue;
                Task<int> Count() => db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                void SetFault(string? fault, bool unknown = false)
                {
                    if (answerModelServer is not null) { answerModelServer.RefinementFault = fault; answerModelServer.OmitChatUsage = unknown; answerModelServer.OmitEmbeddingUsage = unknown; }
                    else { refinementModel.Fault = fault; refinementModel.OmitUsage = unknown; model.InputTokens = unknown ? null : 7; }
                }
            }
            if (verifyComparison)
            {
                const string comparePath = "/api/v1/ai/knowledge-search/compare";
                var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                var compare = new CompareAiKnowledgeSearchRequest([knowledge.Id], embedding.Id, searchRequest.Query, 2, false,
                    new([document.Id], embedding.Version, found.Sources));
                var before = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                await Expect(HttpMethod.Post, comparePath, readerToken, compare with { Query = " " }, HttpStatusCode.BadRequest);
                await Expect(HttpMethod.Post, comparePath, readerToken, compare with { SearchScope = compare.SearchScope with { EmbeddingModelVersion = embedding.Version + 1 } }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, comparePath, readerToken, compare with { SearchScope = compare.SearchScope with { Sources = [found.Sources.Single() with { PublicationId = Guid.CreateVersion7() }] } }, HttpStatusCode.Conflict);
                if (!tenant) await Expect(HttpMethod.Post, comparePath, readerToken, compare with { IncludeDiagnostics = true }, HttpStatusCode.Forbidden);
                Assert.AreEqual(before, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                if (answerModelServer is not null) answerModelServer.OmitEmbeddingUsage = true; else model.InputTokens = null;
                var unknown = await Send<AiKnowledgeSearchComparisonResponse>(HttpMethod.Post, comparePath, readerToken, compare);
                Assert.AreEqual("unknown", unknown.Usage.Status); Assert.AreEqual("unknown", await db.QuerySingleAsync<string>("SELECT UsageStatus FROM fn_ai_operation_budget WHERE Id=@Id", new { Id = unknown.Usage.OperationId }));
                if (answerModelServer is not null) answerModelServer.OmitEmbeddingUsage = false; else model.InputTokens = 7;
                var compared = await Send<AiKnowledgeSearchComparisonResponse>(HttpMethod.Post, comparePath, readerToken, compare);
                Assert.AreEqual("same_candidates_v1", compared.Strategy); Assert.AreEqual(compare.Query, compared.Query); Assert.AreEqual("reported", compared.Usage.Status);
                CollectionAssert.AreEqual(found.Sources.ToArray(), compared.Sources.ToArray()); Assert.IsNull(compared.Diagnostics);
                Assert.AreEqual("keyword_occurrences", compared.Keyword.ScoreKind); Assert.AreEqual("cosine_similarity", compared.Vector.ScoreKind);
                Assert.AreEqual(2, compared.Keyword.Hits.Count); Assert.AreEqual(2, compared.Vector.Hits.Count); Assert.IsTrue(compared.Keyword.HasMore); Assert.IsTrue(compared.Vector.HasMore);
                Assert.IsNotNull(compared.Vector.Hits[0].Correction); Assert.IsNull(compared.Vector.Hits[0].Offset); Assert.IsNull(compared.Vector.Hits[0].Length);
                Assert.AreEqual(before + 2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                var failedBefore = answerModelServer?.EmbeddingInputs.Count ?? model.Calls;
                if (answerModelServer is not null) answerModelServer.RejectEmbeddings = true; else model.Generate = () => Task.FromException(new HttpRequestException("受控模型失败"));
                await Expect(HttpMethod.Post, comparePath, readerToken, compare, HttpStatusCode.Conflict);
                Assert.AreEqual(failedBefore + 1, answerModelServer?.EmbeddingInputs.Count ?? model.Calls, "单次比较失败不能自动重试查询向量。");
                if (answerModelServer is not null) answerModelServer.RejectEmbeddings = false; else model.Generate = null;
                Assert.AreEqual(before + 3, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                var diagnostic = await Send<AiKnowledgeSearchComparisonResponse>(HttpMethod.Post, comparePath, owner, compare with { IncludeDiagnostics = true });
                Assert.AreEqual(17, diagnostic.Diagnostics!.CandidateCount); Assert.AreEqual(1, diagnostic.Diagnostics.SourceCount);
                if (!tenant && browserIdentity is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new { username = browserName, password = FullNetApiFactory.TestPassword,
                        knowledgeBaseId = knowledge.Id, knowledgeName = knowledge.Name, documentId = document.Id, original = Encoding.UTF8.GetString(sourceBytes) }));
                    await WaitBrowser("advance.json", "advance");
                }
                await Edit("replace", current.Block.Parts[0].Id, "下一份比较发布。"); _ = await Generate(partial: false);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                var afterPublish = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                await Expect(HttpMethod.Post, comparePath, readerToken, compare, HttpStatusCode.Conflict);
                Assert.AreEqual(afterPublish, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                if (!tenant && browserIdentity is not null) { await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed"); }
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                compare = compare with { SearchScope = compare.SearchScope with { Sources = found.Sources } };
                Assert.AreEqual(published.Id, (await Send<AiKnowledgeSearchComparisonResponse>(HttpMethod.Post, comparePath, readerToken, compare)).Sources.Single().PublicationId);
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
                var beforeRevoke = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                await Expect(HttpMethod.Post, comparePath, readerToken, compare, HttpStatusCode.Conflict);
                Assert.AreEqual(beforeRevoke, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                if (answerModelServer is not null) { Assert.AreEqual(compare.Query, answerModelServer.EmbeddingInputs.Last().Single()); Assert.AreEqual(0, answerModelServer.ChatCalls); }
                continue;
            }
            if (verifySearchAnswer)
            {
                var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                var ask = new AskAiKnowledgeRequest([knowledge.Id], embedding.Id, searchRequest.Query,
                    new([document.Id], embedding.Version, found.Sources));
                var chat = await Send<AiChatSessionResponse>(HttpMethod.Post, "/api/v1/ai/chat/sessions", readerToken,
                    new CreateAiChatSessionRequest(generation.Id, "冻结搜索问答"), HttpStatusCode.Created);
                var askPath = $"/api/v1/ai/chat/sessions/{chat.Id}/knowledge/ask";
                var before = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                await Expect(HttpMethod.Post, askPath, readerToken, ask with { SearchScope = ask.SearchScope! with { EmbeddingModelVersion = embedding.Version + 1 } }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, askPath, readerToken, ask with { SearchScope = ask.SearchScope! with { Sources = [found.Sources.Single() with { PublicationId = Guid.CreateVersion7() }] } }, HttpStatusCode.Conflict);
                Assert.AreEqual(before, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                var answer = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, askPath, readerToken, ask);
                Assert.IsFalse(answer.Refused); Assert.IsTrue(answer.Message.IsKnowledgeAnswer); Assert.AreEqual(1, answer.Message.Citations.Count);
                Assert.AreEqual(document.Id, answer.Message.Citations.Single().DocumentId); Assert.AreEqual(revision, answer.Message.Citations.Single().Correction!.RevisionId);
                var reloaded = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chat.Id}", readerToken);
                Assert.AreEqual(answer.Message.Content, reloaded.Messages.Single(row => row.Id == answer.Message.Id).Content);
                Assert.AreEqual(before + 2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                Assert.AreEqual(0, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget WHERE SettledAtUtc IS NULL"));
                if (browserIdentity is not null)
                {
                    var browserChat = Guid.CreateVersion7();
                    await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,OwnerUserId,ModelConfigId,ModelName,Title,CreatedAtUtc) VALUES (@Id,@Owner,@Model,'gpt-4.1-mini','冻结搜索问答页面',@Now)",
                        new { Id = browserChat, Owner = browserIdentity.UserId, Model = generation.Id, Now = DateTimeOffset.UtcNow });
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new { username = browserName, password = FullNetApiFactory.TestPassword,
                        knowledgeBaseId = knowledge.Id, knowledgeName = knowledge.Name, documentId = document.Id, original = Encoding.UTF8.GetString(sourceBytes), sessionId = browserChat }));
                    await WaitBrowser("advance.json", "advance");
                }
                await Edit("replace", current.Block.Parts[0].Id, "下一份问答发布。"); _ = await Generate(partial: false);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                using (var staleRequest = new HttpRequestMessage(HttpMethod.Post, askPath) { Content = JsonContent.Create(ask) })
                {
                    staleRequest.Headers.Authorization = new("Bearer", readerToken); using var stale = await client.SendAsync(staleRequest);
                    await Require(stale, HttpStatusCode.Conflict); using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
                    Assert.AreEqual(AiKnowledgeErrorCodes.SearchSourceChanged, problem.RootElement.GetProperty("code").GetString());
                }
                reloaded = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chat.Id}", readerToken);
                Assert.AreEqual("", reloaded.Messages.Single(row => row.Id == answer.Message.Id).Content);
                Assert.AreEqual(0, reloaded.Messages.Single(row => row.Id == answer.Message.Id).Citations.Count);
                if (browserIdentity is not null) { await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed"); }
                var afterReplace = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                ask = ask with { SearchScope = ask.SearchScope! with { Sources = found.Sources } };
                answer = await Send<AskAiKnowledgeResponse>(HttpMethod.Post, askPath, readerToken, ask);
                Assert.AreEqual(afterReplace + 2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
                reloaded = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{chat.Id}", readerToken);
                Assert.AreEqual("", reloaded.Messages.Single(row => row.Id == answer.Message.Id).Content);
                Assert.IsNotNull(reloaded.Messages.Single(row => row.Id == answer.Message.Id).KnowledgeUnavailableCode);
                continue;
            }
            if (searchPolicyOnly)
            {
                var budgetsBefore = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                foreach (var (token, diagnostics) in new[] { (owner, true), (readerToken, false) })
                {
                    using var deniedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/knowledge-search")
                    { Content = JsonContent.Create(searchRequest with { IncludeDiagnostics = diagnostics }) };
                    deniedRequest.Headers.Authorization = new("Bearer", token);
                    using var denied = await client.SendAsync(deniedRequest); await Require(denied, HttpStatusCode.Conflict);
                    Assert.AreEqual("application/problem+json", denied.Content.Headers.ContentType?.MediaType);
                    using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
                    Assert.AreEqual(AiKnowledgeErrorCodes.SearchUnavailable, problem.RootElement.GetProperty("code").GetString());
                    Assert.AreEqual(409, problem.RootElement.GetProperty("status").GetInt32());
                    Assert.IsFalse(problem.RootElement.TryGetProperty("hits", out _)); Assert.IsFalse(problem.RootElement.TryGetProperty("diagnostics", out _));
                    Assert.IsFalse(problem.RootElement.ToString().Contains("明确人工补写的说明", StringComparison.Ordinal));
                }
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { Query = " " }, HttpStatusCode.BadRequest);
                if (!tenant) await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { IncludeDiagnostics = true }, HttpStatusCode.Forbidden);
                Assert.AreEqual(budgetsBefore, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                Assert.AreEqual(calls + 2, model.Calls, "宿主拒绝不能触发模型派发或产生新预算。");
                // 本用例只覆盖新增拒绝路径，不重复已通过的替换发布、历史与浏览器矩阵。
                continue;
            }
            if (verifyContext)
            {
                var found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                var contextRequest = new ReadAiKnowledgeSearchContextRequest(found.Sources.Single(), embedding.Id, embedding.Version, 0, 1);
                const string contextPath = "/api/v1/ai/knowledge-search/context";
                var budgetsBefore = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                var window = await Send<AiKnowledgeSearchContextResponse>(HttpMethod.Post, contextPath, readerToken, contextRequest);
                Assert.AreEqual("adjacent_sequence_v1", window.Strategy); Assert.AreEqual(contextRequest.Source, window.Source);
                CollectionAssert.AreEqual(new[] { 0, 1 }, window.Blocks.Select(block => block.Sequence).ToArray());
                Assert.IsFalse(window.HasPreviousBlocks); Assert.IsTrue(window.HasNextBlocks); Assert.IsNotNull(window.Blocks[0].Correction);
                Assert.IsNull(window.Blocks[0].Offset); Assert.IsNull(window.Blocks[0].Length);
                Assert.AreEqual("supplement", window.Blocks[1].Correction!.Spans.Single().ContentKind);
                var middle = await Send<AiKnowledgeSearchContextResponse>(HttpMethod.Post, contextPath, readerToken, contextRequest with { AnchorSequence = 8, Radius = 2 });
                CollectionAssert.AreEqual(new[] { 6, 7, 8, 9, 10 }, middle.Blocks.Select(block => block.Sequence).ToArray());
                var end = await Send<AiKnowledgeSearchContextResponse>(HttpMethod.Post, contextPath, readerToken, contextRequest with { AnchorSequence = 16, Radius = 2 });
                CollectionAssert.AreEqual(new[] { 14, 15, 16 }, end.Blocks.Select(block => block.Sequence).ToArray()); Assert.IsFalse(end.HasNextBlocks);
                await Expect(HttpMethod.Post, contextPath, readerToken, contextRequest with { Radius = 3 }, HttpStatusCode.BadRequest);
                await Expect(HttpMethod.Post, contextPath, readerToken, contextRequest with { AnchorSequence = 255 }, HttpStatusCode.Conflict);
                Assert.AreEqual(budgetsBefore, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget")); Assert.AreEqual(calls + 2, model.Calls);
                if (!tenant && browserIdentity is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new
                    { username = browserName, password = FullNetApiFactory.TestPassword, knowledgeBaseId = knowledge.Id, knowledgeName = knowledge.Name, original = Encoding.UTF8.GetString(sourceBytes) }));
                    await WaitBrowser("advance.json", "advance");
                }
                await Edit("replace", current.Block.Parts[0].Id, "下一份人工草稿。");
                _ = await Generate(partial: false);
                Assert.AreEqual(contextRequest.Source, (await Send<AiKnowledgeSearchContextResponse>(HttpMethod.Post, contextPath, readerToken, contextRequest)).Source);
                published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
                if (!tenant && browserIdentity is not null)
                {
                    await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), "{}"); await WaitBrowser("complete.json", "passed");
                }
                using (var staleRequest = new HttpRequestMessage(HttpMethod.Post, contextPath) { Content = JsonContent.Create(contextRequest) })
                {
                    staleRequest.Headers.Authorization = new("Bearer", readerToken); using var stale = await client.SendAsync(staleRequest);
                    await Require(stale, HttpStatusCode.Conflict); using var problem = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
                    Assert.AreEqual(AiKnowledgeErrorCodes.SearchSourceChanged, problem.RootElement.GetProperty("code").GetString());
                }
                found = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                contextRequest = contextRequest with { Source = found.Sources.Single() };
                Assert.AreEqual(published.Id, (await Send<AiKnowledgeSearchContextResponse>(HttpMethod.Post, contextPath, readerToken, contextRequest)).Source.PublicationId);
                document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
                await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
                await Expect(HttpMethod.Post, contextPath, readerToken, contextRequest, HttpStatusCode.Conflict);
                Assert.AreEqual(calls + 4, model.Calls); continue;
            }
            if (!tenant && browserIdentity is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(browserBridge!, "ready.json"), JsonSerializer.Serialize(new
                { username = browserName, password = FullNetApiFactory.TestPassword, knowledgeBaseId = knowledge.Id, knowledgeName = knowledge.Name,
                    documentId = document.Id, original = Encoding.UTF8.GetString(sourceBytes), apiLog = api!.LogFilePath }));
                await WaitBrowser("advance.json", "advance");
            }
            AiKnowledgeSearchResponse? firstSearch = null;
            if (verifySearch)
            {
                firstSearch = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                Assert.AreEqual("keyword_all_v1", firstSearch.Strategy); Assert.AreEqual(17, firstSearch.TotalMatches); Assert.AreEqual(2, firstSearch.Hits.Count);
                Assert.IsNull(firstSearch.Diagnostics); Assert.AreEqual(published.Id, firstSearch.Sources.Single().PublicationId);
                Assert.AreEqual("人工原生原文", firstSearch.Hits[0].DocumentTitle); Assert.IsNotNull(firstSearch.Hits[0].Correction);
                Assert.IsNull(firstSearch.Hits[0].Offset); Assert.IsNull(firstSearch.Hits[0].Length);
                var diagnostics = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", owner, searchRequest with { IncludeDiagnostics = true });
                Assert.AreEqual(17, diagnostics.Diagnostics!.CandidateCount);
                // Host 受限角色无诊断权；租户 Admin 使用其正式角色授权，不伪装为受限读者。
                if (!tenant) await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { IncludeDiagnostics = true }, HttpStatusCode.Forbidden);
                Assert.AreEqual(0, (await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken,
                    searchRequest with { Query = "确定不存在的词项" })).TotalMatches);
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { DocumentIds = [Guid.CreateVersion7()] }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { KnowledgeBaseIds = [Guid.CreateVersion7()] }, HttpStatusCode.Conflict);
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { Query = " " }, HttpStatusCode.BadRequest);
                Assert.AreEqual(calls + 2, model.Calls, "独立搜索不派发模型或创建新的付费向量。");
            }
            var saved = await SaveHistory();
            var history = await History(); Assert.AreEqual("依据人工校对与明确补写", history.Content);
            var cite = history.Citations.Single(); Assert.IsNull(cite.Offset); Assert.IsNull(cite.Length); Assert.IsNull(cite.SourceLocation);
            Assert.AreEqual(revision, cite.Correction!.RevisionId); Assert.AreEqual(2, cite.Correction.Spans.Count);
            Assert.AreEqual("corrected", cite.Correction.Spans[0].ContentKind); Assert.AreEqual(original.Length, cite.Correction.Spans[0].SourceLength);
            Assert.AreEqual(0, cite.Correction.Spans[0].SourceOffset); Assert.IsNotNull(cite.Correction.Spans[0].SourceLocation);
            if (format == "csv") Assert.IsNotNull(cite.Correction.Spans[0].SourceLocation!.Grid);
            if (format == "png") Assert.IsNotNull(cite.Correction.Spans[0].SourceLocation!.Image);
            Assert.AreEqual("supplement", cite.Correction.Spans[1].ContentKind); Assert.IsNull(cite.Correction.Spans[1].SourceLocation);
            Assert.AreEqual(calls + 2, model.Calls, "召回、答案落库及 Native 历史不派发向量或生成模型。");

            // 结构合法但偏移伪造的单条消息不能借同批合法来源交付正文。
            var stored = await db.QuerySingleAsync<string>("SELECT KnowledgeLineage FROM fn_ai_chat_message WHERE Id=@Id", new { Id = saved.MessageId });
            var lineage = AiKnowledgeAnswerLineageCodec.Decode(stored, saved.MessageId, saved.SessionId, reader.UserId, tenantId); Assert.AreEqual(4, lineage.Version);
            var badId = Guid.CreateVersion7(); var reference = lineage.References.Single();
            var forged = reference with { Correction = reference.Correction! with { Spans = reference.Correction!.Spans.Select((s, i) => i == 0 ? s with { SourceOffset = s.SourceOffset + 1 } : s).ToArray() } };
            await db.ExecuteAsync("INSERT INTO fn_ai_chat_message (Id,SessionId,RoleKey,Content,StatusKey,CreatedAtUtc,IsKnowledgeAnswer,KnowledgeLineage) VALUES (@Id,@SessionId,'assistant','坏引用不得交付','completed',@Now,1,@Lineage)",
                new { Id = badId, saved.SessionId, Now = DateTimeOffset.UtcNow, Lineage = AiKnowledgeAnswerLineageCodec.Encode(lineage with { MessageId = badId, References = [forged] }) });
            var batch = await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{saved.SessionId}", readerToken);
            Assert.AreEqual("", batch.Messages.Single(m => m.Id == badId).Content); Assert.IsNotNull(batch.Messages.Single(m => m.Id == badId).KnowledgeUnavailableCode);
            Assert.AreEqual("依据人工校对与明确补写", batch.Messages.Single(m => m.Id == saved.MessageId).Content);

            await Edit("replace", current.Block.Parts[0].Id, "下一份人工草稿。");
            Assert.AreEqual("依据人工校对与明确补写", (await History()).Content, "新草稿不能使旧有效发布的人工引用失效。");
            var nextTask = await Generate(partial: false); Assert.AreNotEqual(task, nextTask); Assert.AreEqual(calls + 4, model.Calls);
            if (verifySearch)
                Assert.AreEqual(firstSearch!.Sources.Single(), (await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken,
                    searchRequest with { ExpectedSources = firstSearch.Sources })).Sources.Single(), "未发布候选不能改变搜索来源。");
            Assert.AreEqual("依据人工校对与明确补写", (await History()).Content, "新完整候选仍不是正式发布。");
            published = await Publish(request with { RequestKey = Guid.CreateVersion7(), ExpectedHeadVersion = 1 });
            if (!tenant && browserIdentity is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(browserBridge!, "replaced.json"), JsonSerializer.Serialize(new { publicationId = published.Id }));
                await WaitBrowser("complete.json", "passed");
            }
            Assert.AreNotEqual(firstPublication.Id, published.Id); Assert.AreNotEqual(firstPublication.SnapshotHash, published.SnapshotHash);
            if (verifySearch)
            {
                await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest with { ExpectedSources = firstSearch!.Sources }, HttpStatusCode.Conflict);
                var adopted = await Send<AiKnowledgeSearchResponse>(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest);
                Assert.AreEqual(published.Id, adopted.Sources.Single().PublicationId);
            }
            Assert.AreEqual("", (await History()).Content, "替换发布身份后，旧历史不能自动改用新人工修订。");
            saved = await SaveHistory(); Assert.AreEqual("依据人工校对与明确补写", (await History()).Content);
            Assert.AreEqual(current.RevisionId, (await History()).Citations.Single().Correction!.RevisionId);
            document = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner);
            await Send<AiKnowledgeDocumentMembersResponse>(HttpMethod.Put, path + "/members", owner, new SetAiKnowledgeDocumentMembersRequest([], document.Version));
            Assert.AreEqual("", (await History()).Content, "当前读者撤权后 Native 必须隐藏正文与全部引用。");
            Assert.AreEqual(0, (await History()).Citations.Count); Assert.AreEqual(calls + 4, model.Calls);
            if (verifySearch) await Expect(HttpMethod.Post, "/api/v1/ai/knowledge-search", readerToken, searchRequest, HttpStatusCode.Conflict);
            using var sourceRequest = new HttpRequestMessage(HttpMethod.Get, path + $"/versions/{version}/source"); sourceRequest.Headers.Authorization = new("Bearer", owner);
            using var sourceResponse = await client.SendAsync(sourceRequest); await Require(sourceResponse, HttpStatusCode.OK); CollectionAssert.AreEqual(sourceBytes, await sourceResponse.Content.ReadAsByteArrayAsync());
            Console.WriteLine(JsonSerializer.Serialize(new { nativeHuman = native, provider = provider.ToString(), tenant, format, task, nextTask, revision, modelCalls = model.Calls - calls }));

            async Task<AiKnowledgeCorrectionWorkspaceResponse> Workspace(int block = 0) => await Send<AiKnowledgeCorrectionWorkspaceResponse>(HttpMethod.Get, correction + "/workspace?block=" + block, owner);
            async Task Edit(string kind, Guid? part, string? text, bool? excluded = null)
            {
                await Send<AiKnowledgeCorrectionRevisionResponse>(HttpMethod.Post, correction + "/revisions", owner,
                    new AiKnowledgeCorrectionEditRequest(Guid.CreateVersion7(), new(current.DocumentVersion, current.SourceId, current.RevisionId, current.HeadVersion, current.SourceHash, current.PayloadHash), "原生人工候选", new(kind, current.Block.Id, part, text, null, excluded)));
                current = await Workspace(current.BlockIndex);
            }
            AsyncServiceScope Scope()
            {
                var scope = factory.Services.CreateAsyncScope(); var writer = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
                if (tenantId is { } id) writer.SetTenant(new(id, "acme", "原生人工租户")); else writer.SetHost(); return scope;
            }
            async Task<Guid> Generate(bool partial)
            {
                var taskId = (await Send<JsonElement>(HttpMethod.Post, indexing, owner, null, HttpStatusCode.Accepted)).GetProperty("taskId").GetGuid();
                var doc = await Send<AiKnowledgeDocumentResponse>(HttpMethod.Get, path, owner); var kb = await Send<AiKnowledgeBaseResponse>(HttpMethod.Get, root, owner);
                var binding = new AiKnowledgeIndexTaskBinding(knowledge.Id, document.Id, version, doc.Version, kb.Version, actor);
                await using var scope = Scope(); var sp = scope.ServiceProvider;
                var held = await sp.GetRequiredService<AiKnowledgeIndexTaskStore>().AcquireAsync(binding, taskId, default, 180); Assert.IsNotNull(held?.LeaseToken);
                var execution = sp.GetRequiredService<AiKnowledgeCorrectionPersistenceService>();
                if (partial)
                {
                    Assert.IsTrue(await execution.ExecuteBatchAsync(binding, taskId, held.LeaseToken.Value, 0, default));
                    if (verifyEvaluationSources)
                    {
                        // 收紧重复核证不能采用旧内存授权：原清单损坏和目的地变化仍必须在收费前拒绝。
                        var planning = sp.GetRequiredService<AiKnowledgeCorrectionIndexService>();
                        var frozenPlan = await planning.ReadAsync(binding, taskId, held.LeaseToken.Value, default);
                        Assert.IsNotNull(frozenPlan);
                        var priorBudget = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget");
                        var priorCalls = model.Calls;
                        var originalPayload = await db.QuerySingleAsync<byte[]>("SELECT Payload FROM fn_ai_knowledge_document_manifest WHERE Id=@Id", new { Id=taskId });
                        var corruptedPayload = originalPayload.ToArray(); corruptedPayload[^1] ^= 1;
                        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document_manifest SET Payload=@Payload WHERE Id=@Id", new { Payload=corruptedPayload, Id=taskId });
                        try { Assert.IsNull(await planning.ReadCurrentModelAsync(binding, taskId, held.LeaseToken.Value, frozenPlan, default)); }
                        finally { await db.ExecuteAsync("UPDATE fn_ai_knowledge_document_manifest SET Payload=@Payload WHERE Id=@Id", new { Payload=originalPayload, Id=taskId }); }
                        var originalEndpoint = await db.QuerySingleAsync<string>("SELECT EndpointBaseUrl FROM fn_ai_model_config WHERE Id=@Id", new { Id=embedding.Id });
                        await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id", new { Endpoint=originalEndpoint+"/evaluation-proof-changed", Id=embedding.Id });
                        try { Assert.IsNull(await planning.ReadCurrentModelAsync(binding, taskId, held.LeaseToken.Value, frozenPlan, default)); }
                        finally { await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id", new { Endpoint=originalEndpoint, Id=embedding.Id }); }
                        Assert.IsNotNull(await planning.ReadCurrentModelAsync(binding, taskId, held.LeaseToken.Value, frozenPlan, default));
                        Assert.AreEqual(priorBudget,await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_operation_budget"));
                        Assert.AreEqual(priorCalls,model.Calls);
                    }
                    await Expect(HttpMethod.Post, publications, owner, new RequestAiKnowledgePublication(Guid.CreateVersion7(), version, 0, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)), HttpStatusCode.Conflict);
                }
                Assert.IsTrue(await execution.ExecuteGenerationAsync(binding, taskId, held.LeaseToken.Value, default));
                var beforeReplay = model.Calls; Assert.IsTrue(await execution.ExecuteGenerationAsync(binding, taskId, held.LeaseToken.Value, default)); Assert.AreEqual(beforeReplay, model.Calls);
                Assert.AreEqual("ready", (await Send<AiKnowledgeIndexStatusResponse>(HttpMethod.Get, indexing, owner)).State);
                Assert.AreEqual(verifyPersonalScheduleAnswer ? 1 : 2, await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM fn_ai_knowledge_index_batch AS batch INNER JOIN fn_ai_operation_budget AS ledger ON ledger.Id=batch.Id WHERE batch.IndexTaskId=@Id AND ledger.Outcome='succeeded' AND ledger.SettledAtUtc IS NOT NULL", new { Id = taskId }));
                return taskId;
            }
            async Task<AiKnowledgePublicationResponse> Publish(RequestAiKnowledgePublication intent)
            {
                var proposal = await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications, owner, intent, HttpStatusCode.Created);
                await Expect(HttpMethod.Post, publications + $"/{proposal.Id}/review", owner, new ReviewAiKnowledgePublication(true, proposal.Version, proposal.SnapshotHash), HttpStatusCode.Conflict);
                proposal = await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications + $"/{proposal.Id}/review", readerToken, new ReviewAiKnowledgePublication(true, proposal.Version, proposal.SnapshotHash));
                return await Send<AiKnowledgePublicationResponse>(HttpMethod.Post, publications + $"/{proposal.Id}/publish", owner, new PublishAiKnowledgePublication(proposal.Version, proposal.SnapshotHash));
            }
            async Task<(Guid SessionId, Guid MessageId)> SaveHistory(AiKnowledgeSearchSource? faqSource = null, IReadOnlyList<Guid>? personalScheduleIds = null)
            {
                await using var scope = Scope(); var sp = scope.ServiceProvider; var query = sp.GetRequiredService<IQueryExecutor>();
                var config = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tenant ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat, AiSqlParameters.Create(("ModelConfigId", embedding.Id)), default); Assert.IsNotNull(config);
                var accessor = sp.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
                // 正式续取会撤销旧会话；准备历史时跟随当前读者令牌，不能复用作用域开始时的旧身份快照。
                using var currentReadClaims = Payload(readerToken);
                accessor.HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(currentReadClaims.RootElement.EnumerateObject().SelectMany(p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(v => new Claim(p.Name, v.ToString())) : [new Claim(p.Name, p.Value.ToString())]), "Bearer")) };
                try
                {
                    var vector = new float[1536]; vector[0] = 1; var pipeline = new AiKnowledgeAnswerPipeline(sp.GetRequiredService<AiKnowledgeRetrievalService>(), sp.GetRequiredService<IIdGenerator>());
                    var personalSources = personalScheduleIds is null ? null : await sp.GetRequiredService<AiPersonalScheduleSourceReader>().CaptureAsync(reader.UserId, personalScheduleIds, default);
                    if (personalScheduleIds is not null) Assert.IsNotNull(personalSources);
                    var preparedAnswer = await pipeline.PrepareAsync([faqSource?.KnowledgeBaseId ?? knowledge.Id], reader.UserId, config, vector,
                        faqSource is null ? "说明人工校对和补写的区别" : "标准", default,
                        searchScope: faqSource is null ? null : new([faqSource.DocumentId], embedding.Version, [faqSource]), schedules: personalSources);
                    var citation = preparedAnswer.Context.Citations.First();
                    if (faqSource is null) Assert.AreEqual(2, citation.Chunk.Correction!.Spans.Count);
                    else Assert.AreEqual(faqSource.DocumentId, citation.Chunk.Source.DocumentId);
                    var accepted = await pipeline.AcceptAsync(preparedAnswer, Encoding.UTF8.GetBytes($$"""{"answer":"依据人工校对与明确补写","citationIds":["{{citation.Id}}"],"refusal":false}"""), default);
                    var sessionId = Guid.CreateVersion7(); var messageId = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
                    await db.ExecuteAsync("INSERT INTO fn_ai_chat_session (Id,TenantId,OwnerUserId,ModelConfigId,ModelName,Title,IsGenerating,GenerationId,GenerationExpiresAtUtc,GenerationCancellationRequested,CreatedAtUtc) VALUES (@Id,@Tenant,@Actor,@Model,'gpt-4.1-mini','原生人工历史',1,@Message,@Expiry,0,@Now)",
                        new { Id = sessionId, Tenant = tenantId, Actor = reader.UserId, Model = generation.Id, Message = messageId, Expiry = now.AddMinutes(2), Now = now });
                    var command = sp.GetRequiredService<ICommandExecutor>();
                    Assert.AreEqual(1, await command.ExecuteAsync(AiKnowledgeAnswerSql.InsertFor(tenant), AiSqlParameters.Create(("SessionId", sessionId), ("MessageId", messageId), ("OwnerUserId", reader.UserId), ("Now", now))));
                    // 生成答案与操作 ID 为受控夹具；Host 走生产落库，租户只种入历史，不扩大现有聊天写入边界。
                    var executed = new AiKnowledgeExecutedAnswer(preparedAnswer, accepted, Guid.CreateVersion7(), Guid.CreateVersion7());
                    if (tenant)
                        Assert.AreEqual(1, await db.ExecuteAsync("UPDATE fn_ai_chat_message SET Content=@Content,StatusKey='completed',KnowledgeLineage=@Lineage WHERE Id=@Id AND SessionId=@Session", new
                        {
                            Content = accepted.Text, Lineage = AiKnowledgeAnswerLineageCodec.Encode(AiKnowledgeAnswerLineageCodec.Create(executed, messageId, sessionId)), Id = messageId, Session = sessionId
                        }));
                    else Assert.IsTrue(await sp.GetRequiredService<AiKnowledgeAnswerStore>().CompleteAsync(executed, messageId, sessionId, reader.UserId, default));
                    return (sessionId, messageId);
                }
                finally { accessor.HttpContext = previous; }
            }
            async Task<AiChatMessageResponse> History() => (await Send<AiChatSessionResponse>(HttpMethod.Get, $"/api/v1/ai/chat/sessions/{saved.SessionId}", readerToken)).Messages.Single(m => m.Id == saved.MessageId);
        }
        if (api is not null) { await api.StopGracefullyAsync(); api.AssertNoFatalMarkersInLogs(); Assert.AreEqual(0, api.ExitCode); }

        async Task<T> Send<T>(HttpMethod method, string uri, string token, object? body = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            using var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new("Bearer", token);
            if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
            using var response = await client.SendAsync(request); await Require(response, status); return (await response.Content.ReadFromJsonAsync<T>())!;
        }
        async Task WaitBrowser(string file, string property)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var target = Path.Combine(browserBridge!, file);
            while (!File.Exists(target)) await Task.Delay(250, deadline.Token);
            using var state = JsonDocument.Parse(await File.ReadAllTextAsync(target, deadline.Token));
            Assert.IsTrue(state.RootElement.GetProperty(property).GetBoolean(), "真实搜索浏览器未通过，保留原始报告。");
        }
        async Task Expect(HttpMethod method, string uri, string token, object? body, HttpStatusCode status)
        {
            using var request = new HttpRequestMessage(method, uri); if (body is not null) request.Content = JsonContent.Create(body, body.GetType()); request.Headers.Authorization = new("Bearer", token);
            using var response = await client.SendAsync(request); await Require(response, status);
        }
        static async Task Require(HttpResponseMessage response, HttpStatusCode status) => Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
    private static JsonDocument Payload(string token)
    {
        var value = token.Split('.')[1].Replace('-', '+').Replace('_', '/'); return JsonDocument.Parse(Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '=')));
    }
    /// <summary>JIT 索引继续复用原受控向量，公开问答使用严格的缓冲答案消费者。</summary>
    private sealed class SearchAnswerFactory(AiKnowledgeIndexEmbeddingAssertions.ControlledFactory embedding) : IAiModelClientFactory
    {
        private readonly AiKnowledgePaidAnswerAssertions.ControlledFactory answer = new();
        public string ProviderKey => embedding.ProviderKey;
        public ValueTask<Microsoft.Extensions.AI.IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken token) => answer.CreateChatClientAsync(binding, token);
        public ValueTask<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>> CreateEmbeddingGeneratorAsync(ModelBinding binding, CancellationToken token) => embedding.CreateEmbeddingGeneratorAsync(binding, token);
    }
}

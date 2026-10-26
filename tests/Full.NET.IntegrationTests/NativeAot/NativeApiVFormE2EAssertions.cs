using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只在真实原生进程验证新增设计资产、绑定编译、授权投影及提交物化路径。</summary>
internal static class NativeApiVFormE2EAssertions
{
    public static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        var artifact = NativeApiArtifactLocator.RequireArtifact();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString);
        await using var host = await NativeApiProcessHost.StartAsync(artifact, provider, connectionString,
            new Dictionary<string, string?>(), NativeAotTestTimeouts.ProcessStartup);
        using var client = host.CreateClient();
        var token = await NativeApiE2EAssertions.LoginAsync(client, host.LogFilePath, default);

        // 每次请求均保留响应和原生日志定位信息；不以 JIT API 代替被验收进程。
        async Task<JsonDocument> SendAsync(HttpMethod method, string path, HttpStatusCode expected, object? body = null, bool authenticated = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Origin", "http://localhost");
            using var response = await client.SendAsync(request);
            await NativeApiE2EAssertions.AssertStatusAsync(response, expected, "VForm " + method + " " + path, default, host.LogFilePath);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        using var created = await SendAsync(HttpMethod.Post, "/api/v1/workflow/forms", HttpStatusCode.Created,
            new { formKey = "native.vform." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, adapterVersion = 1, sections = Array.Empty<object>() } });
        var formId = created.RootElement.GetProperty("id").GetGuid();
        var assetPath = $"/api/v1/workflow/forms/{formId:D}/design-asset";
        using var design = JsonDocument.Parse("""
            {"widgetList":[{"id":"grid","type":"grid","category":"container","options":{"name":"grid","gutter":20},"cols":[{
              "id":"col","type":"grid-col","category":"container","options":{"name":"col","span":24},"widgetList":[
                {"id":"title","type":"input","formItemFlag":true,"options":{"name":"title","label":"原版申请标题","placeholder":"完整保留","required":true,"defaultValue":"原版默认值","extra":{"keep":[1,true,null]}}},
                {"id":"tags","type":"checkbox","formItemFlag":true,"options":{"name":"tags","label":"原版多选","defaultValue":["a"],"optionItems":[{"label":"选项甲","value":"a"},{"label":"选项乙","value":"b"}]}},
                {"id":"cutoff","type":"time","formItemFlag":true,"options":{"name":"cutoff","label":"截止时间","format":"HH:mm:ss"}},
                {"id":"secret","type":"input","options":{"name":"secret","defaultValue":"不得发送的设计默认值"}}
              ]
            }]}],"formConfig":{"jsonVersion":3,"labelWidth":180,"labelPosition":"top","size":"large","cssCode":".original { color: #123456; }"},"extraRoot":{"values":["label",1,false,null]}}
            """);
        object Save(long revision) => new { expectedRevision = revision, sourceCommit = WorkflowDesignerAsset.SourceCommit, formatVersion = 3, designerJson = design.RootElement };
        using var saved = await SendAsync(HttpMethod.Put, assetPath, HttpStatusCode.OK, Save(1));
        Assert.AreEqual(2, saved.RootElement.GetProperty("draftRevision").GetInt64());
        using var reread = await SendAsync(HttpMethod.Get, assetPath, HttpStatusCode.OK);
        Assert.IsTrue(JsonElement.DeepEquals(design.RootElement, reread.RootElement.GetProperty("designerJson")));
        using var staleSave = await SendAsync(HttpMethod.Put, assetPath, HttpStatusCode.Conflict, Save(1));
        using var anonymous = await SendAsync(HttpMethod.Get, assetPath, HttpStatusCode.Unauthorized, authenticated: false);

        var publishPath = $"/api/v1/workflow/forms/{formId:D}/publish";
        object Publish(long bindingRevision, string rendererHash) => new { expectedRevision = 2, expectedBindingRevision = bindingRevision, targets = new[] { "web" }, rendererHash };
        using var wrongRuntime = await SendAsync(HttpMethod.Post, publishPath, HttpStatusCode.BadRequest, Publish(2, new string('0', 64)));
        using var wrongRevision = await SendAsync(HttpMethod.Post, publishPath, HttpStatusCode.Conflict, Publish(1, WorkflowDesignerRelease.ContentHash));
        using var published = await SendAsync(HttpMethod.Post, publishPath, HttpStatusCode.OK, Publish(2, WorkflowDesignerRelease.ContentHash));
        var versionId = published.RootElement.GetProperty("id").GetGuid();
        using var frozen = await SendAsync(HttpMethod.Get, $"/api/v1/workflow/form-versions/{versionId:D}", HttpStatusCode.OK);
        using var frozenDesign = JsonDocument.Parse(frozen.RootElement.GetProperty("webRenderSchemaJson").GetString()!);
        Assert.IsTrue(JsonElement.DeepEquals(design.RootElement, frozenDesign.RootElement));

        using var definition = await SendAsync(HttpMethod.Post, "/api/v1/workflow/definitions", HttpStatusCode.Created,
            new { definitionKey = "native.vform." + Guid.NewGuid().ToString("N"), draft = new { schemaVersion = 1, nodes = new object[] {
                new { nodeKey = "start", nodeTypeKey = "start", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "approve" } } },
                new { nodeKey = "approve", nodeTypeKey = "human.approval", nodeSchemaVersion = 1, config = new { nextNodeKeys = new[] { "end" }, fieldPolicies = new { title = "required", tags = "editable", cutoff = "required", secret = "hidden" } } },
                new { nodeKey = "end", nodeTypeKey = "end", nodeSchemaVersion = 1, config = new { nextNodeKeys = Array.Empty<string>() } }
            } } });
        using var definitionVersion = await SendAsync(HttpMethod.Post, $"/api/v1/workflow/definitions/{definition.RootElement.GetProperty("id").GetGuid():D}/publish", HttpStatusCode.OK,
            new { expectedRevision = 1, formVersionId = versionId });
        using var started = await SendAsync(HttpMethod.Post, "/api/v1/workflow/instances", HttpStatusCode.Created,
            new { definitionVersionId = definitionVersion.RootElement.GetProperty("id").GetGuid(), businessType = "native.vform", businessId = Guid.NewGuid().ToString("N"), initialValues = new { title = "原生办理初值", tags = new[] { "a" }, cutoff = "08:30", secret = "不得发送的实际提交值" }, idempotencyKey = Guid.NewGuid().ToString("N") });
        var todoId = started.RootElement.GetProperty("activeTodoId").GetGuid();
        var runtimePath = $"/api/v1/workflow/todos/{todoId:D}/runtime";
        using var runtime = await SendAsync(HttpMethod.Get, runtimePath, HttpStatusCode.OK);
        var view = runtime.RootElement.GetProperty("webRenderJson");
        Assert.AreEqual(WorkflowDesignerRelease.ContentHash, view.GetProperty("fullNetRuntime").GetProperty("rendererHash").GetString());
        Assert.AreEqual(3, view.GetProperty("fullNetRuntime").GetProperty("bindings").GetArrayLength());
        Assert.AreEqual("title", view.GetProperty("fullNetRuntime").GetProperty("bindings")[0].GetProperty("componentId").GetString());
        Assert.AreEqual("string-array", view.GetProperty("fullNetRuntime").GetProperty("bindings")[1].GetProperty("codec").GetString());
        Assert.AreEqual("iso-time", view.GetProperty("fullNetRuntime").GetProperty("bindings")[2].GetProperty("codec").GetString());
        Assert.AreEqual("原生办理初值", runtime.RootElement.GetProperty("submission").GetProperty("title").GetString());
        Assert.AreEqual("a", runtime.RootElement.GetProperty("submission").GetProperty("tags")[0].GetString());
        Assert.AreEqual("08:30", runtime.RootElement.GetProperty("submission").GetProperty("cutoff").GetString());
        Assert.IsFalse(runtime.RootElement.GetProperty("submission").TryGetProperty("secret", out _));
        Assert.IsFalse(view.GetRawText().Contains("secret", StringComparison.Ordinal));
        object Approve(object patch) => new { expectedRevision = 1, expectedFormRevision = runtime.RootElement.GetProperty("submissionRevision").GetInt64(), fieldPatch = patch, idempotencyKey = Guid.NewGuid().ToString("N") };
        // 字段策略拒绝遵循既有 BusinessRule 契约 422；后续合法审批还须证明失败没有修改修订。
        using var invalidPatch = await SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", HttpStatusCode.UnprocessableEntity, Approve(new { secret = "越权" }));
        using var completed = await SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/approve", HttpStatusCode.OK, Approve(new { title = "原生实际提交", tags = new[] { "b" }, cutoff = "12:45:37" }));
        Assert.AreEqual("completed", completed.RootElement.GetProperty("statusKey").GetString());
        using var finalRuntime = await SendAsync(HttpMethod.Get, runtimePath, HttpStatusCode.OK);
        Assert.AreEqual("原生实际提交", finalRuntime.RootElement.GetProperty("submission").GetProperty("title").GetString());
        Assert.AreEqual("b", finalRuntime.RootElement.GetProperty("submission").GetProperty("tags")[0].GetString());
        Assert.AreEqual("12:45:37", finalRuntime.RootElement.GetProperty("submission").GetProperty("cutoff").GetString());

        // 同一真实进程切换可信租户，验证新查询的租户参数在原生物化路径中实际生效。
        token = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, token);
        using var tenantRead = await SendAsync(HttpMethod.Get, assetPath, HttpStatusCode.NotFound);
        using var tenantWrite = await SendAsync(HttpMethod.Put, assetPath, HttpStatusCode.NotFound, Save(2));
        await host.StopGracefullyAsync();
        host.AssertNoFatalMarkersInLogs();
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.IntegrationTests.EnterpriseRequest;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只通过正式 HTTP 和独立原生宿主观察业务；Migrator 仍为 JIT，不借用测试容器内 DI。</summary>
internal static class NativeEnterpriseBusinessAssertions
{
    private const string Requests = "/api/v1/enterprise_request/enterprise-requests";
    private const string Imports = "/api/v1/import-export/tasks";

    internal static void RequireArtifacts()
    {
        _ = NativeApiArtifactLocator.RequireArtifact();
        // Linux 专项缺少任一角色必须失败，不能把只验证 API 的结果当作双进程验收。
        Assert.IsTrue(NativeWorkerArtifactLocator.TryResolve(out _, out var reason), reason);
    }

    internal static async Task VerifyAsync(DatabaseProvider provider, string connectionString)
    {
        RequireArtifacts();
        await NativeApiDatabaseBootstrap.BootstrapAsync(provider, connectionString);
        var external = new SqlConnectionStringBuilder(await SharedDatabaseFixture.CreateSqlServerDatabaseAsync());
        var destination = external.DataSource.Split(',');
        Assert.AreEqual(2, destination.Length);
        var files = Path.Combine(Path.GetTempPath(), $"fullnet-native-enterprise-{Guid.NewGuid():N}");
        Directory.CreateDirectory(files);
        try
        {
            await using var api = await NativeApiProcessHost.StartAsync(NativeApiArtifactLocator.RequireArtifact(),
                provider, connectionString, new Dictionary<string, string?>
                {
                    ["Files:Local:RootPath"] = files,
                    ["FullNet:ImportExport:RunSynchronously"] = "false",
                    ["FullNet:ImportExport:ExecutionEnabled"] = "false",
                    ["FullNet:Reporting:Export:ExecutionEnabled"] = "false",
                    ["Realtime:Enabled"] = "false",
                    ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:Provider"] = "SqlServer",
                    ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:Host"] = destination[0],
                    ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:Port"] = destination[1],
                    ["FullNet:ExternalDatabaseAccess:AllowedDestinations:0:AllowUntrustedCertificate"] = "true",
                }, NativeAotTestTimeouts.ProcessStartup);
            using var client = api.CreateClient();
            var hostToken = await NativeApiE2EAssertions.LoginAsync(client, api.LogFilePath);
            var token = await NativeApiE2EAssertions.EnterLocalTenantAsync(client, hostToken);
            var http = new BusinessHttp(client, token, api.LogFilePath);
            var actor = await http.SendAsync(HttpMethod.Get, "/api/v1/me");
            var actorId = actor.GetProperty("id").GetGuid();
            var unit = await http.SendAsync(HttpMethod.Post, "/api/v1/organization/units", new
            {
                parentId = (Guid?)null, code = $"native-er-{Guid.NewGuid():N}", name = "原生企业验收组织", displayOrder = 10,
            }, HttpStatusCode.Created);
            var unitId = unit.GetProperty("id").GetGuid();
            await http.SendAsync(HttpMethod.Post, "/api/v1/organization/user-units", new
            {
                userId = actorId, unitId, isPrimary = false,
            }, HttpStatusCode.Created);
            await EnterpriseRequestWorkflowBootstrap.PublishDemoApprovalDefinitionInTenantAsync(client, token);

            var draft = await CreateAsync(http, unitId, actorId, "草稿并发验收");
            var id = draft.GetProperty("id").GetGuid();
            Assert.AreEqual(7, id.Version);
            var tenantId = draft.GetProperty("tenantId").GetGuid();
            var progress = await http.SendAsync(HttpMethod.Get, $"{Requests}/{id:D}/approval-progress");
            Assert.AreEqual("not_submitted", progress.GetProperty("deliveryState").GetString());
            await http.SendAsync(HttpMethod.Get, $"{Requests}/{id:D}/approval-progress", expected: HttpStatusCode.Unauthorized,
                anonymous: true);
            var lines = await http.SendAsync(HttpMethod.Put, $"{Requests}/{id:D}/lines", new
            {
                version = draft.GetProperty("version").GetString(), items = new[]
                {
                    new { itemDescription = "原生精度", quantity = "1.0001", unitPrice = "50.00" },
                    new { itemDescription = "原生小数", quantity = "2.5", unitPrice = "3.21" },
                },
            });
            Assert.AreEqual("58.04", lines.GetProperty("totalAmount").GetString());
            Assert.AreEqual("50.01", lines.GetProperty("items")[0].GetProperty("lineAmount").GetString());
            await http.SendAsync(HttpMethod.Put, $"{Requests}/{id:D}/lines", new
            {
                version = draft.GetProperty("version").GetString(), items = Array.Empty<object>(),
            }, HttpStatusCode.Conflict);
            var preserved = await http.SendAsync(HttpMethod.Get, $"{Requests}/{id:D}/lines");
            Assert.AreEqual(lines.GetProperty("requestVersion").GetString(), preserved.GetProperty("requestVersion").GetString());
            Assert.AreEqual(2, preserved.GetProperty("items").GetArrayLength());

            // 两份申请及非空工作簿都先排队；尚未启动 Worker 时只能观察本模块的 queued 回执。
            var submitted = await SubmitAsync(http, id);
            var rejectedDraft = await CreateAsync(http, unitId, actorId, "原生驳回验收");
            var rejectedId = rejectedDraft.GetProperty("id").GetGuid();
            var rejected = await SubmitAsync(http, rejectedId);
            var importNumber = $"NIMP-{Guid.NewGuid():N}"[..16];
            var importId = await QueueWorkbookAsync(http, importNumber, actorId, unitId);

            Assert.IsTrue(NativeWorkerArtifactLocator.TryResolve(out var workerArtifact, out var reason), reason);
            await using var worker = await NativeWorkerProcessHost.StartAsync(workerArtifact, provider, connectionString,
                NativeAotTestTimeouts.ProcessStartup, filesRootPath: files, additionalSettings: new Dictionary<string, string?>
                {
                    ["FullNet__ImportExport__ExecutionEnabled"] = "true",
                    ["FullNet__ImportExport__RunSynchronously"] = "false",
                    ["FullNet__ImportExport__PollSeconds"] = "5",
                    ["FullNet__Reporting__Export__PollSeconds"] = "5",
                    ["FullNet__Reporting__Export__ExecutionEnabled"] = "true",
                });
            await CompleteApprovalAsync(http, id, submitted, "approve", "Approved");
            await CompleteApprovalAsync(http, rejectedId, rejected, "reject", "Rejected");
            var imported = await WaitAsync(http, $"{Imports}/{importId:D}", value =>
                value.GetProperty("statusKey").GetString() == "execution_succeeded", value =>
                Assert.IsTrue(new[] { "queued", "executing", "execution_succeeded" }.Contains(value.GetProperty("statusKey").GetString())));
            Assert.AreEqual(1, imported.GetProperty("succeededRowCount").GetInt32());
            Assert.AreEqual(0, imported.GetProperty("executionFailedRowCount").GetInt32());
            Assert.AreEqual(tenantId, imported.GetProperty("tenantId").GetGuid());
            var list = await http.SendAsync(HttpMethod.Get, $"{Requests}?page=1&pageSize=100");
            var importedRows = list.GetProperty("items").EnumerateArray()
                .Where(item => item.GetProperty("requestNumber").GetString() == importNumber).ToArray();
            Assert.AreEqual(1, importedRows.Length, "正式 Worker 只能导入一份业务申请。");
            var row = await http.SendAsync(HttpMethod.Get, $"{Requests}/{importedRows[0].GetProperty("id").GetGuid():D}");
            Assert.AreEqual("Draft", row.GetProperty("status").GetString());
            Assert.AreEqual("123.45", row.GetProperty("totalAmount").GetString());
            Assert.AreEqual(tenantId, row.GetProperty("tenantId").GetGuid());
            Assert.AreEqual(unitId, row.GetProperty("organizationUnitId").GetGuid());
            Assert.AreEqual(actorId, row.GetProperty("applicantUserId").GetGuid());

            await VerifyReportingExportAsync(http,
                external, destination, provider, connectionString, tenantId);

            await worker.StopGracefullyAsync(TimeSpan.FromSeconds(20));
            Assert.AreEqual(0, worker.ExitCode);
            worker.AssertNoFatalMarkersInLogs();
            await api.StopGracefullyAsync();
            Assert.AreEqual(0, api.ExitCode);
            api.AssertNoFatalMarkersInLogs();
        }
        finally
        {
            // 两个宿主均已结束并排空日志，才清理本场景拥有的文件目录。
            Directory.Delete(files, recursive: true);
        }
    }

    private static Task<JsonElement> CreateAsync(BusinessHttp http, Guid unitId, Guid actorId, string title) =>
        http.SendAsync(HttpMethod.Post, Requests, new
        {
            requestNumber = $"NREQ-{Guid.NewGuid():N}"[..16], title, status = "Draft", totalAmount = "1.00", applicantUserId = actorId,
        }, HttpStatusCode.Created, unitId: unitId);

    /// <summary>同一真实栈验收报表生成、文件下载及上传后崩溃的原生 Worker 恢复。</summary>
    private static async Task VerifyReportingExportAsync(BusinessHttp tenant,
        SqlConnectionStringBuilder external, string[] destination, DatabaseProvider provider,
        string connectionString, Guid tenantId)
    {
        // 上下文切换会撤销旧会话；每次使用正式接口返回的新令牌，不复用最初的 Host 令牌。
        var host = await tenant.ChangeContextAsync(null);
        await VerifySettingsAuditDiffAsync(host);
        var source = await host.SendAsync(HttpMethod.Post, "/api/v1/reporting/data-sources", new
        {
            tenantId = (Guid?)null, name = "原生报表外部数据源", providerKey = "sql_server",
            serverHost = destination[0], port = int.Parse(destination[1], System.Globalization.CultureInfo.InvariantCulture),
            databaseName = external.InitialCatalog, username = external.UserID, password = external.Password,
            trustServerCertificate = true, isEnabled = true,
        }, HttpStatusCode.Created);
        var group = await host.SendAsync(HttpMethod.Post, "/api/v1/reporting/groups", new
        {
            parentId = (Guid?)null, name = "原生报表验收", sortOrder = 0, isEnabled = true,
        }, HttpStatusCode.Created);
        var definition = await host.SendAsync(HttpMethod.Post, "/api/v1/reporting/definitions", new
        {
            groupId = group.GetProperty("id").GetGuid(), dataSourceId = source.GetProperty("id").GetGuid(),
            definitionKey = $"native-inventory-{Guid.NewGuid():N}", name = "原生目录报表", description = (string?)null,
            queryPortKey = "reporting.schema_inventory", parameterSchema = new[]
            {
                new { parameterKey = "topN", displayName = "条数", dataTypeKey = "integer", isRequired = true, defaultValue = "20" },
            },
            layoutConfigJson = "{\"columns\":[{\"key\":\"SchemaName\",\"requiredPermission\":\"reporting.executions.columns.schema_name\"}]}",
            isEnabled = true,
        }, HttpStatusCode.Created);
        var definitionId = definition.GetProperty("id").GetGuid();
        await host.SendAsync(HttpMethod.Post, $"/api/v1/reporting/definitions/{definitionId:D}/publish",
            new { changeNote = "原生验收", version = definition.GetProperty("version").GetInt32() });
        var grant = $"/api/v1/reporting/definitions/{definitionId:D}/versions/1/tenant-grants/{tenantId:D}";
        await host.SendAsync(HttpMethod.Put, grant);
        tenant = await host.ChangeContextAsync(tenantId);
        var task = await tenant.SendAsync(HttpMethod.Post, "/api/v1/reporting/export-tasks", new
        {
            definitionId, formatKey = "excel", versionNumber = 1,
            parameters = new[] { new { parameterKey = "topN", value = "20" } },
        }, HttpStatusCode.Created);
        Assert.AreEqual("succeeded", task.GetProperty("statusKey").GetString());
        Assert.IsGreaterThan(0, task.GetProperty("rowCount").GetInt32());
        var id = task.GetProperty("id").GetGuid();
        var fileId = task.GetProperty("outputFileId").GetGuid();
        var download = $"/api/v1/reporting/export-tasks/{id:D}/download";
        var bytes = await tenant.DownloadAsync(download);
        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            Assert.IsNotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        }

        // 只注入崩溃后的持久化状态；恢复本身必须由独立原生 Worker 执行生产逻辑。
        await using System.Data.Common.DbConnection connection = provider == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString)
            : new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: false));
        await connection.OpenAsync();
        Assert.AreEqual(1, await connection.ExecuteAsync("""
            UPDATE fn_reporting_export_task
            SET StatusKey='processing', OutputFileId=NULL, OutputFileName=NULL, CompletedAtUtc=NULL,
                LeaseId=@LeaseId, LeaseExpiresAtUtc=@Expiry
            WHERE TenantId=@TenantId AND Id=@Id AND StatusKey='succeeded'
            """, new { TenantId = tenantId, Id = id, LeaseId = Guid.CreateVersion7(), Expiry = DateTime.UtcNow.AddMinutes(-1) }));
        var recovered = await WaitAsync(tenant, $"/api/v1/reporting/export-tasks/{id:D}",
            current => current.GetProperty("statusKey").GetString() == "succeeded");
        Assert.AreEqual(fileId, recovered.GetProperty("outputFileId").GetGuid());
        CollectionAssert.AreEqual(bytes, await tenant.DownloadAsync(download));
        host = await tenant.ChangeContextAsync(null);
        await host.SendAsync(HttpMethod.Delete, grant);
        tenant = await host.ChangeContextAsync(tenantId);
        await tenant.SendAsync(HttpMethod.Get, download, expected: HttpStatusCode.Forbidden);
    }

    private static async Task<JsonElement> SubmitAsync(BusinessHttp http, Guid id)
    {
        var submitted = await http.SendAsync(HttpMethod.Post, $"{Requests}/{id:D}/submit-for-approval");
        Assert.AreEqual("Submitted", submitted.GetProperty("status").GetString());
        var queued = await http.SendAsync(HttpMethod.Get, $"{Requests}/{id:D}/approval-progress");
        Assert.AreEqual("queued", queued.GetProperty("deliveryState").GetString());
        Assert.AreEqual(submitted.GetProperty("version").GetString(), queued.GetProperty("submittedVersion").GetString());
        var replay = await http.SendAsync(HttpMethod.Post, $"{Requests}/{id:D}/submit-for-approval");
        Assert.AreEqual(submitted.GetProperty("version").GetString(), replay.GetProperty("version").GetString());
        return queued;
    }

    private static async Task CompleteApprovalAsync(BusinessHttp http, Guid id, JsonElement queued, string action, string status)
    {
        var started = await WaitAsync(http, $"{Requests}/{id:D}/approval-progress", value =>
            value.GetProperty("deliveryState").GetString() == "started");
        var instanceId = started.GetProperty("workflowInstanceId").GetGuid();
        Assert.AreEqual(queued.GetProperty("workflowInstanceId").GetGuid(), instanceId);
        Assert.AreEqual(queued.GetProperty("submittedVersion").GetString(), started.GetProperty("submittedVersion").GetString());
        var instance = await http.SendAsync(HttpMethod.Get, $"/api/v1/workflow/instances/{instanceId:D}");
        var todoId = instance.GetProperty("activeTodoId").GetGuid();
        await VerifyInstanceListsAsync(http, instance, completed: false);
        var pendingPage = await http.SendAsync(HttpMethod.Get, "/api/v1/workflow/todos/mine?page=1&pageSize=100");
        var pending = FindListItem(pendingPage, todoId);
        var todo = await http.SendAsync(HttpMethod.Get, $"/api/v1/workflow/todos/{todoId:D}");
        foreach (var field in new[] { "id", "instanceId", "stepId", "statusKey", "revision" })
            Assert.AreEqual(todo.GetProperty(field).GetRawText(), pending.GetProperty(field).GetRawText(), field);
        // 详情契约没有到达时间；列表单独核对时间范围，随后与真实历史快照比较。
        Assert.IsTrue(pending.GetProperty("arrivedAtUtc").GetDateTimeOffset() >= instance.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.IsTrue(pending.GetProperty("arrivedAtUtc").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
        foreach (var field in new[] { "businessType", "businessId", "businessTitle" })
            Assert.AreEqual(instance.GetProperty(field).GetRawText(), pending.GetProperty(field).GetRawText(), field);
        Assert.AreEqual(instance.GetProperty("statusKey").GetString(), pending.GetProperty("instanceStatusKey").GetString());
        // 演示定义使用单人审批；详情中的 ActiveNodeKey 仅描述多人审批进度。
        Assert.AreEqual("approve", pending.GetProperty("nodeKey").GetString());
        Assert.IsFalse(string.IsNullOrWhiteSpace(pending.GetProperty("definitionKey").GetString()));
        Assert.AreEqual(JsonValueKind.Null, pending.GetProperty("completedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, pending.GetProperty("resultActionKey").ValueKind);
        var command = new { expectedRevision = 1, fieldPatch = new { }, comment = "原生业务验收", idempotencyKey = $"native-{Guid.NewGuid():N}" };
        var completed = await http.SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/{action}", command);
        Assert.AreEqual(action == "approve" ? "completed" : "rejected", completed.GetProperty("statusKey").GetString());
        // 同一 HTTP 幂等键重试不能再次推进业务版本或创建另一份流程。
        await http.SendAsync(HttpMethod.Post, $"/api/v1/workflow/todos/{todoId:D}/{action}", command);
        var final = await WaitAsync(http, $"{Requests}/{id:D}/approval-progress", value =>
            value.GetProperty("deliveryState").GetString() == "finalized");
        Assert.AreEqual(status, final.GetProperty("requestStatus").GetString());
        Assert.AreEqual(instanceId, final.GetProperty("workflowInstanceId").GetGuid());
        Assert.AreEqual(queued.GetProperty("submittedVersion").GetString(), final.GetProperty("submittedVersion").GetString());
        var request = await http.SendAsync(HttpMethod.Get, $"{Requests}/{id:D}");
        Assert.AreEqual(status, request.GetProperty("status").GetString());
        Assert.AreEqual(final.GetProperty("requestVersion").GetString(), request.GetProperty("version").GetString());
        var finishedInstance = await http.SendAsync(HttpMethod.Get, $"/api/v1/workflow/instances/{instanceId:D}");
        await VerifyInstanceListsAsync(http, finishedInstance, completed: true);
        var historyPage = await http.SendAsync(HttpMethod.Get, "/api/v1/workflow/todos/mine/history?page=1&pageSize=100");
        var history = FindListItem(historyPage, todoId);
        foreach (var field in new[] { "id", "instanceId", "stepId", "arrivedAtUtc", "businessType", "businessId", "businessTitle", "definitionKey", "nodeKey" })
            Assert.AreEqual(pending.GetProperty(field).GetRawText(), history.GetProperty(field).GetRawText(), field);
        Assert.AreEqual(action, history.GetProperty("resultActionKey").GetString());
        Assert.IsTrue(history.GetProperty("revision").GetInt64() > pending.GetProperty("revision").GetInt64());
        Assert.IsTrue(history.GetProperty("completedAtUtc").GetDateTimeOffset() >= history.GetProperty("arrivedAtUtc").GetDateTimeOffset());
        Assert.AreEqual(finishedInstance.GetProperty("statusKey").GetString(), history.GetProperty("instanceStatusKey").GetString());
    }

    /// <summary>列表必须物化真实实例，并同时验证全局管理和“我发起的”两条受控查询。</summary>
    private static async Task VerifyInstanceListsAsync(BusinessHttp http, JsonElement instance, bool completed)
    {
        foreach (var path in new[] { "/api/v1/workflow/instances", "/api/v1/workflow/instances/mine" })
        {
            var page = await http.SendAsync(HttpMethod.Get, path + "?page=1&pageSize=100");
            var item = FindListItem(page, instance.GetProperty("id").GetGuid());
            foreach (var field in new[] { "id", "definitionVersionId", "businessType", "businessId", "businessTitle", "statusKey", "startedAtUtc" })
                Assert.AreEqual(instance.GetProperty(field).GetRawText(), item.GetProperty(field).GetRawText(), field);
            Assert.IsFalse(string.IsNullOrWhiteSpace(item.GetProperty("definitionKey").GetString()));
            Assert.AreNotEqual(Guid.Empty, item.GetProperty("startedById").GetGuid());
            if (completed)
                Assert.IsTrue(item.GetProperty("completedAtUtc").GetDateTimeOffset() >= item.GetProperty("startedAtUtc").GetDateTimeOffset());
            else
                Assert.AreEqual(JsonValueKind.Null, item.GetProperty("completedAtUtc").ValueKind);
        }
    }

    private static JsonElement FindListItem(JsonElement page, Guid id)
    {
        var matches = page.GetProperty("items").EnumerateArray().Where(item => item.GetProperty("id").GetGuid() == id).ToArray();
        Assert.HasCount(1, matches, "分页查询必须包含且只包含一份真实业务行，空列表不能证明物化器有效。");
        return matches[0];
    }

    /// <summary>正式诊断策略写入生成真实域审计，再通过聚合 API 读取同一 Trace 的非空差异。</summary>
    private static async Task VerifySettingsAuditDiffAsync(BusinessHttp host)
    {
        const string policyPath = "/api/v1/settings/diagnostic-policy";
        var current = await host.SendAsync(HttpMethod.Get, policyPath);
        var traceId = Guid.NewGuid().ToString("N");
        var updated = await host.SendAsync(HttpMethod.Put, policyPath, new
        {
            pressureState = "Normal", rules = Array.Empty<object>(),
            configEntryVersion = current.GetProperty("configEntryVersion").GetInt32(),
        }, traceId: traceId);
        var path = "/api/v1/auditing/domain-change-diffs?traceId=" + traceId;
        await host.SendAsync(HttpMethod.Get, path, expected: HttpStatusCode.Unauthorized, anonymous: true);
        var diffs = await host.SendAsync(HttpMethod.Get, path);
        Assert.AreEqual(traceId, diffs.GetProperty("traceId").GetString());
        var entries = diffs.GetProperty("entries").EnumerateArray().Where(entry => entry.GetProperty("moduleKey").GetString() == "settings").ToArray();
        Assert.HasCount(1, entries);
        var entry = entries[0];
        Assert.AreNotEqual(Guid.Empty, entry.GetProperty("auditId").GetGuid());
        Assert.AreEqual("settings.logging-diagnostic-policy.updated", entry.GetProperty("actionKey").GetString());
        Assert.AreEqual("available", entry.GetProperty("availability").GetString());
        Assert.IsTrue(entry.GetProperty("occurredAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-30));
        var fields = entry.GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("fieldKey").GetString()!, field => field.GetProperty("afterValue").GetString());
        Assert.AreEqual(updated.GetProperty("version").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture), fields["version"]);
        Assert.AreEqual("Normal", fields["pressure"]);
    }

    private static async Task<Guid> QueueWorkbookAsync(BusinessHttp http, string number, Guid actorId, Guid unitId)
    {
        var bytes = await http.DownloadAsync("/api/v1/import-export/schemas/demo.enterprise_requests/worksheets/requests/template");
        using var workbook = new MemoryStream();
        workbook.Write(bytes);
        using (var zip = new ZipArchive(workbook, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument document;
            using (var input = entry.Open()) document = XDocument.Load(input);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var values = new[] { number, "原生非空导入", "123.45", actorId.ToString("D"), unitId.ToString("D") };
            document.Root!.Element(ns + "sheetData")!.Add(new XElement(ns + "row", new XAttribute("r", 2),
                values.Select((value, index) => new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + index)}2"),
                    new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value))))));
            entry.Delete();
            using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open();
            document.Save(output);
        }
        using var form = new MultipartFormDataContent
        {
            { new StringContent("demo.enterprise_requests"), "schemaKey" }, { new StringContent("requests"), "worksheetKey" },
        };
        var file = new ByteArrayContent(workbook.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(file, "file", "native-enterprise.xlsx");
        var preview = await http.UploadAsync(form);
        Assert.AreEqual("preview_succeeded", preview.GetProperty("statusKey").GetString());
        Assert.AreEqual(1, preview.GetProperty("validRowCount").GetInt32());
        Assert.AreEqual(0, preview.GetProperty("invalidRowCount").GetInt32());
        var id = preview.GetProperty("id").GetGuid();
        var queued = await http.SendAsync(HttpMethod.Post, $"{Imports}/{id:D}/execute");
        Assert.AreEqual("queued", queued.GetProperty("statusKey").GetString());
        return id;
    }

    private static async Task<JsonElement> WaitAsync(BusinessHttp http, string path, Func<JsonElement, bool> complete,
        Action<JsonElement>? verifyIntermediate = null)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            var current = await http.SendAsync(HttpMethod.Get, path, allowProgressSnapshotRetry: true);
            if (current.ValueKind != JsonValueKind.Undefined)
            {
                verifyIntermediate?.Invoke(current);
                if (complete(current)) return current;
            }
            await Task.Delay(250);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(90));
        Assert.Fail($"原生 Worker 未在预算内完成正式业务回执：{path}");
        return default;
    }

    private sealed class BusinessHttp(HttpClient client, string token, string logPath)
    {
        internal async Task<BusinessHttp> ChangeContextAsync(Guid? tenantId)
        {
            var changed = await SendAsync(HttpMethod.Put, "/api/v1/tenancy/context", new { tenantId });
            var accessToken = changed.GetProperty("accessToken").GetString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(accessToken));
            return new BusinessHttp(client, accessToken, logPath);
        }

        internal async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body = null,
            HttpStatusCode expected = HttpStatusCode.OK, Guid? unitId = null, bool anonymous = false,
            bool allowProgressSnapshotRetry = false, string? traceId = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (!anonymous) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (traceId is not null)
            {
                var spanId = Guid.NewGuid().ToString("N")[..16];
                request.Headers.Add("traceparent", $"00-{traceId}-{spanId}-01");
            }
            if (unitId is not null) request.Headers.Add(OrganizationRequestHeaders.OrganizationUnitId, unitId.Value.ToString("D"));
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request);
            // Worker 终态提交可能跨越进度的两次读取；只在既有轮询预算内重读精确快照冲突。
            if (allowProgressSnapshotRetry && method == HttpMethod.Get &&
                Full.NET.Testing.ApprovalProgressReadPolling.IsSnapshotConflict(path, response.StatusCode,
                    await response.Content.ReadAsStringAsync()))
                return default;
            await NativeApiE2EAssertions.AssertStatusAsync(response, expected, $"Native Enterprise {method} {path}",
                nativeLogFilePath: logPath);
            if (expected != HttpStatusCode.OK && expected != HttpStatusCode.Created) return default;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }

        internal async Task<byte[]> DownloadAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            await NativeApiE2EAssertions.AssertStatusAsync(response, HttpStatusCode.OK, "Native Enterprise workbook template", nativeLogFilePath: logPath);
            return await response.Content.ReadAsByteArrayAsync();
        }

        internal async Task<JsonElement> UploadAsync(HttpContent content)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Imports) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            await NativeApiE2EAssertions.AssertStatusAsync(response, HttpStatusCode.Created, "Native Enterprise workbook preview", nativeLogFilePath: logPath);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
    }
}

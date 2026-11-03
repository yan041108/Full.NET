using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.ImportExport.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Data.MySql;
using MySqlConnector;
using Microsoft.Extensions.DependencyInjection;
using Full.NET.Modules.ImportExport.ImportTasks;

namespace Full.NET.IntegrationTests.Api;

/// <summary>真实会话、双库、列重排与持久任务冻结共同验证个人导入配置。</summary>
[TestClass]
public sealed class ImportProfilesApiTests
{
    private const string Base = "/api/v1/me/import-profiles/organization.tenant_positions/positions";
    private const string Tasks = "/api/v1/import-export/tasks";
    private static readonly string[] Headers = ["code", "name", "displayOrder", "unitCode", "positionLevelCode"];
    private static readonly string[] Permissions = [ImportProfilePermissions.Read, ImportProfilePermissions.Create, ImportProfilePermissions.Update,
        ImportProfilePermissions.Delete, ImportExportPermissions.StaticSchemasRead, ImportExportPermissions.ImportTasksCreate,
        ImportExportPermissions.ImportTasksRead, ImportExportPermissions.ImportTasksExecute, OrganizationPositionManagementPermissions.Import,
        "identity.navigation.read", "tenancy.tenants.switch"];

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Profiles_and_frozen_imports_enforce_owner_versions_limits_and_source_authority(DatabaseProvider provider)
    {
        var connection = provider == DatabaseProvider.SqlServer ? await SharedDatabaseFixture.CreateSqlServerDatabaseAsync() : await SharedDatabaseFixture.CreateMySqlDatabaseAsync();
        using var factory = new FullNetApiFactory(provider, connection, new Dictionary<string, string?> { ["FullNet:ImportExport:RunSynchronously"] = "false", ["FullNet:ImportExport:ExecutionEnabled"] = "true" });
        await factory.InitializeAsync(); using var admin = factory.CreateClientForHost("localhost");
        var adminHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(admin, "admin"); admin.DefaultRequestHeaders.Authorization = new("Bearer", adminHost);
        var tenant = (await admin.GetFromJsonAsync<TenantContextSummary[]>("/api/v1/tenancy/available"))!.Single(item => item.Identifier == "acme");
        await SendAsync(admin, HttpMethod.Get, Base, null, HttpStatusCode.Forbidden);
        admin.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(admin, adminHost, tenant.Id));
        var owner = await MemberAsync("owner", Permissions); using var client = owner.Client;
        var other = await MemberAsync("other", Permissions); using var otherClient = other.Client;
        var denied = await MemberAsync("denied", Permissions.Where(permission => permission != OrganizationPositionManagementPermissions.Import).ToArray()); using var deniedClient = denied.Client;
        await SendAsync(deniedClient, HttpMethod.Get, Base, null, HttpStatusCode.Forbidden);
        var configuration = Mapping();
        var created = await SendAsync(client, HttpMethod.Post, Base, new SaveImportProfileRequest("供应商格式", configuration, 0), HttpStatusCode.Created);
        var id = created.GetProperty("summary").GetProperty("id").GetGuid();
        await SendAsync(otherClient, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.NotFound);
        await SendAsync(admin, HttpMethod.Get, $"{Base}/{id:D}", null, HttpStatusCode.NotFound);
        await SendAsync(client, HttpMethod.Put, $"{Base}/{id:D}", new SaveImportProfileRequest("新版", configuration, 1), HttpStatusCode.OK);
        await SendAsync(client, HttpMethod.Put, $"{Base}/{id:D}", new SaveImportProfileRequest("过期覆盖", configuration, 1), HttpStatusCode.Conflict);
        var list = await SendAsync(client, HttpMethod.Get, Base, null, HttpStatusCode.OK);
        Assert.AreEqual(1, list.GetArrayLength()); Assert.IsFalse(list[0].TryGetProperty("configuration", out _));
        await SendAsync(client, HttpMethod.Post, Base, new SaveImportProfileRequest("旧结构", new(["changed"], [new("changed", "x")]), 0), HttpStatusCode.Conflict);
        await SendAsync(client, HttpMethod.Post, Base, new SaveImportProfileRequest("重复来源", configuration with { Columns = Headers.Select(header => new ImportColumnMapping(header, "same")).ToArray() }, 0), HttpStatusCode.BadRequest);

        using var template = await client.GetAsync("/api/v1/import-export/schemas/organization.tenant_positions/worksheets/positions/template");
        Assert.AreEqual(HttpStatusCode.OK, template.StatusCode);
        var code = "mapped-" + Guid.CreateVersion7().ToString("N")[..12];
        var bytes = Workbook(await template.Content.ReadAsByteArrayAsync(),
            [["名称", "ignored", "级别", "编号", "单位", "顺序"], ["", "", "", "", "", ""], ["映射职位", "extra", "", code, "", "42"]]);
        var task = await UploadAsync(client, bytes, configuration, HttpStatusCode.Created);
        var taskId = task.GetProperty("id").GetGuid(); Assert.AreEqual(3, task.GetProperty("previewRows")[0].GetProperty("lineNumber").GetInt32());
        Assert.AreEqual(5, task.GetProperty("columnMappings").GetArrayLength());
        await UploadAsync(deniedClient, bytes, configuration, HttpStatusCode.Forbidden);
        await SendAsync(otherClient, HttpMethod.Post, $"{Tasks}/{taskId:D}/execute", null, HttpStatusCode.Forbidden);
        // 已创建任务只读自己的输入快照；配置修改为另一套表头并删除后仍导入原始字段含义。
        var replaced = new ImportColumnMappingRequest(Headers, Headers.Select(header => new ImportColumnMapping(header, "new-" + header)).ToArray());
        await SendAsync(client, HttpMethod.Put, $"{Base}/{id:D}", new SaveImportProfileRequest("已变更", replaced, 2), HttpStatusCode.OK);
        await SendAsync(client, HttpMethod.Delete, $"{Base}/{id:D}", new DeleteImportProfileRequest(3), HttpStatusCode.OK);
        var queued = await SendAsync(client, HttpMethod.Post, $"{Tasks}/{taskId:D}/execute", null, HttpStatusCode.OK);
        Assert.AreEqual(ImportExportTaskStatusKeys.Queued, queued.GetProperty("statusKey").GetString());
        await ProcessPendingAsync();
        var executed = await SendAsync(client, HttpMethod.Get, $"{Tasks}/{taskId:D}", null, HttpStatusCode.OK);
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, executed.GetProperty("statusKey").GetString());
        await using var database = MyTasksApiTests.Open(provider, connection); await database.OpenAsync();
        Assert.AreEqual(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_organization_position WHERE TenantId=@TenantId AND Code=@Code AND Name=@Name AND DisplayOrder=42", new { TenantId = tenant.Id, Code = code, Name = "映射职位" }));
        var stored = await database.QuerySingleAsync<(string? Mapping, string? Binding)>("SELECT ColumnMappingJson,ActorSessionBindingJson FROM fn_import_export_task WHERE Id=@Id", new { Id = taskId });
        Assert.IsFalse(string.IsNullOrWhiteSpace(stored.Mapping)); Assert.IsFalse(string.IsNullOrWhiteSpace(stored.Binding));
        Assert.IsFalse(executed.TryGetProperty("actorSessionBindingJson", out _)); Assert.IsFalse(executed.TryGetProperty("columnMappingJson", out _));
        var stale = await SendAsync(client, HttpMethod.Post, Base, new SaveImportProfileRequest("待修复", configuration, 0), HttpStatusCode.Created);
        var staleId = stale.GetProperty("summary").GetProperty("id").GetGuid();
        await database.ExecuteAsync("UPDATE fn_import_export_profile SET HeaderFingerprint=@Fingerprint WHERE Id=@Id", new { Id = staleId, Fingerprint = new string('0', 64) });
        var incompatible = await SendAsync(client, HttpMethod.Get, $"{Base}/{staleId:D}", null, HttpStatusCode.OK);
        Assert.IsFalse(incompatible.GetProperty("summary").GetProperty("isCompatible").GetBoolean()); Assert.AreEqual(JsonValueKind.Null, incompatible.GetProperty("configuration").ValueKind);
        await SendAsync(client, HttpMethod.Put, $"{Base}/{staleId:D}", new SaveImportProfileRequest("修复完成", configuration, 1), HttpStatusCode.OK);
        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await database.ExecuteAsync("""
            INSERT INTO fn_import_export_profile (Id,TenantId,OwnerUserId,SchemaKey,WorksheetKey,Name,HeaderFingerprint,ConfigurationJson,CreatedAtUtc,UpdatedAtUtc,Version)
            VALUES (@Id,@TenantId,@UserId,'organization.tenant_positions','positions',@Name,@Fingerprint,@Json,@Now,NULL,1)
            """, Enumerable.Range(0, 30).Select(index => new { Id = Guid.CreateVersion7(), TenantId = tenant.Id, UserId = owner.UserId,
                Name = "Bound " + index, Fingerprint = StaticImportHeaderMapping.Fingerprint(Headers), Json = json, Now = DateTime.UtcNow }));
        var attempts = await Task.WhenAll(CreateLastAsync(), CreateLastAsync());
        CollectionAssert.AreEquivalent(new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest }, attempts);
        Assert.AreEqual(32, (await SendAsync(client, HttpMethod.Get, Base, null, HttpStatusCode.OK)).GetArrayLength());

        var queuedCode = "queued-" + Guid.CreateVersion7().ToString("N")[..12];
        var queuedBytes = Workbook(await template.Content.ReadAsByteArrayAsync(),
            [["名称", "ignored", "级别", "编号", "单位", "顺序"], ["不得写入", "", "", queuedCode, "", "0"]]);
        var noBinding = await UploadAsync(client, queuedBytes, configuration, HttpStatusCode.Created); var noBindingId = noBinding.GetProperty("id").GetGuid();
        await SendAsync(client, HttpMethod.Post, $"{Tasks}/{noBindingId:D}/execute", null, HttpStatusCode.OK);
        await database.ExecuteAsync("UPDATE fn_import_export_task SET ActorSessionBindingJson=NULL WHERE Id=@Id", new { Id = noBindingId });
        await ProcessPendingAsync();
        var failed = await SendAsync(client, HttpMethod.Get, $"{Tasks}/{noBindingId:D}", null, HttpStatusCode.OK);
        Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, failed.GetProperty("errorCode").GetString());
        // 无绑定的历史队列不能自动升级；原上传者的明确重试才可绑定其当前有效会话。
        await SendAsync(client, HttpMethod.Post, $"{Tasks}/{noBindingId:D}/retry", null, HttpStatusCode.OK); await ProcessPendingAsync();
        Assert.AreEqual(ImportExportTaskStatusKeys.ExecutionSucceeded, (await SendAsync(client, HttpMethod.Get, $"{Tasks}/{noBindingId:D}", null, HttpStatusCode.OK)).GetProperty("statusKey").GetString());
        var revoked = await UploadAsync(client, queuedBytes, configuration, HttpStatusCode.Created); var revokedId = revoked.GetProperty("id").GetGuid();
        await SendAsync(client, HttpMethod.Post, $"{Tasks}/{revokedId:D}/execute", null, HttpStatusCode.OK);
        await database.ExecuteAsync("UPDATE fn_identity_user SET SecurityStamp=@Stamp WHERE Id=@Id", new { Id = owner.UserId, Stamp = Guid.CreateVersion7().ToString("N") });
        await ProcessPendingAsync();
        Assert.AreEqual(ImportTaskAuthorityErrorCodes.Denied, await database.ExecuteScalarAsync<string>("SELECT ErrorCode FROM fn_import_export_task WHERE Id=@Id", new { Id = revokedId }));
        Assert.AreEqual(2, await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_organization_position WHERE TenantId=@TenantId AND Code IN (@First,@Second)", new { TenantId = tenant.Id, First = code, Second = queuedCode }));

        async Task ProcessPendingAsync()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.AreEqual(1, await scope.ServiceProvider.GetRequiredService<ImportExportTaskRunner>().ProcessPendingAsync(default));
        }
        // 部分 DDL 已存在时重放两次，保留配置和任务快照，不覆盖数据。
        await database.ExecuteAsync("DROP TABLE fn_import_export_profile_scope");
        var migration = await File.ReadAllTextAsync(Path.Combine(FullNetApiFactory.FindRepositoryRoot(), "src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations",
            provider == DatabaseProvider.SqlServer ? "SqlServer" : "MySql", "357_ImportProfilesAndInputSnapshots.sql"));
        // 仅部署连接允许固定迁移脚本的用户变量，业务连接继续保持禁用。
        await using System.Data.Common.DbConnection recovery = provider == DatabaseProvider.MySql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(connection, MySqlGuidStorageMode.Binary16, allowUserVariables: true))
            : MyTasksApiTests.Open(provider, connection);
        await recovery.OpenAsync();
        for (var repeat = 0; repeat < 2; repeat++)
            foreach (var batch in provider == DatabaseProvider.SqlServer ? Regex.Split(migration, @"(?im)^GO\s*$") : [migration])
                if (!string.IsNullOrWhiteSpace(batch)) await recovery.ExecuteAsync(batch);
        Assert.AreEqual(32, await database.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM fn_import_export_profile WHERE TenantId=@TenantId AND OwnerUserId=@UserId", new { TenantId = tenant.Id, UserId = owner.UserId }));

        // 同一用户加入第二租户后仍看不到原租户的个人配置，不能仅靠所有者谓词形成假隔离。
        admin.DefaultRequestHeaders.Authorization = null; adminHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(admin, "admin");
        admin.DefaultRequestHeaders.Authorization = new("Bearer", adminHost);
        var identifier = "profiles-" + Guid.CreateVersion7().ToString("N")[..12];
        var otherTenant = await SendAsync(admin, HttpMethod.Post, "/api/v1/tenancy/tenants", new ProvisionTenantRequest(identifier, identifier, identifier + ".localhost"), HttpStatusCode.Created);
        var otherTenantId = otherTenant.GetProperty("id").GetGuid();
        admin.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(admin, adminHost, otherTenantId));
        var nextInvitation = await SendAsync(admin, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(owner.Email, TenantMemberRoles.Member), HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = null; var freshOwnerHost = await IntegrationTestAuthHelper.LoginAsHostUserAsync(client, owner.Username);
        client.DefaultRequestHeaders.Authorization = new("Bearer", freshOwnerHost);
        await SendAsync(client, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{nextInvitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(client, freshOwnerHost, otherTenantId));
        Assert.AreEqual(0, (await SendAsync(client, HttpMethod.Get, Base, null, HttpStatusCode.OK)).GetArrayLength());
        await SendAsync(client, HttpMethod.Get, $"{Base}/{staleId:D}", null, HttpStatusCode.NotFound);

        async Task<HttpStatusCode> CreateLastAsync() { using var result = await client.PostAsJsonAsync(Base, new SaveImportProfileRequest("最后一项", configuration, 0)); return result.StatusCode; }
        async Task<(HttpClient Client, Guid UserId, string Username, string Email)> MemberAsync(string suffix, string[] permissions)
        {
            var username = "profiles." + suffix + "." + Guid.CreateVersion7().ToString("N"); var identity = await factory.CreateHostIdentityAsync(username, permissions, password: FullNetApiFactory.TestPassword);
            var email = username + "@example.com"; await factory.EnsureHostUserProfileEmailAsync(identity.UserId, email, username);
            var invitation = await SendAsync(admin, HttpMethod.Post, "/api/v1/identity/tenant-members/invitations", new CreateTenantInvitationRequest(email, TenantMemberRoles.Member), HttpStatusCode.OK);
            var member = factory.CreateClientForHost("localhost"); var host = await IntegrationTestAuthHelper.LoginAsHostUserAsync(member, username); member.DefaultRequestHeaders.Authorization = new("Bearer", host);
            await SendAsync(member, HttpMethod.Post, $"/api/v1/me/tenant-invitations/{invitation.GetProperty("invitation").GetProperty("id").GetGuid():D}/accept", new { }, HttpStatusCode.OK);
            member.DefaultRequestHeaders.Authorization = new("Bearer", await IntegrationTestTenantContextHelper.SwitchToTenantAsync(member, host, tenant.Id)); return (member, identity.UserId, username, email);
        }
    }

    internal static ImportColumnMappingRequest Mapping() => new(Headers, [new("code", "编号"), new("name", "名称"), new("displayOrder", "顺序"), new("unitCode", "单位"), new("positionLevelCode", "级别")]);
    internal static byte[] Workbook(byte[] template, string[][] rows)
    {
        using var buffer = new MemoryStream(); buffer.Write(template);
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("xl/worksheets/sheet1.xml")!; entry.Delete();
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var document = new XDocument(new XElement(ns + "worksheet", new XElement(ns + "sheetData", rows.Select((row, rowIndex) =>
                new XElement(ns + "row", new XAttribute("r", rowIndex + 1), row.Select((value, column) =>
                    new XElement(ns + "c", new XAttribute("r", $"{(char)('A' + column)}{rowIndex + 1}"), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)))))))));
            using var output = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); document.Save(output);
        }
        return buffer.ToArray();
    }
    private static async Task<JsonElement> UploadAsync(HttpClient client, byte[] bytes, ImportColumnMappingRequest configuration, HttpStatusCode expected)
    {
        using var form = new MultipartFormDataContent { { new StringContent("organization.tenant_positions"), "schemaKey" }, { new StringContent("positions"), "worksheetKey" },
            { new StringContent(JsonSerializer.Serialize(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8), "columnMapping" }, { new ByteArrayContent(bytes), "file", "positions.xlsx" } };
        using var response = await client.PostAsync(Tasks, form); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, json);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path); if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request); var json = await response.Content.ReadAsStringAsync(); Assert.AreEqual(expected, response.StatusCode, path + " " + json);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}

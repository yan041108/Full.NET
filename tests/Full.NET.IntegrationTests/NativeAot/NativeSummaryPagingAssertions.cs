using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>只在 JIT 夹具播种；所有分页、版本与正文读取均通过实际原生 HTTP 进程。</summary>
internal static class NativeSummaryPagingAssertions
{
    internal static async Task VerifyAsync(DatabaseProvider provider, string connectionString,
        HttpClient client, string accessToken, CancellationToken token)
    {
        await using DbConnection connection = provider == DatabaseProvider.MySql
            ? new MySqlConnection(MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, false))
            : new SqlConnection(connectionString);
        var prefix = "native-summary-" + Guid.CreateVersion7().ToString("N");
        var canvas = "{\"width\":1,\"height\":1,\"components\":[],\"payload\":\"" + new string('x', 32768) + "\"}";
        var html = "<p>" + new string('x', 32768) + "</p>";
        var layout = "{\"payload\":\"" + new string('x', 32768) + "\"}";
        var groupId = Guid.CreateVersion7();
        object now = provider == DatabaseProvider.MySql ? DateTime.UtcNow : DateTimeOffset.UtcNow;
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO fn_reporting_group (Id, ParentId, Name, SortOrder, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
            VALUES (@Id, NULL, @Name, 0, 1, @Now, NULL, 1)
            """, new { Id = groupId, Name = prefix, Now = now }, cancellationToken: token)).ConfigureAwait(false);

        var firstProject = Guid.Empty;
        var firstTemplate = Guid.Empty;
        var firstDefinition = Guid.Empty;
        for (var index = 0; index < 2; index++)
        {
            var projectId = Guid.CreateVersion7();
            var templateId = Guid.CreateVersion7();
            var definitionId = Guid.CreateVersion7();
            if (index == 0) (firstProject, firstTemplate, firstDefinition) = (projectId, templateId, definitionId);
            var parameters = new { ProjectId = projectId, TemplateId = templateId, DefinitionId = definitionId,
                GroupId = groupId, DataSourceId = Guid.CreateVersion7(), Key = prefix + index, Name = prefix + index,
                Canvas = canvas, Html = html, Layout = layout, Now = now, Actor = Guid.CreateVersion7() };
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO fn_goview_project (Id, ProjectKey, Name, CanvasJson, LatestPublishedVersionNumber, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES (@ProjectId, @Key, @Name, @Canvas, 2, 1, @Now, NULL, 1);
                INSERT INTO fn_printing_template (Id, TemplateKey, Name, FormSchemaKey, LayoutHtml, LatestPublishedVersionNumber, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES (@TemplateId, @Key, @Name, 'printing.tenant_profile_card', @Html, 2, 1, @Now, NULL, 1);
                INSERT INTO fn_reporting_definition (Id, GroupId, DataSourceId, DefinitionKey, Name, Description, QueryPortKey, ParameterSchemaJson, LayoutConfigJson, LatestPublishedVersionNumber, IsEnabled, CreatedAtUtc, UpdatedAtUtc, Version)
                VALUES (@DefinitionId, @GroupId, @DataSourceId, @Key, @Name, NULL, 'reporting.database_engine_version', '[]', @Layout, 2, 1, @Now, NULL, 1)
                """, parameters, cancellationToken: token)).ConfigureAwait(false);
            for (var version = 1; version <= 2; version++)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO fn_goview_project_version (Id, ProjectId, VersionNumber, CanvasJson, ChangeNote, PublishedByUserId, PublishedAtUtc)
                    VALUES (@ProjectVersionId, @ProjectId, @VersionNumber, @Canvas, NULL, @Actor, @Now);
                    INSERT INTO fn_printing_template_version (Id, TemplateId, VersionNumber, LayoutHtml, ChangeNote, PublishedByUserId, PublishedAtUtc)
                    VALUES (@TemplateVersionId, @TemplateId, @VersionNumber, @Html, NULL, @Actor, @Now);
                    INSERT INTO fn_reporting_definition_version (Id, DefinitionId, VersionNumber, DataSourceId, QueryPortKey, ParameterSchemaJson, LayoutConfigJson, ChangeNote, PublishedByUserId, PublishedAtUtc)
                    VALUES (@DefinitionVersionId, @DefinitionId, @VersionNumber, @DataSourceId, 'reporting.database_engine_version', '[]', @Layout, NULL, @Actor, @Now)
                    """, new { ProjectVersionId = Guid.CreateVersion7(), TemplateVersionId = Guid.CreateVersion7(),
                        DefinitionVersionId = Guid.CreateVersion7(), parameters.ProjectId, parameters.TemplateId,
                        parameters.DefinitionId, parameters.DataSourceId, parameters.Canvas, parameters.Html,
                        parameters.Layout, parameters.Actor, parameters.Now, VersionNumber = version }, cancellationToken: token)).ConfigureAwait(false);
            }
        }

        foreach (var (path, id, bodyField, expected) in new[]
        {
            ("goview/projects", firstProject, "canvasJson", canvas),
            ("printing/templates", firstTemplate, "layoutHtml", html),
            ("reporting/definitions", firstDefinition, "layoutConfigJson", layout)
        })
        {
            var root = "/api/v1/" + path;
            using var first = await ReadAsync(root + $"?nameContains={prefix}&page=1&pageSize=1");
            using var second = await ReadAsync(root + $"?nameContains={prefix}&page=2&pageSize=1");
            Assert.AreEqual(2, first.RootElement.GetProperty("total").GetInt32());
            var row = first.RootElement.GetProperty("items").EnumerateArray().Single();
            Assert.AreEqual(id, row.GetProperty("id").GetGuid());
            Assert.IsFalse(row.TryGetProperty(bodyField, out _));
            var next = second.RootElement.GetProperty("items").EnumerateArray().Single();
            Assert.AreNotEqual(id, next.GetProperty("id").GetGuid());
            Assert.IsLessThan(2048, first.RootElement.GetRawText().Length);
            using var detail = await ReadAsync(root + $"/{id}");
            Assert.AreEqual(expected, detail.RootElement.GetProperty(bodyField).GetString());
            for (var page = 1; page <= 2; page++)
            {
                using var versions = await ReadAsync(root + $"/{id}/versions?page={page}&pageSize=1");
                Assert.AreEqual(2, versions.RootElement.GetProperty("total").GetInt32());
                var version = versions.RootElement.GetProperty("items").EnumerateArray().Single();
                Assert.AreEqual(3 - page, version.GetProperty("versionNumber").GetInt32());
                Assert.IsFalse(version.TryGetProperty(bodyField, out _));
            }
            using var snapshot = await ReadAsync(root + $"/{id}/versions/1");
            Assert.AreEqual(expected, snapshot.RootElement.GetProperty(bodyField).GetString());
        }
        using var groups = await ReadAsync("/api/v1/reporting/groups");
        Assert.IsTrue(groups.RootElement.EnumerateArray().Any(row => row.GetProperty("id").GetGuid() == groupId));

        async Task<JsonDocument> ReadAsync(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, token).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path + ": " + json);
            return JsonDocument.Parse(json);
        }
    }
}

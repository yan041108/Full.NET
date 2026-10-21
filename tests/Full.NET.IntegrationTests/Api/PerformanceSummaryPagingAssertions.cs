using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.GoView.Features.ManageProjects;
using Full.NET.Modules.GoView.Persistence;
using Full.NET.Modules.Printing.Features.ManageTemplates;
using Full.NET.Modules.Printing.Persistence;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Api;

/// <summary>相同数据和断言覆盖双库正文隔离、稳定分页以及按需明细，不依赖空表 Smoke。</summary>
internal static class PerformanceSummaryPagingAssertions
{
    internal static async Task VerifyAsync(ICommandExecutor commands, IQueryExecutor queries,
        DatabaseProvider provider, Action<string> evidence)
    {
        var now = DateTimeOffset.UtcNow;
        var prefix = "summary-" + Guid.CreateVersion7().ToString("N");
        var canvas = "{\"content\":\"" + new string('x', 32768) + "\"}";
        var layout = "<p>" + new string('x', 32768) + "</p>";
        var projectId = Guid.Empty;
        var templateId = Guid.Empty;
        for (var index = 0; index < 21; index++)
        {
            var project = Guid.CreateVersion7();
            var template = Guid.CreateVersion7();
            if (index == 0) { projectId = project; templateId = template; }
            var common = new Dictionary<string, object?>
            {
                ["Id"] = project, ["ProjectKey"] = prefix + index, ["Name"] = prefix + index.ToString("D2"),
                ["CanvasJson"] = canvas, ["LatestPublishedVersionNumber"] = 0, ["IsEnabled"] = true,
                ["CreatedAtUtc"] = now, ["UpdatedAtUtc"] = null, ["Version"] = 1
            };
            await commands.ExecuteAsync(GoViewProjectSql.InsertProject, common);
            common["Id"] = template; common["TemplateKey"] = prefix + index;
            common["FormSchemaKey"] = "summary-test"; common["LayoutHtml"] = layout;
            await commands.ExecuteAsync(PrintingTemplateSql.InsertTemplate, common);
        }

        var database = Options.Create(new DatabaseOptions { Provider = provider });
        var projects = new GoViewProjectQueryService(queries, database);
        var templates = new PrintingTemplateQueryService(queries, database);
        var projectPage = await projects.ListAsync(prefix, page: 1, pageSize: 2);
        var nextProjects = await projects.ListAsync(prefix, page: 2, pageSize: 2);
        Assert.IsTrue(projectPage.IsSuccess); Assert.IsTrue(nextProjects.IsSuccess);
        Assert.AreEqual(21L, projectPage.Value!.Total); Assert.HasCount(2, projectPage.Value.Items);
        Assert.HasCount(4, projectPage.Value.Items.Concat(nextProjects.Value!.Items).Select(row => row.Id).Distinct().ToArray());
        Assert.IsLessThan(2048, JsonSerializer.Serialize(projectPage.Value).Length);
        Assert.AreEqual(canvas, (await projects.GetByIdAsync(projectId)).Value!.CanvasJson);
        Assert.HasCount(1, (await projects.ListAsync(prefix, page: 11, pageSize: 2)).Value!.Items);

        var templatePage = await templates.ListAsync(prefix, page: 1, pageSize: 2);
        var nextTemplates = await templates.ListAsync(prefix, page: 2, pageSize: 2);
        Assert.IsTrue(templatePage.IsSuccess); Assert.IsTrue(nextTemplates.IsSuccess);
        Assert.AreEqual(21L, templatePage.Value!.Total); Assert.HasCount(2, templatePage.Value.Items);
        Assert.HasCount(4, templatePage.Value.Items.Concat(nextTemplates.Value!.Items).Select(row => row.Id).Distinct().ToArray());
        Assert.IsLessThan(2048, JsonSerializer.Serialize(templatePage.Value).Length);
        Assert.AreEqual(layout, (await templates.GetByIdAsync(templateId)).Value!.LayoutHtml);
        Assert.HasCount(1, (await templates.ListAsync(prefix, page: 11, pageSize: 2)).Value!.Items);

        for (var version = 1; version <= 3; version++)
        {
            var parameters = new Dictionary<string, object?>
            {
                ["Id"] = Guid.CreateVersion7(), ["ProjectId"] = projectId, ["TemplateId"] = templateId,
                ["VersionNumber"] = version, ["CanvasJson"] = canvas, ["LayoutHtml"] = layout,
                ["ChangeNote"] = null, ["PublishedByUserId"] = Guid.CreateVersion7(), ["PublishedAtUtc"] = now
            };
            await commands.ExecuteAsync(GoViewProjectSql.InsertVersion, parameters);
            parameters["Id"] = Guid.CreateVersion7();
            await commands.ExecuteAsync(PrintingTemplateSql.InsertVersion, parameters);
        }
        var projectVersions = await projects.ListVersionsAsync(projectId, page: 2, pageSize: 2);
        var templateVersions = await templates.ListVersionsAsync(templateId, page: 2, pageSize: 2);
        Assert.AreEqual(3L, projectVersions.Value!.Total); Assert.HasCount(1, projectVersions.Value.Items);
        Assert.AreEqual(3L, templateVersions.Value!.Total); Assert.HasCount(1, templateVersions.Value.Items);
        Assert.IsLessThan(2048, JsonSerializer.Serialize(projectVersions.Value).Length);
        Assert.IsLessThan(2048, JsonSerializer.Serialize(templateVersions.Value).Length);
        Assert.AreEqual(canvas, (await projects.GetVersionAsync(projectId, 1)).Value!.CanvasJson);
        Assert.AreEqual(layout, (await templates.GetVersionAsync(templateId, 1)).Value!.LayoutHtml);
        evidence($"summary_paging provider={provider} projects=21 templates=21 versions_each=3 body_bytes=32768 page_size=2 detail_preserved=verified");
    }
}

using System.Text.Json;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Contracts;
using Full.NET.Modules.Workflow.Features.FormAttachments;
using Full.NET.Modules.Workflow.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Workflow;

/// <summary>待办附件只按精确本人视图读取，实例身份和同实例其他视图不能替代该资源边界。</summary>
[TestClass]
public sealed class WorkflowTodoAttachmentAccessTests
{
    private const string Schema = """{"schemaVersion":1,"adapterVersion":1,"sections":[{"sectionKey":"main","fields":[{"fieldKey":"file","fieldTypeKey":"attachment","required":false,"constraints":{"maxCount":1,"maxSizeBytes":1024,"allowedExtensions":["png"]}}]}]}""";

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer, true, true)]
    [DataRow(DatabaseProvider.MySql, true, true)]
    [DataRow(DatabaseProvider.SqlServer, false, true)]
    [DataRow(DatabaseProvider.MySql, false, true)]
    [DataRow(DatabaseProvider.SqlServer, true, false)]
    [DataRow(DatabaseProvider.MySql, true, false)]
    public async Task Personal_main_attachment_requires_its_exact_visible_todo(DatabaseProvider provider, bool visible, bool ownsTodo)
    {
        var query = Substitute.For<IQueryExecutor>(); var files = Files(); var todo = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var instance = Guid.CreateVersion7(); var version = Guid.CreateVersion7(); var file = Guid.CreateVersion7();
        var statement = provider == DatabaseProvider.SqlServer ? WorkflowFormSubmissionAttachmentSql.ReadTodoViewSqlServer : WorkflowFormSubmissionAttachmentSql.ReadTodoViewMySql;
        if (ownsTodo) query.QuerySingleOrDefaultAsync<WorkflowTodoAttachmentReadViewRecord>(statement, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoAttachmentReadViewRecord(instance, version, "task", JsonSerializer.Serialize(new { file = new[] { file } }), null));
        query.QuerySingleOrDefaultAsync<int>(WorkflowFormSubmissionAttachmentSql.ExistsForInstanceFile, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(1);
        var canonical = JsonSerializer.Serialize(new { nodes = new[] { new { nodeKey = "task", nodeTypeKey = "human.task", config = new { fieldPolicies = new { file = visible ? "readOnly" : "hidden" } } } } });
        query.QuerySingleOrDefaultAsync<WorkflowRuntimeAssetRecord>(WorkflowSql.FindRuntimeAsset, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowRuntimeAssetRecord(version, Guid.CreateVersion7(), canonical, Schema, null, "active"));
        var result = await Service(query, files, provider).OpenTodoAttachmentAsync(todo, file, actor, "host");
        Assert.AreEqual(visible && ownsTodo, result.IsSuccess);
        await query.Received(1).QuerySingleOrDefaultAsync<WorkflowTodoAttachmentReadViewRecord>(statement,
            Arg.Is<object?>(value => Bound(value, "TodoId", todo) && Bound(value, "ActorUserId", actor) && Bound(value, "TenantScopeKey", "host")), Arg.Any<CancellationToken>());
        await files.Received(visible && ownsTodo ? 1 : 0).OpenReadyContentAsync(file, Arg.Any<CancellationToken>());
        if (result.IsSuccess) await result.Value!.Content.DisposeAsync();
    }

    [TestMethod]
    [DataRow(DatabaseProvider.SqlServer)]
    [DataRow(DatabaseProvider.MySql)]
    public async Task Private_ballot_content_is_bound_to_the_requested_todo(DatabaseProvider provider)
    {
        var query = Substitute.For<IQueryExecutor>(); var files = Files(); var todo = Guid.CreateVersion7(); var actor = Guid.CreateVersion7();
        var file = Guid.CreateVersion7(); var instance = Guid.CreateVersion7();
        var personal = provider == DatabaseProvider.SqlServer ? WorkflowFormSubmissionAttachmentSql.ReadTodoViewSqlServer : WorkflowFormSubmissionAttachmentSql.ReadTodoViewMySql;
        var ballot = provider == DatabaseProvider.SqlServer ? WorkflowVoteAttachmentSql.ReadOwnedForTodoSqlServer : WorkflowVoteAttachmentSql.ReadOwnedForTodoMySql;
        query.QuerySingleOrDefaultAsync<WorkflowTodoAttachmentReadViewRecord>(personal, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowTodoAttachmentReadViewRecord(instance, Guid.CreateVersion7(), "vote", "{}", null));
        query.QueryAsync<WorkflowVoteAttachmentViewRecord>(ballot, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkflowVoteAttachmentViewRecord(null, Schema, JsonSerializer.Serialize(new { choiceKey = "yes", values = new { file = new[] { file } } })) });
        var result = await Service(query, files, provider).OpenTodoAttachmentAsync(todo, file, actor, "host");
        Assert.IsTrue(result.IsSuccess);
        await query.Received(1).QueryAsync<WorkflowVoteAttachmentViewRecord>(ballot,
            Arg.Is<object?>(value => Bound(value, "TodoId", todo) && Bound(value, "ActorUserId", actor) && Bound(value, "InstanceId", instance)), Arg.Any<CancellationToken>());
        await result.Value!.Content.DisposeAsync();
    }

    private static bool Bound(object? parameters, string key, object expected) => parameters is IReadOnlyDictionary<string, object?> map && map.TryGetValue(key, out var value) && Equals(value, expected);
    private static IHostFileContentReader Files()
    {
        var files = Substitute.For<IHostFileContentReader>();
        files.OpenReadyContentAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => Result<HostFileContent>.Success(new(new MemoryStream(), "image/png", "private.png")));
        return files;
    }
    private static WorkflowFormAttachmentAccessService Service(IQueryExecutor query, IHostFileContentReader files, DatabaseProvider provider) =>
        new(query, files, Options.Create(new DatabaseOptions { Provider = provider }), Substitute.For<ITenantResourceFileStore>());
}

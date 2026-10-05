using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Full.NET.Data.Abstractions;
using Full.NET.IntegrationTests.Api;
using Full.NET.Modules.Ai.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实并发删除只能递增一次版本；后续收据确认不改墓碑或放宽归属。</summary>
internal static class AiKnowledgeDeleteReplayAssertions
{
    internal static async Task VerifyAsync(FullNetApiFactory factory, HttpClient client, HttpClient other, string basePath, Gate gate)
    {
        using var created = await client.PostAsJsonAsync(basePath + "/documents", new CreateAiKnowledgeDocumentRequest("删除重放", null));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode, await created.Content.ReadAsStringAsync());
        var document = (await created.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var path = basePath + $"/documents/{document.Id}";
        gate.DocumentId = document.Id;
        gate.Readers = 0;
        gate.Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var responses = await Task.WhenAll(Delete(client, path, 1), Delete(client, path, 1));
            try
            {
                foreach (var response in responses)
                    Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode, await response.Content.ReadAsStringAsync());
                Assert.AreEqual(2, gate.Readers, "两个请求必须先读取同一存活版本再竞争删除");
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        finally { gate.Ready.TrySetResult(true); gate.Ready = null; }

        await using var db = AiOperationBudgetAssertions.Connection(factory);
        var parameters = new { document.Id };
        Assert.AreEqual(2, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id", parameters));
        Assert.AreEqual(1, await db.QuerySingleAsync<int>("SELECT IsDeleted FROM fn_ai_knowledge_document WHERE Id=@Id", parameters));
        var before = await Snapshot();
        for (var index = 0; index < 2; index++)
        {
            using var repeated = await Delete(client, path, 1);
            Assert.AreEqual(HttpStatusCode.NoContent, repeated.StatusCode, await repeated.Content.ReadAsStringAsync());
            Assert.AreEqual(string.Empty, await repeated.Content.ReadAsStringAsync());
        }
        foreach (var version in new[] { -1, 0, 2, 3, int.MaxValue })
        {
            using var invalid = await Delete(client, path, version);
            await Problem(invalid, version < 1 ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict,
                version < 1 ? AiKnowledgeErrorCodes.DocumentInvalid : AiKnowledgeErrorCodes.DocumentVersionConflict);
        }
        using (var invisible = await Delete(other, path, 1))
            await Problem(invisible, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using (var missing = await Delete(client, basePath + $"/documents/{Guid.CreateVersion7()}", 1))
            await Problem(missing, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        using (var wrongParent = await Delete(client, $"/api/v1/ai/knowledge-bases/{Guid.CreateVersion7()}/documents/{document.Id}", 1))
            await Problem(wrongParent, HttpStatusCode.NotFound, AiKnowledgeErrorCodes.DocumentNotFound);
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=0 WHERE Id=@Id", new { Id = document.KnowledgeBaseId });
        try
        {
            using var disabled = await Delete(client, path, 1);
            Assert.AreEqual(HttpStatusCode.NoContent, disabled.StatusCode, await disabled.Content.ReadAsStringAsync());
        }
        finally { await db.ExecuteAsync("UPDATE fn_ai_knowledge_base SET IsEnabled=1 WHERE Id=@Id", new { Id = document.KnowledgeBaseId }); }
        Assert.AreEqual(before, await Snapshot(), "重放和拒绝请求不得更新墓碑版本或时间");
        using var get = await client.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.NotFound, get.StatusCode);

        using var raceCreated = await client.PostAsJsonAsync(basePath + "/documents", new CreateAiKnowledgeDocumentRequest("更新先提交", null));
        Assert.AreEqual(HttpStatusCode.Created, raceCreated.StatusCode);
        var race = (await raceCreated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var racePath = basePath + $"/documents/{race.Id}";
        gate.DocumentId = race.Id;
        gate.DeleteEntering = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.ContinueDelete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingDelete = Delete(client, racePath, 1);
        HttpResponseMessage? conflict = null;
        try
        {
            await gate.DeleteEntering.Task.WaitAsync(TimeSpan.FromSeconds(20));
            using var update = await client.PutAsJsonAsync(racePath, new UpdateAiKnowledgeDocumentRequest("保留更新", null, 1));
            Assert.AreEqual(HttpStatusCode.OK, update.StatusCode, await update.Content.ReadAsStringAsync());
            gate.ContinueDelete.TrySetResult(true);
            conflict = await pendingDelete;
            await Problem(conflict, HttpStatusCode.Conflict, AiKnowledgeErrorCodes.DocumentVersionConflict);
        }
        finally
        {
            gate.ContinueDelete.TrySetResult(true);
            try { conflict ??= await pendingDelete; }
            finally { conflict?.Dispose(); gate.DeleteEntering = null; gate.ContinueDelete = null; }
        }
        var updated = (await client.GetFromJsonAsync<AiKnowledgeDocumentResponse>(racePath))!;
        Assert.AreEqual(2, updated.Version);
        Assert.AreEqual("保留更新", updated.Title);
        using (var deleted = await Delete(client, racePath, 2)) Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var replayed = await Delete(client, racePath, 2)) Assert.AreEqual(HttpStatusCode.NoContent, replayed.StatusCode);

        using var limitCreated = await client.PostAsJsonAsync(basePath + "/documents", new CreateAiKnowledgeDocumentRequest("版本上界", null));
        Assert.AreEqual(HttpStatusCode.Created, limitCreated.StatusCode);
        var limit = (await limitCreated.Content.ReadFromJsonAsync<AiKnowledgeDocumentResponse>())!;
        var limitPath = basePath + $"/documents/{limit.Id}";
        await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET Version=@Version WHERE Id=@Id", new { limit.Id, Version = int.MaxValue - 1 });
        using (var deleted = await Delete(client, limitPath, int.MaxValue - 1)) Assert.AreEqual(HttpStatusCode.NoContent, deleted.StatusCode);
        using (var replayed = await Delete(client, limitPath, int.MaxValue - 1)) Assert.AreEqual(HttpStatusCode.NoContent, replayed.StatusCode);
        Assert.AreEqual(int.MaxValue, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_document WHERE Id=@Id", new { limit.Id }));

        async Task<string> Snapshot() => JsonSerializer.Serialize(await db.QueryAsync(
            "SELECT Version,UpdatedAtUtc,IsDeleted FROM fn_ai_knowledge_document WHERE Id=@Id", parameters));
    }

    internal static async Task<HttpResponseMessage> Delete(HttpClient client, string path, int version)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, path) { Content = JsonContent.Create(new DeleteAiKnowledgeDocumentRequest(version)) };
        return await client.SendAsync(request);
    }

    private static async Task Problem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.AreEqual(status, response.StatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(code, json.RootElement.GetProperty("code").GetString());
    }

    /// <summary>只阻塞目标文档的首次读取；真实 SQL 和事务仍由生产执行器执行。</summary>
    internal sealed class Gate
    {
        internal Guid DocumentId;
        internal int Readers;
        internal TaskCompletionSource<bool>? Ready;
        internal TaskCompletionSource<bool>? DeleteEntering;
        internal TaskCompletionSource<bool>? ContinueDelete;

        internal void Configure(IServiceCollection services)
        {
            var descriptor = services.Last(x => x.ServiceType == typeof(IQueryExecutor));
            services.RemoveAll<IQueryExecutor>();
            services.AddScoped<IQueryExecutor>(provider => new Queries((IQueryExecutor)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), this));
            var commands = services.Last(x => x.ServiceType == typeof(ICommandExecutor));
            services.RemoveAll<ICommandExecutor>();
            services.AddScoped<ICommandExecutor>(provider => new Commands((ICommandExecutor)(commands.ImplementationInstance
                ?? commands.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, commands.ImplementationType!)), this));
        }

        private sealed class Commands(ICommandExecutor inner, Gate gate) : ICommandExecutor
        {
            public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            {
                if (statement.Name is "ai.knowledge_document.delete_host" or "ai.knowledge_document.delete_tenant"
                    && parameters is IReadOnlyDictionary<string, object?> values
                    && values.TryGetValue("DocumentId", out var id) && Equals(id, gate.DocumentId)
                    && gate.DeleteEntering is { } entering)
                {
                    entering.TrySetResult(true);
                    await gate.ContinueDelete!.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
                return await inner.ExecuteAsync(statement, parameters, cancellationToken);
            }
        }

        private sealed class Queries(IQueryExecutor inner, Gate gate) : IQueryExecutor
        {
            public async Task<T?> QuerySingleOrDefaultAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default)
            {
                var result = await inner.QuerySingleOrDefaultAsync<T>(statement, parameters, cancellationToken);
                if (statement.Name.StartsWith("ai.knowledge_document.find_", StringComparison.Ordinal)
                    && parameters is IReadOnlyDictionary<string, object?> values
                    && values.TryGetValue("DocumentId", out var id) && Equals(id, gate.DocumentId) && gate.Ready is { } ready)
                {
                    if (Interlocked.Increment(ref gate.Readers) == 2) ready.TrySetResult(true);
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
                return result;
            }

            public Task<IReadOnlyList<T>> QueryAsync<T>(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) =>
                inner.QueryAsync<T>(statement, parameters, cancellationToken);
        }
    }
}

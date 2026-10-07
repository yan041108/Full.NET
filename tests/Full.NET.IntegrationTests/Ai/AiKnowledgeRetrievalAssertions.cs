using System.Data.Common;
using System.Text;
using Dapper;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Abstractions.Results;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Features;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>真实生产 SQL/载荷和完整代次证明；读取身份受控，不宣称真实登录或云模型问答。</summary>
internal static class AiKnowledgeRetrievalAssertions
{
    internal static async Task VerifyAsync(IServiceProvider services, DbConnection db, AiKnowledgeIndexTaskBinding binding)
    {
        var query = services.GetRequiredService<IQueryExecutor>(); var tenant = services.GetRequiredService<ICurrentTenant>();
        var state = services.GetRequiredService<IDataTransactionState>(); var options = services.GetRequiredService<IOptions<DatabaseOptions>>();
        var ids = services.GetRequiredService<IIdGenerator>(); var clock = services.GetRequiredService<IClock>();
        var command = services.GetRequiredService<ICommandExecutor>(); var transaction = services.GetRequiredService<ICommandTransaction>();
        var reader = new Reader(binding.OwnerUserId, tenant.Id, state);
        var relationships = new Relationships(state);
        var access = new AiKnowledgeReadAccess(relationships, relationships, tenant);
        var permission = new AiKnowledgePermissionGuard(reader, new HttpContextAccessor(), new ApiKeyAuthorization());
        var tasks = new AiKnowledgeIndexTaskStore(query, command, tenant, clock, ids); var offlineWriter = new OfflineWriter();
        var service = new AiKnowledgeRetrievalService(query, state, tenant, access, permission,
            new(query, command, transaction, state, tenant, clock, ids, tasks, offlineWriter, options),
            new(query, command, transaction, state, tenant, clock, ids, tasks, offlineWriter, options), options);
        var modelId = await db.QuerySingleAsync<Guid>("SELECT EmbeddingModelConfigId FROM fn_ai_knowledge_base WHERE Id=@Id", new { Id = binding.KnowledgeBaseId });
        var model = await query.QuerySingleOrDefaultAsync<AiModelConfigRecord>(tenant.Id.HasValue ? AiModelConfigSql.FindAvailableForTenantChat : AiModelConfigSql.FindAvailableForHostChat,
            new Dictionary<string, object?> { ["ModelConfigId"] = modelId }); Assert.IsNotNull(model);
        var vector = new float[model.ModelId == "text-embedding-3-small" ? 1536 : 3072]; vector[0] = 1;
        var result = await service.RetrieveAsync([binding.KnowledgeBaseId], reader.Actor, model, vector, default);
        Assert.IsTrue(result.Chunks.Count > 1); Assert.AreEqual(binding.VersionId, result.Sources[0].VersionId);
        Assert.IsTrue(result.Chunks.All(c => c.Chunk.Source.ModelConfigId == model.Id));
        Assert.IsTrue(result.Chunks.All(c => c.Chunk.PageNumber.HasValue == (c.Chunk.Source.ContentType == "application/pdf")));
        var pipeline = new AiKnowledgeAnswerPipeline(service, ids);
        // 文本夹具包含八个满额块，整包超过 4096 Token 必须拒绝；单个完整块仍可形成有界问答包。
        var context = AiKnowledgeAnswerContext.Create("来源说明", result.Chunks.Take(1).ToArray(), ids, default);
        var prepared = result.Sources[0].ContentType == "application/pdf"
            ? await pipeline.PrepareAsync([binding.KnowledgeBaseId], reader.Actor, model, vector, "来源说明", default)
            : new AiKnowledgePreparedAnswer(result, context);
        var bytes = Encoding.UTF8.GetBytes($$"""{"answer":"测试答案","citationIds":["{{prepared.Context.Citations[0].Id}}"],"refusal":false}""");
        Assert.AreEqual("测试答案", (await pipeline.AcceptAsync(prepared, bytes, default)).Text);
        reader.Denied = true;
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => pipeline.AcceptAsync(prepared, bytes, default)); reader.Denied = false;
        // 两层必须同时具备；只有目录成员关系仍不能召回正文。
        reader.Actor = Guid.CreateVersion7();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await service.RetrieveAsync([binding.KnowledgeBaseId], reader.Actor, model, vector, default));
        var member = new { Id = Guid.CreateVersion7(), KnowledgeBaseId = binding.KnowledgeBaseId, UserId = reader.Actor, Now = clock.UtcNow };
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@KnowledgeBaseId,@UserId,@Now)", member);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await service.RetrieveAsync([binding.KnowledgeBaseId], reader.Actor, model, vector, default));
        var docMember = new { Id = Guid.CreateVersion7(), DocumentId = binding.DocumentId, UserId = reader.Actor, Now = clock.UtcNow };
        await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@DocumentId,@UserId,@Now)", docMember);
        var memberResult = await service.RetrieveAsync([binding.KnowledgeBaseId], reader.Actor, model, vector, default);
        Assert.AreEqual(result.Chunks.Count, memberResult.Chunks.Count);
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE Id=@Id", new { docMember.Id });
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.RevalidateAsync(memberResult, default));
        await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_member WHERE Id=@Id", new { member.Id }); reader.Actor = binding.OwnerUserId;
        foreach (var kind in tenant.Id.HasValue ? new[] { "role", "organization" } : new[] { "role" })
        {
            reader.Actor = Guid.CreateVersion7(); var relationship = Guid.CreateVersion7();
            if (kind == "role") relationships.Roles = [relationship]; else relationships.Organizations = [relationship];
            var baseGrant = Guid.CreateVersion7(); var docGrant = Guid.CreateVersion7();
            // 表名来自此固定夹具分支，不接收请求标识；两库都执行实际两层资源授予查询。
            var baseTable = kind == "role" ? "fn_ai_knowledge_role" : "fn_ai_knowledge_org";
            var docTable = kind == "role" ? "fn_ai_knowledge_document_role" : "fn_ai_knowledge_document_org";
            var column = kind == "role" ? "RoleId" : "OrganizationUnitId";
            await db.ExecuteAsync($"INSERT INTO {baseTable} (Id,KnowledgeBaseId,{column},CreatedAtUtc) VALUES (@Id,@Resource,@Relationship,@Now)", new { Id = baseGrant, Resource = binding.KnowledgeBaseId, Relationship = relationship, Now = clock.UtcNow });
            await db.ExecuteAsync($"INSERT INTO {docTable} (Id,DocumentId,{column},CreatedAtUtc) VALUES (@Id,@Resource,@Relationship,@Now)", new { Id = docGrant, Resource = binding.DocumentId, Relationship = relationship, Now = clock.UtcNow });
            var allowed = await service.RetrieveAsync([binding.KnowledgeBaseId], reader.Actor, model, vector, default);
            reader.Calls = 0; reader.BeforeAuthorization = count =>
            { if (count == 16) { relationships.Roles = []; relationships.Organizations = []; } return Task.CompletedTask; };
            try { await Assert.ThrowsExactlyAsync<InvalidDataException>(() => service.RevalidateAsync(allowed, default)); }
            finally
            {
                reader.BeforeAuthorization = null; relationships.Roles = []; relationships.Organizations = [];
                await db.ExecuteAsync($"DELETE FROM {docTable} WHERE Id=@Id", new { Id = docGrant });
                await db.ExecuteAsync($"DELETE FROM {baseTable} WHERE Id=@Id", new { Id = baseGrant });
                reader.Actor = binding.OwnerUserId;
            }
        }
        foreach (var drift in new[] { "source", "destination" })
        {
            reader.Calls = 0;
            reader.BeforeAuthorization = count => count != 16 ? Task.CompletedTask : drift == "source"
                ? db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=1 WHERE Id=@Id", new { Id = binding.DocumentId })
                : db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl='https://other.example/v1' WHERE Id=@Id", new { Id = model.Id });
            try { await Assert.ThrowsExactlyAsync<InvalidDataException>(() => pipeline.AcceptAsync(prepared, bytes, default)); }
            finally
            {
                reader.BeforeAuthorization = null;
                await db.ExecuteAsync("UPDATE fn_ai_knowledge_document SET IsDeleted=0 WHERE Id=@Id", new { Id = binding.DocumentId });
                await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id", new { Id = model.Id, Endpoint = model.EndpointBaseUrl });
            }
        }
        await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl='https://other.example/v1' WHERE Id=@Id", new { Id = model.Id });
        try { await Assert.ThrowsExactlyAsync<InvalidDataException>(() => pipeline.AcceptAsync(prepared, bytes, default)); }
        finally { await db.ExecuteAsync("UPDATE fn_ai_model_config SET EndpointBaseUrl=@Endpoint WHERE Id=@Id", new { Id = model.Id, Endpoint = model.EndpointBaseUrl }); }
        // 改实际字节而保留旧摘要，最终交付仍须拒绝；恢复后可再次读取。
        var payload = await db.QuerySingleAsync<byte[]>("SELECT vector.Payload FROM fn_ai_knowledge_index_vector AS vector INNER JOIN fn_ai_knowledge_index_batch AS batch ON batch.Id=vector.Id WHERE batch.IndexTaskId=@TaskId AND batch.BatchOrdinal=0", new { TaskId = result.Sources[0].TaskId });
        await SetPayload(new byte[8]);
        try { await Assert.ThrowsExactlyAsync<InvalidDataException>(() => pipeline.AcceptAsync(prepared, bytes, default)); }
        finally { await SetPayload(payload); }
        await service.RevalidateAsync(result, default);
        Task<int> SetPayload(byte[] value) => db.ExecuteAsync("UPDATE fn_ai_knowledge_index_vector SET Payload=@Payload WHERE Id=(SELECT Id FROM fn_ai_knowledge_index_batch WHERE IndexTaskId=@TaskId AND BatchOrdinal=0)", new { TaskId = result.Sources[0].TaskId, Payload = value });
    }
    private sealed class Reader(Guid actor, Guid? tenant, IDataTransactionState state) : ICurrentSessionAuthorization
    {
        internal Guid Actor = actor; internal bool Denied; internal int Calls; internal Func<int, Task>? BeforeAuthorization;
        public async Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Assert.IsFalse(state.HasTransaction); Calls++; if (BeforeAuthorization is not null) await BeforeAuthorization(Calls); return Denied ? null : new(Actor, tenant, Guid.CreateVersion7()); }
    }
    private sealed class OfflineWriter : IBackgroundSessionAuthorization
    {
        public Task<AuthorizedSessionActor?> AuthorizeAsync(SessionBindingSnapshot binding, string permissionCode, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("读取证明不能依赖原写入会话在线。");
    }
    private sealed class Relationships(IDataTransactionState state) : IActiveRoleDirectory, ICurrentTenantOrganizationUnitDirectory
    {
        internal Guid[] Roles = [], Organizations = [];
        public Task<IReadOnlyList<Guid>> ListActiveRoleIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Assert.IsFalse(state.HasTransaction); return Task.FromResult<IReadOnlyList<Guid>>(Roles); }
        public Task<IReadOnlyList<Guid>> ListActiveUnitIdsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Assert.IsFalse(state.HasTransaction); return Task.FromResult<IReadOnlyList<Guid>>(Organizations); }
        public Task<PagedResult<ActiveRoleDirectoryEntry>> ListActiveRolesAsync(int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, ActiveRoleDirectoryEntry>> FindActiveRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<TenantOrganizationUnitDirectoryEntry>> ListActiveUnitsAsync(int page, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, TenantOrganizationUnitDirectoryEntry>> FindActiveUnitsAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class ApiKeyAuthorization : ICurrentApiKeyAuthorization
    {
        public Task<Guid?> AuthorizeAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("该夹具只模拟交互读取者。");
    }
}

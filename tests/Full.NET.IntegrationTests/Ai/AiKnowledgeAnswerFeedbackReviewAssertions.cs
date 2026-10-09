using System.Data.Common;
using Dapper;
using DbUp;
using DbUp.Helpers;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Features.ManageAnswerFeedback;
using Full.NET.Modules.Ai.Features.ManageAnswerFeedbackReview;
using Full.NET.Modules.Ai.Knowledge;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Full.NET.IntegrationTests.Ai;

/// <summary>复用已结算真实来源，双库验证当前评审者资格、双版本和实际事务回滚。</summary>
internal static class AiKnowledgeAnswerFeedbackReviewAssertions
{
    internal static async Task ServiceAsync(IServiceProvider services, DbConnection db, AiKnowledgeAnswerHistoryGuard guard,
        ICurrentSessionAuthorization authorization, Guid owner, Guid session, Guid message, Guid knowledgeBase, Guid document,
        Action<Guid> setActor, Action<Func<int, Task>?> beforeAuthorization, Action resetCalls)
    {
        var query = services.GetRequiredService<IQueryExecutor>(); var commands = services.GetRequiredService<ICommandExecutor>();
        var transaction = services.GetRequiredService<ICommandTransaction>(); var state = services.GetRequiredService<IDataTransactionState>();
        var tenant = services.GetRequiredService<ICurrentTenant>(); var options = services.GetRequiredService<IOptions<DatabaseOptions>>();
        if (options.Value.Provider == DatabaseProvider.MySql)
        {
            // API 测试模板用 CREATE TABLE LIKE 克隆，未复制外键；重放本模块迁移后再验收真实级联行为。
            // 已打开的连接会隐藏密码；重放使用可信测试配置，禁止从活动连接取脱敏后的连接字符串。
            var restored = DeployChanges.To.MySqlDatabase(MySqlConnectionStringPolicy.Create(options.Value.ConnectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true))
                .WithScriptsEmbeddedInAssembly(typeof(DbUpMigrationRunner).Assembly, name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
                    && name.EndsWith("258_AiKnowledgeAnswerReview.sql", StringComparison.Ordinal))
                .JournalTo(new NullJournal()).WithExecutionTimeout(TimeSpan.FromSeconds(300)).Build().PerformUpgrade();
            Assert.IsTrue(restored.Successful, restored.Error?.ToString()); Assert.HasCount(1, restored.Scripts);
            Assert.AreEqual("CASCADE", await db.QuerySingleAsync<string>("SELECT DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND TABLE_NAME='fn_ai_knowledge_answer_review' AND CONSTRAINT_NAME='FK_fn_ai_knowledge_answer_review_FeedbackId'"));
        }
        var ids = services.GetRequiredService<IIdGenerator>(); var clock = services.GetRequiredService<IClock>();
        var cursor = new AiKnowledgeAnswerFeedbackReviewCursor(new EphemeralDataProtectionProvider(), clock);
        var feedback = new AiKnowledgeAnswerFeedbackService(query, commands, transaction, state, tenant, authorization, guard, options, ids, clock);
        AiKnowledgeAnswerFeedbackReviewService Create(ICommandExecutor executor, ICurrentSessionAuthorization? auth = null) => new(query, executor, transaction, state, tenant, auth ?? authorization, guard, options, ids, clock, cursor);
        var review = Create(commands); var request = new SetAiKnowledgeAnswerFeedbackRequest("helpful", "accurate", [], 0);
        var created = await feedback.SetAsync(session, message, owner, request, default); Assert.IsTrue(created.IsSuccess, created.Error?.ToString());
        var feedbackId = created.Value!.Id; var approve = new ReviewAiKnowledgeAnswerFeedbackRequest("approved", 1, 0);
        Assert.AreEqual(ErrorType.Validation, (await review.ReviewAsync(Guid.Empty, owner, approve, default)).Error!.Type);
        Assert.AreEqual(ErrorType.NotFound, (await review.ReviewAsync(feedbackId, owner, approve, default)).Error!.Type);
        var reviewer = Guid.CreateVersion7(); var member = Guid.CreateVersion7(); var docMember = Guid.CreateVersion7();
        setActor(reviewer);
        try
        {
            Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items);
            Assert.AreEqual(ErrorType.NotFound, (await review.ReviewAsync(feedbackId, reviewer, approve, default)).Error!.Type);
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@Resource,@UserId,@Now)", new { Id = docMember, Resource = document, UserId = reviewer, Now = clock.UtcNow });
            Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE Id=@Id", new { Id = docMember });
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_member (Id,KnowledgeBaseId,UserId,CreatedAtUtc) VALUES (@Id,@Resource,@UserId,@Now)", new { Id = member, Resource = knowledgeBase, UserId = reviewer, Now = clock.UtcNow });
            Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items);
            await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@Resource,@UserId,@Now)", new { Id = docMember, Resource = document, UserId = reviewer, Now = clock.UtcNow });
            var pending = (await review.GetAsync(reviewer, null, default)).Value!.Items.Single(item => item.FeedbackId == feedbackId);
            Assert.AreEqual("pending_review", pending.ReviewStateKey); Assert.AreEqual(0, pending.ReviewVersion);
            Assert.AreEqual(ErrorType.Validation, (await review.GetAsync(reviewer, feedbackId.ToString(), default)).Error!.Type);
            // 最后操作授权期间撤销文档授予，联合 SQL 必须隐藏标签且拒绝写入。
            foreach (var write in new[] { false, true })
            {
                resetCalls(); beforeAuthorization(count => count == 14 ? db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE Id=@Id", new { Id = docMember }) : Task.CompletedTask);
                try
                {
                    if (write) Assert.AreEqual(ErrorType.NotFound, (await review.ReviewAsync(feedbackId, reviewer, approve, default)).Error!.Type);
                    else Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items);
                    Assert.AreEqual(0L, await Count());
                }
                finally
                {
                    beforeAuthorization(null);
                    await db.ExecuteAsync("INSERT INTO fn_ai_knowledge_document_member (Id,DocumentId,UserId,CreatedAtUtc) VALUES (@Id,@Resource,@UserId,@Now)", new { Id = docMember, Resource = document, UserId = reviewer, Now = clock.UtcNow });
                }
            }
            foreach (var permission in new[] { AiKnowledgeAnswerFeedbackReviewPermissions.Read, AiKnowledgeAnswerFeedbackReviewPermissions.Review, AiKnowledgeAnswerFeedbackReviewPermissions.Export })
            {
                var denied = Create(commands, new DenyPermission(authorization, permission));
                Assert.IsFalse(permission == AiKnowledgeAnswerFeedbackReviewPermissions.Read ? (await denied.GetAsync(reviewer, null, default)).IsSuccess
                    : permission == AiKnowledgeAnswerFeedbackReviewPermissions.Export ? (await denied.ExportAsync(reviewer, default)).IsSuccess
                    : (await denied.ReviewAsync(feedbackId, reviewer, approve, default)).IsSuccess);
            }
            Assert.IsFalse((await Create(new RejectAfterWrite(commands)).ReviewAsync(feedbackId, reviewer, approve, default)).IsSuccess);
            Assert.AreEqual(0L, await Count());
            await Assert.ThrowsAsync<OperationCanceledException>(() => Create(new CancelAfterWrite(commands)).ReviewAsync(feedbackId, reviewer, approve, default));
            Assert.AreEqual(0L, await Count());
            var accepted = await review.ReviewAsync(feedbackId, reviewer, approve, default); Assert.IsTrue(accepted.IsSuccess, accepted.Error?.ToString());
            Assert.AreEqual(1, accepted.Value!.ReviewVersion);
            Assert.AreEqual(1, (await review.ReviewAsync(feedbackId, reviewer, approve, default)).Value!.ReviewVersion);
            Assert.AreEqual(ErrorType.Conflict, (await review.ReviewAsync(feedbackId, reviewer, approve with { DecisionKey = "rejected" }, default)).Error!.Type);
            Assert.AreEqual(2, (await review.ReviewAsync(feedbackId, reviewer, approve with { DecisionKey = "rejected", ReviewVersion = 1 }, default)).Value!.ReviewVersion);
            Assert.AreEqual(ErrorType.Conflict, (await review.ReviewAsync(feedbackId, reviewer, approve, default)).Error!.Type);
            Assert.AreEqual(3, (await review.ReviewAsync(feedbackId, reviewer, approve with { ReviewVersion = 2 }, default)).Value!.ReviewVersion);
            setActor(owner); Assert.IsTrue((await feedback.SetAsync(session, message, owner, request with { Version = 1, ReasonKey = "useful" }, default)).IsSuccess); setActor(reviewer);
            var invalidated = (await review.GetAsync(reviewer, null, default)).Value!.Items.Single(item => item.FeedbackId == feedbackId);
            Assert.AreEqual("pending_review", invalidated.ReviewStateKey); Assert.AreEqual(3, invalidated.ReviewVersion);
            Assert.AreEqual(ErrorType.Conflict, (await review.ReviewAsync(feedbackId, reviewer, approve with { ReviewVersion = 2 }, default)).Error!.Type);
            Assert.AreEqual(4, (await review.ReviewAsync(feedbackId, reviewer, approve with { FeedbackVersion = 2, ReviewVersion = 3 }, default)).Value!.ReviewVersion);
            setActor(owner); Assert.IsTrue((await feedback.WithdrawAsync(session, message, owner, 2, default)).IsSuccess); setActor(reviewer);
            Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items);
            Assert.HasCount(0, (await review.ExportAsync(reviewer, default)).Value!.Cells);
            setActor(owner); Assert.IsTrue((await feedback.SetAsync(session, message, owner, request with { Version = 3 }, default)).IsSuccess); setActor(reviewer);
            var renewed = (await review.GetAsync(reviewer, null, default)).Value!.Items.Single(item => item.FeedbackId == feedbackId);
            Assert.AreEqual("pending_review", renewed.ReviewStateKey); Assert.AreEqual(4, renewed.ReviewVersion);
            Assert.AreEqual(5, (await review.ReviewAsync(feedbackId, reviewer, approve with { FeedbackVersion = 4, ReviewVersion = 4 }, default)).Value!.ReviewVersion);
            // 第十四次为来源证明后的最后操作权限检查；其后撤回必须被候选复读发现。
            resetCalls(); beforeAuthorization(count => count == 14 ? db.ExecuteAsync("UPDATE fn_ai_knowledge_answer_feedback SET RatingKey=NULL,ReasonKey=NULL,CitationIdsJson='[]',LineageHash=NULL,StatusKey='withdrawn',Version=Version+1 WHERE Id=@Id", new { Id = feedbackId }) : Task.CompletedTask);
            try { Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items); }
            finally { beforeAuthorization(null); }
            Assert.AreEqual(5, await db.QuerySingleAsync<int>("SELECT Version FROM fn_ai_knowledge_answer_feedback WHERE Id=@Id", new { Id = feedbackId }));
            setActor(owner); Assert.IsTrue((await feedback.SetAsync(session, message, owner, request with { Version = 5 }, default)).IsSuccess); setActor(reviewer);
            Assert.AreEqual(6, (await review.ReviewAsync(feedbackId, reviewer, approve with { FeedbackVersion = 6, ReviewVersion = 5 }, default)).Value!.ReviewVersion);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE Id=@Id", new { Id = docMember });
            Assert.HasCount(0, (await review.GetAsync(reviewer, null, default)).Value!.Items);
            Assert.HasCount(0, (await review.ExportAsync(reviewer, default)).Value!.Cells);
            Assert.AreEqual(ErrorType.NotFound, (await review.ReviewAsync(feedbackId, reviewer, approve with { FeedbackVersion = 6, ReviewVersion = 6 }, default)).Error!.Type);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_answer_feedback WHERE Id=@Id", new { Id = feedbackId }); Assert.AreEqual(0L, await Count());
        }
        finally
        {
            beforeAuthorization(null); setActor(owner);
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_document_member WHERE Id=@Id", new { Id = docMember });
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_member WHERE Id=@Id", new { Id = member });
            await db.ExecuteAsync("DELETE FROM fn_ai_knowledge_answer_feedback WHERE Id=@Id", new { Id = feedbackId });
        }
        Task<long> Count() => db.QuerySingleAsync<long>("SELECT COUNT(*) FROM fn_ai_knowledge_answer_review WHERE FeedbackId=@Id", new { Id = feedbackId });
    }

    private sealed class RejectAfterWrite(ICommandExecutor inner) : ICommandExecutor
    { public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) { await inner.ExecuteAsync(statement, parameters, cancellationToken); return 0; } }
    private sealed class CancelAfterWrite(ICommandExecutor inner) : ICommandExecutor
    { public async Task<int> ExecuteAsync(SqlStatement statement, object? parameters = null, CancellationToken cancellationToken = default) { await inner.ExecuteAsync(statement, parameters, cancellationToken); throw new OperationCanceledException(); } }
    private sealed class DenyPermission(ICurrentSessionAuthorization inner, string denied) : ICurrentSessionAuthorization
    { public Task<AuthorizedSessionActor?> AuthorizeAsync(string permissionCode, CancellationToken cancellationToken = default) => permissionCode == denied ? Task.FromResult<AuthorizedSessionActor?>(null) : inner.AuthorizeAsync(permissionCode, cancellationToken); }
}

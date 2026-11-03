using System.Text.Json;
using Full.NET.AI.Abstractions.Tools;
using Full.NET.Abstractions.Ids;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Agents.Workflows;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Features.ManageAgentApprovals;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Runtime;
using Full.NET.Modules.Ai.Serialization;
using Full.NET.Modules.Calendar.Contracts;
using Full.NET.Modules.Identity.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Ai.Features.CompletePersonalSchedules;

/// <summary>批准消费/原意图/Outbox仅AI本地事务；Calendar派发和准确对账始终在事务外。</summary>
internal sealed class AiScheduleCompletionService(IQueryExecutor query, ICommandExecutor command, ICommandTransaction transaction,
    IDataTransactionState transactions, IOutboxWriter outbox, AiAgentRunStore runs, AiAgentApprovalConsumption approvals,
    IAgentRunExecutionContext execution, IPersonalScheduleCompletion calendar, ICurrentTenant tenant, IBackgroundSessionAuthorization authorization,
    IClock clock, IIdGenerator ids, IOptions<DatabaseOptions> database)
{
    internal async Task<AiPersonalScheduleCompletionStatus> AcceptAsync(ToolInvocation invocation, ToolActor actor, CancellationToken token)
    {
        Outside(); var args = ScheduleCompletionArguments.Parse(invocation.Arguments) ?? throw new InvalidOperationException("日程工具参数无效。");
        if (invocation.RunId is not { } runId) throw new InvalidOperationException("缺少真实运行身份。");
        var run = await runs.FindOwnedAsync(runId, Scope, actor.UserId, token).ConfigureAwait(false);
        var original = execution.Binding;
        if (run is null || original is null || !RunMatches(run, original) || run.StatusKey != "running"
            || actor.UserId != original.UserId || actor.TenantId != original.TenantId || actor.SessionId != original.SessionId
            || invocation.ToolName != PersonalScheduleCompletionKeys.Tool || invocation.ToolVersion != 1
            || invocation.OperationId != AgentWorkflowNodeReceipt.ResolveOperationId(run.Id, "complete_schedule")) throw new InvalidOperationException("必须使用真实原运行、节点和会话。");
        RequireScope(original.TenantId);
        var snapshot = JsonSerializer.Deserialize(run.BudgetJson, AiJsonSerializerContext.Default.AgentRunBudgetSnapshot);
        if (snapshot?.PersonalScheduleTarget != new AiPersonalScheduleCompletionTarget(args.ScheduleId, args.ExpectedVersion)
            || !await calendar.CanCompleteAsync(args.ScheduleId, args.ExpectedVersion, original, token).ConfigureAwait(false)) throw new InvalidOperationException("原目标已失效。");
        if (await approvals.ValidateForToolExecutionAsync(invocation, actor, token).ConfigureAwait(false) != AgentApprovalExecutionStatus.Approved)
            throw new InvalidOperationException("原独立批准不可执行。");
        var approval = await query.QuerySingleOrDefaultAsync<AiAgentApprovalRecord>(AiAgentApprovalSql.FindByOperation,
            AiSqlParameters.Create(("OperationId", invocation.OperationId)), token).ConfigureAwait(false);
        if (approval?.ApproverId is not { } approver || approver == actor.UserId) throw new InvalidOperationException("必须独立批准。");
        var request = new PersonalScheduleCompletionRequest(run.Id, invocation.OperationId, args.ScheduleId, args.ExpectedVersion, approver,
            original, approval.ExpiresAtUtc < run.DeadlineAtUtc ? approval.ExpiresAtUtc : run.DeadlineAtUtc);
        var requestJson = JsonSerializer.Serialize(request, AiJsonSerializerContext.Default.PersonalScheduleCompletionRequest);
        if (requestJson.Length > 32768) throw new InvalidOperationException("原意图超出固定预算。");
        await transaction.ExecuteAsync(async ct =>
        {
            RequireScope(original.TenantId);
            var root = await query.QuerySingleOrDefaultAsync<Guid>(database.Value.Provider == DatabaseProvider.SqlServer
                ? AiScheduleCompletionSql.LockRunSqlServer : AiScheduleCompletionSql.LockRunMySql,
                Parameters(("RunId", run.Id), ("OwnerUserId", actor.UserId), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
            if (root != run.Id) throw new InvalidOperationException("原运行已取消或过期。");
            RequireScope(original.TenantId);
            if (!await approvals.TryConsumeForToolExecutionAsync(invocation, actor, ct).ConfigureAwait(false)) throw new InvalidOperationException("原批准消费失败。");
            RequireScope(original.TenantId);
            var parameters = Parameters(("Id", ids.NewId()), ("RunId", run.Id), ("OperationId", invocation.OperationId), ("OwnerUserId", actor.UserId),
                ("ScheduleId", args.ScheduleId), ("ExpectedVersion", args.ExpectedVersion),
                ("RequestJson", requestJson), ("RequestDigest", request.Digest()), ("Now", clock.UtcNow));
            if (await command.ExecuteAsync(AiScheduleCompletionSql.Insert, parameters, ct).ConfigureAwait(false) != 1) throw new InvalidOperationException("原日程意图未提交。");
            await outbox.AddAsync(AiScheduleCompletionRequested.EventType, 1, new AiScheduleCompletionRequested(invocation.OperationId, tenant.Id), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); RequireScope(original.TenantId); return true;
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested(); RequireScope(original.TenantId);
        return new(run.Id, invocation.OperationId, args.ScheduleId, args.ExpectedVersion, "accepted", null, null, null);
    }

    internal async Task<AiPersonalScheduleCompletionStatus?> ReadAsync(Guid runId, SessionBindingSnapshot current, bool reconcile, CancellationToken token)
    {
        Outside(); if (current.TenantId != tenant.Id || current.UserId == Guid.Empty) return null;
        var actor = await authorization.AuthorizeAllAsync(current, [AiAgentRunPermissions.Read, PersonalScheduleCompletionKeys.Permission,
            CalendarPermissions.Read], token).ConfigureAwait(false);
        if (actor?.UserId != current.UserId || actor.SessionId != current.SessionId || actor.TenantId != tenant.Id) return null;
        var row = await query.QuerySingleOrDefaultAsync<AiScheduleCompletionIntentRecord>(AiScheduleCompletionSql.Owned,
            Parameters(("RunId", runId), ("OwnerUserId", current.UserId)), token).ConfigureAwait(false);
        if (row is null) return null;
        if (reconcile && row.Outcome is "accepted" or "unknown")
        {
            // 用户对账只读准确接收方回执，不再次派发写入，也不产生新操作身份。
            await DeliverAsync(row.OperationId, false, token).ConfigureAwait(false);
            row = await FindAsync(row.OperationId, token).ConfigureAwait(false) ?? throw new InvalidOperationException("原意图丢失。");
        }
        actor = await authorization.AuthorizeAllAsync(current, [AiAgentRunPermissions.Read, PersonalScheduleCompletionKeys.Permission, CalendarPermissions.Read], token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (current.TenantId != tenant.Id || actor?.UserId != current.UserId || actor.SessionId != current.SessionId) return null;
        return Status(row, Request(row));
    }

    internal async Task DeliverAsync(Guid operationId, bool allowInitialDispatch, CancellationToken token)
    {
        Outside(); var row = await FindAsync(operationId, token).ConfigureAwait(false) ?? throw new InvalidOperationException("原日程意图不存在。");
        var request = Request(row);
        if (row.Outcome is "completed" or "rejected") { _ = Status(row, request); return; }
        PersonalScheduleCompletionReceipt? receipt;
        try
        {
            receipt = await calendar.FindReceiptAsync(request, token).ConfigureAwait(false);
            if (receipt is null && row.Outcome == "accepted" && allowInitialDispatch)
            {
                var current = await authorization.AuthorizeAsync(request.Original, AiAgentRunPermissions.Create, token).ConfigureAwait(false);
                if (current?.UserId != request.Original.UserId || current.SessionId != request.Original.SessionId || current.TenantId != tenant.Id)
                    throw new UnauthorizedAccessException("原运行执行权限已失效；只能对账，不能继续派发。");
                // 先提交不可逆的派发标记，结果投影失败或进程崩溃后也只能查原回执。
                // 标记未提交时不会调用Calendar；竞争落败者不能重复派发。
                var claimed = await transaction.ExecuteAsync(async ct =>
                {
                    RequireScope(row.TenantId);
                    var count = await command.ExecuteAsync(AiScheduleCompletionSql.ClaimDispatch, Parameters(
                        ("OperationId", row.OperationId), ("RequestDigest", row.RequestDigest), ("Version", row.Version), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested(); RequireScope(row.TenantId); return count;
                }, token).ConfigureAwait(false);
                if (claimed != 1) throw new InvalidOperationException("原操作已有派发标记，只允许准确回执对账。");
                row = row with { Outcome = "unknown", Version = checked(row.Version + 1) };
                receipt = await calendar.ReceiveAsync(request, token).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 跨模块结果未确定，持久化unknown；下次只查准确回执，不自动重复调用工具或写动作。
            using var record = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await StoreAsync(row, "unknown", null, record.Token).ConfigureAwait(false); throw;
        }
        if (receipt is null)
        {
            if (allowInitialDispatch) throw new InvalidOperationException("原日程结果仍未知，只允许准确回执对账。");
            return;
        }
        if (!receipt.IsFor(request))
            throw new InvalidOperationException("接收方原操作回执不匹配。");
        RequireScope(row.TenantId);
        await StoreAsync(row, receipt.Outcome, JsonSerializer.Serialize(receipt, AiJsonSerializerContext.Default.PersonalScheduleCompletionReceipt), token).ConfigureAwait(false);
    }

    private Task<AiScheduleCompletionIntentRecord?> FindAsync(Guid operationId, CancellationToken token) => query.QuerySingleOrDefaultAsync<AiScheduleCompletionIntentRecord>(
        AiScheduleCompletionSql.Find, Parameters(("OperationId", operationId)), token);
    private async Task StoreAsync(AiScheduleCompletionIntentRecord row, string outcome, string? receipt, CancellationToken token)
    {
        RequireScope(row.TenantId);
        var affected = await transaction.ExecuteAsync(async ct =>
        {
            var count = await command.ExecuteAsync(AiScheduleCompletionSql.RecordReceipt, Parameters(("OperationId", row.OperationId), ("RequestDigest", row.RequestDigest),
                ("Version", row.Version), ("Outcome", outcome), ("ReceiptJson", receipt), ("Now", clock.UtcNow)), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); RequireScope(row.TenantId); return count;
        }, token).ConfigureAwait(false);
        if (affected != 1)
        {
            var current = await FindAsync(row.OperationId, token).ConfigureAwait(false);
            if (current is null || current.RequestDigest != row.RequestDigest || (outcome != "unknown" && (current.Outcome != outcome || current.ReceiptJson != receipt)))
                throw new InvalidOperationException("原日程结果投影并发改变，不能确认投递完成。");
        }
    }
    private PersonalScheduleCompletionRequest Request(AiScheduleCompletionIntentRecord row)
    {
        if (row.RequestJson.Length > 32768) throw new InvalidOperationException("原意图读取预算无效。");
        var request = JsonSerializer.Deserialize(row.RequestJson, AiJsonSerializerContext.Default.PersonalScheduleCompletionRequest);
        if (request is null || request.Digest() != row.RequestDigest || request.RunId != row.RunId || request.OperationId != row.OperationId
            || request.Original.UserId != row.OwnerUserId || request.Original.TenantId != row.TenantId || row.TenantId != tenant.Id)
            throw new InvalidOperationException("原日程意图绑定损坏。");
        return request;
    }
    private static AiPersonalScheduleCompletionStatus Status(AiScheduleCompletionIntentRecord row, PersonalScheduleCompletionRequest request)
    {
        if (row.Outcome is not ("accepted" or "unknown" or "completed" or "rejected")) throw new InvalidOperationException("未知的原操作状态。");
        var receipt = row.ReceiptJson is null ? null : JsonSerializer.Deserialize(row.ReceiptJson, AiJsonSerializerContext.Default.PersonalScheduleCompletionReceipt);
        if (receipt is not null && (!receipt.IsFor(request) || receipt.Outcome != row.Outcome)
            || row.Outcome is "completed" or "rejected" && receipt is null)
            throw new InvalidOperationException("原日程结果绑定损坏。");
        return new(row.RunId, row.OperationId, request.ScheduleId, request.ExpectedVersion, row.Outcome, receipt?.Reason, receipt?.ActualVersion, receipt?.CompletedAtUtc);
    }
    internal static bool RunMatches(AiAgentRunRecord run, SessionBindingSnapshot original) => run.DefinitionKey == AgentWorkflowRegistry.ScheduleCompletionWorkflowKey
        && run.DefinitionVersion == 1 && run.ActorUserId == original.UserId && run.TenantId == original.TenantId && run.SessionId == original.SessionId
        && run.SecurityStamp == original.SecurityStamp && run.ActorScope == original.ActorScope && run.EffectiveScope == original.EffectiveScope && run.SessionKind == original.SessionKind;
    private string Scope => tenant.Id?.ToString("N") ?? (tenant.IsHost ? "host" : throw new InvalidOperationException("缺少可信范围。"));
    private void RequireScope(Guid? expected)
    { if (tenant.Id != expected || (expected is null ? !tenant.IsHost : tenant.IsHost || !tenant.IsAvailable)) throw new InvalidOperationException("日程意图可信范围已改变。"); }
    private Dictionary<string, object?> Parameters(params (string Name, object? Value)[] values)
    { var result = new Dictionary<string, object?>(AiSqlParameters.Create(values), StringComparer.Ordinal); result["ScopeTenantId"] = tenant.Id; result["ScopeKey"] = Scope; return result; }
    private void Outside() { if (transactions.HasTransaction) throw new InvalidOperationException("日程意图权威调用不能加入本地事务。"); }
}

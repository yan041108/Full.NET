using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Abstractions.Ids;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;
using Full.NET.Modules.Workflow.Execution.Connectors;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>事务预算用尽时落库续办，输入快照和活动身份在重领期间保持不变。</summary>
internal sealed class WorkflowActivityScheduler(ICommandExecutor command, IIdGenerator ids)
{
    internal const int DispatchActivityBudget = 32;
    internal const int InstanceActivityBudget = 4096;

    /// <summary>HTTP尚未发送时先建立真实激活和固定请求；事务提交前Worker无法观察到待分发状态。</summary>
    public async Task EnqueueConnectorAsync(WorkflowInstanceRecord instance, WorkflowConnectorWaitPlan wait,
        WorkflowBoundHttpConnector connector, IReadOnlyDictionary<string, JsonElement> values, string requestJson,
        long formRevision, long sequence, Guid? joinId, string? branchKey, DateTimeOffset now, CancellationToken token)
    {
        var id = ids.NewId();
        var input = SerializeInput(values);
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        var requestHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)));
        var parameters = WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instance.Id), ("TenantScopeKey", instance.TenantScopeKey),
            ("NodeKey", wait.NodeKey), ("NodeTypeKey", wait.IsAsync ? "integration.async_trigger" : "integration.trigger"), ("ContinuationNodeKey", wait.ContinuationNodeKey),
            ("ConnectorKey", connector.Definition.ConnectorKey), ("ConnectorVersion", connector.Definition.Version), ("ConnectorHash", connector.ContentHash),
            ("ModeKey", wait.IsAsync ? "async" : "sync"), ("InputJson", input), ("InputHash", inputHash), ("InputFormRevision", formRevision),
            ("RequestJson", requestJson), ("RequestHash", requestHash), ("ExecutionSequence", sequence),
            ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey), ("Now", now));
        await command.ExecuteAsync(WorkflowConnectorSql.InsertStep, parameters, token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowConnectorSql.Insert, parameters, token).ConfigureAwait(false);
        var detail = WorkflowJsonCanonicalizer.Compile(writer =>
        {
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey", wait.IsAsync ? "integration.async_trigger" : "integration.trigger"); writer.WriteString("phaseKey", "scheduled");
            writer.WriteString("connectorKey", connector.Definition.ConnectorKey); writer.WriteNumber("connectorVersion", connector.Definition.Version);
            writer.WriteString("connectorHash", connector.ContentHash); writer.WriteString("requestHash", requestHash); writer.WriteEndObject();
        }).CanonicalJson;
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,
            WorkflowSqlParameters.Create(("Id", ids.NewId()), ("TenantId", instance.TenantId), ("ScopeKey", instance.ScopeKey),
                ("InstanceId", instance.Id), ("OperationKey", "activity.execute"), ("ActorUserId", null),
                ("ResourceTypeKey", "step"), ("ResourceId", id), ("OutcomeKey", "succeeded"), ("DetailJson", detail), ("CreatedAtUtc", now),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson())), token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", id),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("TransitionKey", wait.IsAsync ? "node.integration.async_trigger.scheduled" : "node.integration.trigger.waiting"), ("FromStatusKey", null), ("ToStatusKey", "active"),
                ("IdempotencyKey", null), ("Summary", null), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }

    /// <summary>激活等待时保存原输入、时区和已解析截止；实际到期只生成一次普通续办工作。</summary>
    public async Task EnqueueDelayAsync(WorkflowInstanceRecord instance, WorkflowDelayWaitPlan wait,
        IReadOnlyDictionary<string, JsonElement> values, long formRevision, long sequence,
        Guid? joinId, string? branchKey, DateTimeOffset due, DateTimeOffset now, CancellationToken token)
    {
        var id = ids.NewId();
        var input = SerializeInput(values);
        var parameters = WorkflowSqlParameters.Create(("Id", id), ("InstanceId", instance.Id),
            ("TenantScopeKey", instance.TenantScopeKey), ("NodeKey", wait.NodeKey), ("ContinuationNodeKey", wait.ContinuationNodeKey),
            ("InputJson", input), ("InputHash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))),
            ("InputFormRevision", formRevision), ("ExecutionSequence", sequence), ("ParallelJoinId", joinId),
            ("ParallelBranchKey", branchKey), ("ModeKey", wait.DelayPolicy.ModeKey), ("TimeZoneKey", wait.DelayPolicy.TimeZoneKey),
            ("CalendarVersionId",wait.DelayPolicy.CalendarVersionId),("CalendarHash",wait.DelayPolicy.CalendarHash),("CalendarJson",wait.DelayPolicy.Calendar?.CanonicalJson),
            ("DueAtUtc", due), ("Now", now));
        await command.ExecuteAsync(WorkflowDelaySql.InsertStep, parameters, token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowDelaySql.Insert, parameters, token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
            WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", id),
                ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                ("TransitionKey", "node.timer.delay.waiting"), ("FromStatusKey", null), ("ToStatusKey", "active"),
                ("IdempotencyKey", null), ("Summary", wait.DelayPolicy.ModeKey), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(WorkflowInstanceRecord instance, string nodeKey,
        IReadOnlyDictionary<string, JsonElement> values, long formRevision, long sequence,
        Guid? joinId, string? branchKey, DateTimeOffset now, CancellationToken token, bool budgetExhausted = false,
        Guid? workId = null, Guid? resumeBarrierId = null)
    {
        var input = SerializeInput(values);
        await command.ExecuteAsync(WorkflowActivitySql.InsertWork,
            WorkflowSqlParameters.Create(("Id", workId ?? ids.NewId()), ("InstanceId", instance.Id),
                ("TenantScopeKey", instance.TenantScopeKey), ("NodeKey", nodeKey),
                ("InputJson", input), ("InputHash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)))),
                ("InputFormRevision", formRevision), ("ExecutionSequence", sequence),
                ("ParallelJoinId", joinId), ("ParallelBranchKey", branchKey), ("DueAtUtc", now), ("ResumeBarrierId", resumeBarrierId)), token).ConfigureAwait(false);
        if (budgetExhausted)
            await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,
                WorkflowSqlParameters.Create(("Id", ids.NewId()), ("InstanceId", instance.Id), ("StepId", null),
                    ("CorrelationJson", Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson()),
                    ("TransitionKey", "activity.budget.exhausted"), ("FromStatusKey", "active"),
                    ("ToStatusKey", "suspended"), ("IdempotencyKey", null),
                    ("Summary", "workflow.activity.budget_exhausted"), ("CreatedAtUtc", now)), token).ConfigureAwait(false);
    }
    internal static string SerializeInput(IReadOnlyDictionary<string, JsonElement> values) => JsonSerializer.Serialize(
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), WorkflowJsonSerializerContext.Default.DictionaryStringJsonElement);

    /// <summary>等待绑定真实原CallId；即使当前依赖为空，续办也只由服务器屏障回执释放。</summary>
    public async Task EnqueueBarrierAsync(WorkflowInstanceRecord instance, WorkflowAsyncBarrierWaitPlan wait, Guid[] dependencies,
        IReadOnlyDictionary<string, JsonElement> values, long formRevision, long sequence, Guid? joinId, string? branchKey,
        DateTimeOffset now, CancellationToken token)
    {
        var id = ids.NewId(); var input = SerializeInput(values);
        var dependencyJson = JsonSerializer.Serialize(dependencies.Order().ToArray(), WorkflowJsonSerializerContext.Default.GuidArray);
        static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var parameters = WorkflowSqlParameters.Create(("Id",id),("InstanceId",instance.Id),("TenantScopeKey",instance.TenantScopeKey),
            ("NodeKey",wait.NodeKey),("DependencyCallIdsJson",dependencyJson),("DependencyHash",Hash(dependencyJson)),("InputJson",input),("InputHash",Hash(input)),
            ("InputFormRevision",formRevision),("ExecutionSequence",sequence),("ParallelJoinId",joinId),("ParallelBranchKey",branchKey),("Now",now));
        await command.ExecuteAsync(WorkflowAsyncBarrierSql.InsertStep,parameters,token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowAsyncBarrierSql.Insert,parameters,token).ConfigureAwait(false);
        var detail = WorkflowJsonCanonicalizer.Compile(writer => {
            writer.WriteStartObject(); writer.WriteString("nodeTypeKey","internal.await"); writer.WriteString("phaseKey","waiting");
            writer.WriteString("consumerNodeKey",wait.NodeKey); writer.WriteNumber("dependencyCount",dependencies.Length); writer.WriteEndObject(); }).CanonicalJson;
        var correlation=Full.NET.Abstractions.Observability.LogCorrelationContext.Capture().ToJson();
        await command.ExecuteAsync(WorkflowSql.InsertDomainAudit,WorkflowSqlParameters.Create(("Id",ids.NewId()),("TenantId",instance.TenantId),("ScopeKey",instance.ScopeKey),
            ("InstanceId",instance.Id),("OperationKey","activity.execute"),("ActorUserId",null),("ResourceTypeKey","step"),("ResourceId",id),
            ("OutcomeKey","succeeded"),("DetailJson",detail),("CreatedAtUtc",now),("CorrelationJson",correlation)),token).ConfigureAwait(false);
        await command.ExecuteAsync(WorkflowSql.InsertExecutionLog,WorkflowSqlParameters.Create(("Id",ids.NewId()),("InstanceId",instance.Id),("StepId",id),
            ("CorrelationJson",correlation),("TransitionKey","activity.async.waiting"),("FromStatusKey",null),("ToStatusKey","active"),
            ("IdempotencyKey",null),("Summary",null),("CreatedAtUtc",now)),token).ConfigureAwait(false);
    }
}

/// <summary>同一事务中分叉共享预算；下一次耐久调度获得新的有限预算。</summary>
internal sealed class WorkflowDispatchBudget
{
    private readonly long startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
    internal static readonly TimeSpan DispatchTimeBudget = TimeSpan.FromSeconds(5);
    public bool TimeExpired => System.Diagnostics.Stopwatch.GetElapsedTime(startedAt) >= DispatchTimeBudget;
    public int Remaining { get; set; } = WorkflowActivityScheduler.DispatchActivityBudget;
    public int TotalRemaining { get; set; } = WorkflowActivityScheduler.InstanceActivityBudget;
    public Guid? CurrentWorkId { get; set; }
    public bool Exhausted { get; set; }
}

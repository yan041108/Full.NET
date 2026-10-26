using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>投票固定独立表单版本和两个百分比分母；额外声明式规则只能收紧基础通过条件。</summary>
internal sealed record WorkflowVotePolicy(Guid BallotFormVersionId, int ParticipationPercentage, int ApprovalPercentage,
    bool AllowAbstain, int? CloseAfterSeconds, string? ResultFieldKey, WorkflowVoteResultRule? ResultRule)
{
    public static bool TryRead(JsonElement config, out WorkflowVotePolicy? policy)
    {
        policy = null;
        if (!UniqueObject(config) || !config.TryGetProperty("votePolicy", out var element) || !UniqueObject(element) ||
            element.EnumerateObject().Any(property => property.Name is not ("ballotFormVersionId" or "participationPercentage" or
                "approvalPercentage" or "allowAbstain" or "closeAfterSeconds" or "resultFieldKey" or "resultRule")) ||
            !element.TryGetProperty("ballotFormVersionId", out var form) || form.ValueKind != JsonValueKind.String ||
            !form.TryGetGuid(out var version) || version == Guid.Empty || !Percent(element, "participationPercentage", out var participation) ||
            !Percent(element, "approvalPercentage", out var approval) || !element.TryGetProperty("allowAbstain", out var abstain) ||
            abstain.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        int? seconds = null;
        if (element.TryGetProperty("closeAfterSeconds", out var deadline))
        {
            if (!deadline.TryGetInt32Safe(out var value) || value is < 60 or > 2_592_000) return false;
            seconds = value;
        }
        string? field = null;
        if (element.TryGetProperty("resultFieldKey", out var output))
        {
            if (output.ValueKind != JsonValueKind.String || !ValidField(output.GetString())) return false;
            field = output.GetString();
        }
        WorkflowVoteResultRule? rule = null;
        if (element.TryGetProperty("resultRule", out var expression) && !WorkflowVoteResultRule.TryRead(expression, out rule)) return false;
        policy = new(version, participation, approval, abstain.GetBoolean(), seconds, field, rule);
        return true;
    }

    internal static bool UniqueObject(JsonElement value) => value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().GroupBy(property => property.Name, StringComparer.Ordinal).All(group => group.Count() == 1);
    private static bool Percent(JsonElement element, string key, out int value)
    { value = 0; return element.TryGetProperty(key, out var number) && number.TryGetInt32Safe(out value) && value is >= 1 and <= 100; }
    private static bool ValidField(string? field) => field is { Length: >= 1 and <= 64 } &&
        (char.IsAsciiLetter(field[0]) || field[0] == '_') && field.All(value => char.IsAsciiLetterOrDigit(value) || value == '_');
}

/// <summary>受限结果表达式只读取聚合数量，不访问表单、脚本、网络或用户身份。</summary>
internal sealed record WorkflowVoteResultRule(string? OperatorKey, string? MetricKey, string? ComparisonKey, int Value,
    IReadOnlyList<WorkflowVoteResultRule> Conditions)
{
    public static bool TryRead(JsonElement element, out WorkflowVoteResultRule? rule)
    { var count = 0; return Read(element, 1, ref count, out rule); }

    private static bool Read(JsonElement element, int depth, ref int count, out WorkflowVoteResultRule? rule)
    {
        rule = null;
        if (depth > 4 || ++count > 32 || !WorkflowVotePolicy.UniqueObject(element)) return false;
        if (element.TryGetProperty("operatorKey", out var operation))
        {
            if (element.EnumerateObject().Count() != 2 || operation.ValueKind != JsonValueKind.String || operation.GetString() is not ("and" or "or") ||
                !element.TryGetProperty("conditions", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 2 or > 8) return false;
            var children = new List<WorkflowVoteResultRule>();
            foreach (var child in array.EnumerateArray())
            { if (!Read(child, depth + 1, ref count, out var parsed)) return false; children.Add(parsed!); }
            rule = new(operation.GetString(), null, null, 0, children); return true;
        }
        if (element.EnumerateObject().Count() != 3 || !element.TryGetProperty("metricKey", out var metric) || metric.ValueKind != JsonValueKind.String ||
            metric.GetString() is not ("eligible" or "participated" or "valid" or "yes" or "no" or "abstain") ||
            !element.TryGetProperty("comparisonKey", out var comparison) || comparison.ValueKind != JsonValueKind.String || comparison.GetString() is not ("eq" or "gte" or "lte") ||
            !element.TryGetProperty("value", out var number) || !number.TryGetInt32Safe(out var value) || value is < 0 or > 20) return false;
        rule = new(null, metric.GetString(), comparison.GetString(), value, []); return true;
    }

    internal bool Evaluate(WorkflowVoteTally tally)
    {
        if (OperatorKey is "and") return Conditions.All(condition => condition.Evaluate(tally));
        if (OperatorKey is "or") return Conditions.Any(condition => condition.Evaluate(tally));
        var number = MetricKey switch { "eligible" => tally.EligibleCount, "participated" => tally.YesCount + tally.NoCount + tally.AbstainCount,
            "valid" => tally.YesCount + tally.NoCount, "yes" => tally.YesCount, "no" => tally.NoCount, "abstain" => tally.AbstainCount, _ => -1 };
        return number >= 0 && (ComparisonKey switch { "eq" => number == Value, "gte" => number >= Value, "lte" => number <= Value, _ => false });
    }
}

/// <summary>个人票数与尚未提交人数共同校验固定候选分母，弃权只参与参与率。</summary>
internal sealed record WorkflowVoteTally(int EligibleCount, int YesCount, int NoCount, int AbstainCount, int PendingCount);
/// <summary>实际候选顺序在激活准备阶段固化，不随后续投票重新计算分母。</summary>
internal sealed record WorkflowVoteActivationPolicy(WorkflowVotePolicy Policy, IReadOnlyList<Guid> MemberUserIds);
internal enum WorkflowVoteOutcome { Invalid, Waiting, Approved, Rejected, Suspended }

/// <summary>截止只能由服务端时钟与持久截止事实裁定；未截止的部分票无论当前比例如何都等待。</summary>
internal static class WorkflowVoteRules
{
    public static WorkflowVoteOutcome Decide(WorkflowVotePolicy policy, WorkflowVoteTally tally, bool deadlineReached)
    {
        if (policy.ParticipationPercentage is < 1 or > 100 || policy.ApprovalPercentage is < 1 or > 100 || tally.EligibleCount is < 1 or > 20 ||
            tally.YesCount is < 0 or > 20 || tally.NoCount is < 0 or > 20 || tally.AbstainCount is < 0 or > 20 || tally.PendingCount is < 0 or > 20 ||
            tally.YesCount + tally.NoCount + tally.AbstainCount + tally.PendingCount != tally.EligibleCount || !policy.AllowAbstain && tally.AbstainCount != 0)
            return WorkflowVoteOutcome.Invalid;
        if (tally.PendingCount > 0 && !deadlineReached) return WorkflowVoteOutcome.Waiting;
        var valid = tally.YesCount + tally.NoCount;
        if (valid == 0) return WorkflowVoteOutcome.Suspended;
        var participated = valid + tally.AbstainCount;
        var quorum = (tally.EligibleCount * policy.ParticipationPercentage + 99) / 100;
        var yesRequired = (valid * policy.ApprovalPercentage + 99) / 100;
        return participated >= quorum && tally.YesCount >= yesRequired && (policy.ResultRule?.Evaluate(tally) ?? true)
            ? WorkflowVoteOutcome.Approved : WorkflowVoteOutcome.Rejected;
    }
}

/// <summary>先检查数值种类，避免错误种类的 JSON 在严格配置验证中抛异常。</summary>
internal static class WorkflowVoteJsonNumber
{
    internal static bool TryGetInt32Safe(this JsonElement element, out int value)
    { value = 0; return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value); }
}

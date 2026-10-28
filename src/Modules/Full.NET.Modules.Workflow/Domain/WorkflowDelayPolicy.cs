using System.Text.Json;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>延时接受墙钟时长、明确时间点或已绑定版本的工作时长，工作日历必须带固定快照。</summary>
internal sealed record WorkflowDelayPolicy(string ModeKey, string TimeZoneKey, long DurationSeconds,
    DateTimeOffset? AtUtc, string? FieldKey,Guid? CalendarVersionId=null,string? CalendarHash=null,WorkflowWorkCalendar? Calendar=null)
{
    internal const long MaximumDurationSeconds = 366L * 24 * 60 * 60;

    public static bool TryRead(JsonElement config, WorkflowFormSchema? schema, out WorkflowDelayPolicy? policy)
    {
        policy = null;
        if(config.ValueKind==JsonValueKind.Object && config.TryGetProperty("delayPolicy",out var working) && working.ValueKind==JsonValueKind.Object &&
            working.TryGetProperty("modeKey",out var workingMode) && workingMode.ValueKind==JsonValueKind.String && workingMode.GetString()=="working")
            return WorkflowWorkingDelayPolicy.TryRead(working,out policy);
        if (config.ValueKind != JsonValueKind.Object || !config.TryGetProperty("delayPolicy", out var value) || value.ValueKind != JsonValueKind.Object ||
            value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != value.EnumerateObject().Count() ||
            value.EnumerateObject().Any(property => property.Name is not ("modeKey" or "timeZoneKey" or "duration" or "unitKey" or "at" or "fieldKey")) ||
            !ReadString(value, "modeKey", out var mode) || !ReadString(value, "timeZoneKey", out var zone) ||
            zone!.Length > 128 || !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)) return false;
        if (mode == "relative")
        {
            if (value.TryGetProperty("at", out _) || value.TryGetProperty("fieldKey", out _) ||
                !value.TryGetProperty("duration", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt64(out var amount) || amount < 1 ||
                !ReadString(value, "unitKey", out var unit)) return false;
            var multiplier = unit switch { "second" => 1L, "minute" => 60L, "hour" => 3600L, "day" => 86400L, _ => 0L };
            if (multiplier == 0 || amount > MaximumDurationSeconds / multiplier) return false;
            policy = new(mode, zone, amount * multiplier, null, null);
            return true;
        }
        if (mode != "absolute" || value.TryGetProperty("duration", out _) || value.TryGetProperty("unitKey", out _)) return false;
        var hasAt = value.TryGetProperty("at", out var at);
        var hasField = value.TryGetProperty("fieldKey", out var field);
        if (hasAt == hasField) return false;
        if (hasAt)
        {
            if (!TryReadInstant(at, out var instant)) return false;
            policy = new(mode, zone, 0, instant, null);
            return true;
        }
        if (field.ValueKind != JsonValueKind.String || !WorkflowNodeKeyValidator.IsValid(field.GetString())) return false;
        var fieldKey = field.GetString()!;
        if (schema is not null && !schema.Sections.SelectMany(section => section.Fields)
                .Any(candidate => candidate.FieldKey == fieldKey && candidate.FieldTypeKey == "datetime")) return false;
        policy = new(mode, zone, 0, null, fieldKey);
        return true;
    }

    /// <summary>只在活动激活时解析输入，重启或恢复不得重新读取可变表单推迟截止时间。</summary>
    public bool TryResolve(IReadOnlyDictionary<string, JsonElement> values, DateTimeOffset now, out DateTimeOffset due)
    {
        due = default;
        if(ModeKey=="working")return Calendar is not null && Calendar.TryAddWorkingMilliseconds(now,DurationSeconds*1000,out due);
        if (ModeKey == "relative")
        {
            if (now > DateTimeOffset.MaxValue.AddSeconds(-DurationSeconds)) return false;
            due = now.AddSeconds(DurationSeconds).ToUniversalTime();
            return true;
        }
        if (AtUtc is { } fixedInstant) { due = fixedInstant; return true; }
        return FieldKey is { } field && values.TryGetValue(field, out var value) && TryReadInstant(value, out due);
    }

    private static bool TryReadInstant(JsonElement value, out DateTimeOffset instant)
    {
        instant = default;
        if (value.ValueKind != JsonValueKind.String ||
            !WorkflowFormFieldConstraints.TryParseCanonicalTemporal("datetime", value.GetString()!, out var ticks)) return false;
        instant = new DateTimeOffset(ticks, TimeSpan.Zero);
        return instant.Year >= 1000 && instant.Year < 9999;
    }

    private static bool ReadString(JsonElement value, string key, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString())) return false;
        result = property.GetString(); return true;
    }
}

/// <summary>自动等待与人工待办分离，等待结束后才解析发布版本的后继。</summary>
internal abstract record WorkflowAutomaticWaitPlan(string NodeKey, string ContinuationNodeKey);

/// <summary>路由先落库固定输入，由独立根事务冻结选择并建立唯一跨代次后继。</summary>
internal sealed record WorkflowRouteWaitPlan(string NodeKey, string ContinuationNodeKey, JsonElement Configuration)
    : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);

/// <summary>子流程等待固定版本、父Schema和原配置，启动在独立短事务中完成。</summary>
internal sealed record WorkflowSubprocessWaitPlan(string NodeKey, string ContinuationNodeKey, WorkflowSubprocessPolicy Policy,
    WorkflowBoundSubprocess Binding, WorkflowFormSchema ParentSchema, JsonElement Configuration) : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);

/// <summary>所有者业务效果必须由真实回执确认，初期动作只允许根审批的最终提交位置。</summary>
internal sealed record WorkflowBusinessCommandWaitPlan(string NodeKey, string ContinuationNodeKey, WorkflowBusinessCommandPolicy Policy,
    string ActionHash, IReadOnlyList<string> ApprovalNodeKeys) : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);

/// <summary>延时激活保存截止后等待，不占用人工席位。</summary>
internal sealed record WorkflowDelayWaitPlan(string NodeKey, string ContinuationNodeKey, WorkflowDelayPolicy DelayPolicy)
    : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);

/// <summary>字段动作固定输入后由耐久工作执行，不能提前求值后继。</summary>
internal sealed record WorkflowFieldWaitPlan(string NodeKey, string ContinuationNodeKey, WorkflowFieldMutationPolicy Policy)
    : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);

/// <summary>连接器只在发布绑定摘要仍匹配时分发，后继交由固定输入的耐久续办推进。</summary>
internal sealed record WorkflowConnectorWaitPlan(string NodeKey, string ContinuationNodeKey,
    WorkflowConnectorPolicy Policy, string ConnectorHash, bool IsAsync = false) : WorkflowAutomaticWaitPlan(NodeKey, ContinuationNodeKey);

/// <summary>消费者等待原异步激活；释放后从同一消费者继续，不增加可发布菜单节点。</summary>
internal sealed record WorkflowAsyncBarrierWaitPlan(string NodeKey, WorkflowAsyncWaitDefinition Definition)
    : WorkflowAutomaticWaitPlan(NodeKey, NodeKey);

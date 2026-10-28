using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Workflow.Persistence;
using Full.NET.Modules.Workflow.Serialization;

namespace Full.NET.Modules.Workflow.Domain;

/// <summary>来源快照记录一次捕获时间；恢复只使用快照内成员，不能再次查询并替换原分母。</summary>
internal sealed record WorkflowMultiInstanceSnapshot(WorkflowMultiInstancePreparedSource Source, DateTimeOffset CapturedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WorkflowSubprocessPreparedStarters? Starters = null);

internal static class WorkflowMultiInstanceSnapshotRules
{
    public static bool TryRead(WorkflowMultiInstanceSetRecord set, WorkflowMultiInstancePolicy policy,
        IReadOnlyDictionary<string, JsonElement> input, [NotNullWhen(true)] out WorkflowMultiInstanceSnapshot? snapshot)
    {
        snapshot = null;
        if (set.ModeKey != policy.ModeKey || set.SourceKey != policy.SourceKey || set.MaximumCount != policy.MaximumCount ||
            set.RequiredPercentage != policy.RequiredPercentage || set.Revision < 1 || set.SourceJson is null ||
            Encoding.UTF8.GetByteCount(set.SourceJson) > WorkflowSubprocessPolicy.MaximumInputBytes ||
            set.SourceHash != WorkflowSubprocessBindings.Hash(set.SourceJson)) return false;
        try { snapshot = JsonSerializer.Deserialize(set.SourceJson, WorkflowJsonSerializerContext.Default.WorkflowMultiInstanceSnapshot); }
        catch (JsonException) { return false; }
        if (snapshot?.Source is not { Items: { } items, RoleMembers: { } members } source || source.RoleId != policy.RoleId ||
            policy.RoleId is null && members.Count != 0 || !policy.TryFreeze(input, policy.RoleId is null ? null : members, out var seeds) ||
            !seeds.SequenceEqual(items) || set.ItemCount != items.Count || set.RequiredSuccesses != policy.RequiredSuccesses(items.Count))
        { snapshot = null; return false; }
        return true;
    }

    public static bool MatchesItems(WorkflowSubprocessPolicy policy, WorkflowFormSchema child,
        IReadOnlyDictionary<string, JsonElement> input, WorkflowMultiInstanceSnapshot snapshot,
        IReadOnlyList<WorkflowSubprocessItemRecord> items, Guid starter)
    {
        if (items.Count != snapshot.Source.Items.Count || items.Select(item => item.Id).Distinct().Count() != items.Count) return false;
        var delegated = policy.Starter is { SourceKey: not "parent" };
        var captured = snapshot.Starters?.ByItemKey;
        if (delegated && (captured is null || captured.Count != items.Count) || !delegated && snapshot.Starters is not null) return false;
        foreach (var seed in snapshot.Source.Items)
        {
            var expectedStarter = starter;
            if (policy.Starter is not null && !policy.Starter.TryResolve(starter, input, seed, out expectedStarter) ||
                delegated && (captured is null || !captured.TryGetValue(seed.ItemKey, out var stored) || stored != expectedStarter)) return false;
            var matching = items.Where(item => item.SequenceNo == seed.SequenceNo && item.ItemKey == seed.ItemKey).ToArray();
            if (matching is not [var item] || item.StarterUserId != expectedStarter ||
                !policy.TryCreateInput(input, child, out var expected, seed) || item.InputJson != expected ||
                item.InputHash != WorkflowSubprocessBindings.Hash(item.InputJson)) return false;
        }
        return true;
    }

    public static string SelectionHash(IEnumerable<WorkflowSubprocessItemRecord> items) => WorkflowSubprocessBindings.Hash(string.Join("\n",
        items.OrderBy(item => item.ItemKey, StringComparer.Ordinal).Select(item => item.ItemKey + ":" + item.Id.ToString("D") + ":" + item.ResultHash)));
}

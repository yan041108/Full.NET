using Full.NET.Modules.Workflow.Domain;

namespace Full.NET.UnitTests.Workflow;

/// <summary>发布版本引用图独立于展示树；历史自引用、间接循环和过深调用不能靠版本固定绕过。</summary>
[TestClass]
public sealed class WorkflowSubprocessReferenceTests
{
    [TestMethod]
    public void Historical_self_reference_is_rejected_directly_and_transitively()
    {
        var own = Guid.CreateVersion7(); var child = Guid.CreateVersion7();
        var graph = new Dictionary<Guid, IReadOnlyList<Guid>> { [own] = [], [child] = [own] };
        Assert.IsFalse(WorkflowSubprocessBindings.ValidateReferenceGraph(graph, new HashSet<Guid> { own }, [own]));
        Assert.IsFalse(WorkflowSubprocessBindings.ValidateReferenceGraph(graph, new HashSet<Guid> { own }, [child]));
    }
    [TestMethod]
    public void Shared_descendant_is_allowed_but_cycle_and_missing_asset_are_rejected()
    {
        var left = Guid.CreateVersion7(); var right = Guid.CreateVersion7(); var shared = Guid.CreateVersion7();
        var graph = new Dictionary<Guid, IReadOnlyList<Guid>> { [left] = [shared], [right] = [shared], [shared] = [] };
        Assert.IsTrue(WorkflowSubprocessBindings.ValidateReferenceGraph(graph, new HashSet<Guid>(), [left, right]));
        graph[shared] = [left]; Assert.IsFalse(WorkflowSubprocessBindings.ValidateReferenceGraph(graph, new HashSet<Guid>(), [left]));
        graph[shared] = [Guid.CreateVersion7()]; Assert.IsFalse(WorkflowSubprocessBindings.ValidateReferenceGraph(graph, new HashSet<Guid>(), [left]));
    }
    [TestMethod]
    public void Excessive_depth_is_rejected()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => Guid.CreateVersion7()).ToArray();
        var graph = ids.Select((id, index) => (id, next: (IReadOnlyList<Guid>)(index == ids.Length - 1 ? [] : new[] { ids[index + 1] }))).ToDictionary(item => item.id, item => item.next);
        Assert.IsFalse(WorkflowSubprocessBindings.ValidateReferenceGraph(graph, new HashSet<Guid>(), [ids[0]]));
    }
}

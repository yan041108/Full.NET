using Full.NET.AI.Abstractions.Budgets;
using Full.NET.AI.Abstractions.Models;

namespace Full.NET.UnitTests.Ai;

/// <summary>精确计数继承原调用预算和授权复核，不允许嵌套放宽或后台使用已释放上下文。</summary>
[TestClass]
public sealed class AiChatInputBudgetScopeTests
{
    [TestMethod]
    public async Task Captured_scope_can_reenter_across_async_operations_without_leaking_to_caller()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        var captured = AiChatInputBudgetScope.Capture(20);
        Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
        async Task Invoke()
        {
            using var activation = captured.Enter(); await Task.Yield();
            Assert.AreSame(captured, AiChatInputBudgetScope.RequireCurrent());
            Assert.AreEqual(20L, captured.InputTokenLimit); await captured.RevalidateAsync(default);
        }
        await Invoke(); Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
        await Invoke(); Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
        captured.Dispose();
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => captured.RevalidateAsync(default));
    }

    [TestMethod]
    public void Captured_scope_cannot_replace_a_new_tighter_parent()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        using var captured = AiChatInputBudgetScope.Capture(80);
        using var tighter = AiChatInputBudgetScope.Begin(10);
        Assert.ThrowsExactly<AiBudgetException>(() => captured.Enter());
        Assert.AreSame(tighter, AiChatInputBudgetScope.RequireCurrent());
    }

    [TestMethod]
    public void Captured_scope_must_finish_activation_before_closing()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        var captured = AiChatInputBudgetScope.Capture(20);
        using (captured.Enter())
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => captured.Dispose());
            Assert.ThrowsExactly<AiBudgetException>(() => captured.Enter());
        }
        captured.Dispose(); Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
    }

    [TestMethod]
    public async Task Nested_scope_keeps_smallest_limit_and_all_authority_checks()
    {
        var calls = new List<string>();
        using var root = AiChatInputBudgetScope.Begin(100, _ => { calls.Add("root"); return Task.CompletedTask; });
        using (var child = AiChatInputBudgetScope.Begin(200, _ => { calls.Add("child"); return Task.CompletedTask; }))
        {
            Assert.AreEqual(100L, child.InputTokenLimit);
            Assert.AreSame(child, AiChatInputBudgetScope.RequireCurrent());
            await child.RevalidateAsync(default);
            CollectionAssert.AreEqual(new[] { "child", "root" }, calls);
        }
        Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
    }

    [TestMethod]
    public async Task Child_cannot_replace_a_denied_parent_authority()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => throw new UnauthorizedAccessException());
        using var child = AiChatInputBudgetScope.Begin(20, _ => Task.CompletedTask);
        await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => child.RevalidateAsync(default));
    }

    [TestMethod]
    public async Task Missing_authority_is_not_an_exact_dispatch_certificate()
    {
        using var scope = AiChatInputBudgetScope.Begin(100);
        var error = await Assert.ThrowsExactlyAsync<AiBudgetException>(() => scope.RevalidateAsync(default));
        Assert.AreEqual("ai.budget.hard_input_unavailable", error.Code);
    }

    [TestMethod]
    public async Task Disposed_scope_cannot_be_used_by_a_late_provider()
    {
        var scope = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        scope.Dispose();
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => scope.RevalidateAsync(default));
        Assert.ThrowsExactly<AiBudgetException>(() => AiChatInputBudgetScope.RequireCurrent());
    }

    [TestMethod]
    public async Task Cancelled_count_does_not_run_authority_or_generation()
    {
        var calls = 0;
        using var scope = AiChatInputBudgetScope.Begin(100, _ => { calls++; return Task.CompletedTask; });
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => scope.RevalidateAsync(cancelled.Token));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task Disposed_middle_ancestor_invalidates_child_waiting_for_root_authority()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var root = AiChatInputBudgetScope.Begin(100, async _ => { entered.SetResult(); await release.Task; });
        var middle = AiChatInputBudgetScope.Begin(80, _ => Task.CompletedTask);
        var work = Task.Run(async () => { using var child = AiChatInputBudgetScope.Begin(60); await child.RevalidateAsync(default); });
        await entered.Task; middle.Dispose(); release.SetResult();
        await Assert.ThrowsExactlyAsync<AiBudgetException>(() => work);
    }

    [TestMethod]
    public async Task Asynchronous_branches_do_not_share_child_limits()
    {
        using var root = AiChatInputBudgetScope.Begin(100, _ => Task.CompletedTask);
        static async Task<long> Branch(long limit)
        {
            using var child = AiChatInputBudgetScope.Begin(limit);
            await Task.Yield(); await child.RevalidateAsync(default);
            return AiChatInputBudgetScope.RequireCurrent().InputTokenLimit;
        }
        var values = await Task.WhenAll(Branch(10), Branch(20));
        CollectionAssert.AreEqual(new long[] { 10, 20 }, values);
        Assert.AreSame(root, AiChatInputBudgetScope.RequireCurrent());
    }

    [TestMethod]
    [DataRow(0L)] [DataRow(-1L)] [DataRow(1_000_000_001L)]
    public void Invalid_limit_fails_before_installing_scope(long limit)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AiChatInputBudgetScope.Begin(limit));
        Assert.ThrowsExactly<AiBudgetException>(() => AiChatInputBudgetScope.RequireCurrent());
    }
}

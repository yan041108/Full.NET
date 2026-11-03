using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>单元测试（代码生成）：Code Generation Apply Gate。</summary>
[TestClass]
public sealed class CodeGenerationApplyGateTests
{
    private string _workspaceRoot = null!;
    [TestInitialize] public void Initialize() => _workspaceRoot = Directory.CreateTempSubdirectory("fullnet-codegen-gate-").FullName;
    [TestCleanup] public void Cleanup() => Directory.Delete(_workspaceRoot, recursive: true);
    [TestMethod]
    public async Task Failed_acquisition_does_not_strand_local_gate()
    {
        var backend = new FailingOnceBackend();
        var gate = new CodeGenerationApplyGate(Options.Create(new CodeGenerationApplyOptions
        {
            WorkspaceRoot = _workspaceRoot, DistributedGateEnabled = true,
        }), backend);
        await Assert.ThrowsExactlyAsync<IOException>(() => gate.TryEnterAsync(CancellationToken.None));
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        await gate.ReleaseAsync();
    }

    private sealed class FailingOnceBackend : ICodeGenerationWorkspaceLockBackend
    {
        private int _calls;
        public Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken) =>
            ++_calls == 1 ? Task.FromException<IAsyncDisposable?>(new IOException("锁获取失败"))
                : Task.FromResult<IAsyncDisposable?>(new EmptyLease());
        private sealed class EmptyLease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    public async Task Release_waits_for_lease_without_releasing_local_gate_early()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new DelegateBackend(() => Task.FromResult<IAsyncDisposable?>(new AsyncLease(() => new ValueTask(completion.Task))));
        var gate = CreateDistributedGate(backend);
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        var release = gate.ReleaseAsync();
        Assert.IsFalse(release.IsCompleted);
        Assert.IsFalse(await gate.TryEnterAsync(CancellationToken.None));
        completion.SetResult();
        await release;
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        await gate.ReleaseAsync();
    }

    [TestMethod]
    public async Task Release_failure_does_not_strand_local_gate()
    {
        var gate = CreateDistributedGate(new DelegateBackend(() => Task.FromResult<IAsyncDisposable?>(
            new AsyncLease(() => ValueTask.FromException(new IOException("释放失败"))))));
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        await Assert.ThrowsExactlyAsync<IOException>(() => gate.ReleaseAsync().AsTask());
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        await Assert.ThrowsExactlyAsync<IOException>(() => gate.ReleaseAsync().AsTask());
    }

    [TestMethod]
    public async Task Cancelled_acquisition_does_not_strand_local_gate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var calls = 0;
        var gate = CreateDistributedGate(new DelegateBackend(() => ++calls == 1
            ? Task.FromCanceled<IAsyncDisposable?>(cancellation.Token)
            : Task.FromResult<IAsyncDisposable?>(new AsyncLease(() => ValueTask.CompletedTask))));
        await Assert.ThrowsAsync<OperationCanceledException>(() => gate.TryEnterAsync(CancellationToken.None));
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        await gate.ReleaseAsync();
    }

    private CodeGenerationApplyGate CreateDistributedGate(ICodeGenerationWorkspaceLockBackend backend) =>
        new(Options.Create(new CodeGenerationApplyOptions { WorkspaceRoot = _workspaceRoot, DistributedGateEnabled = true }), backend);

    private sealed class DelegateBackend(Func<Task<IAsyncDisposable?>> acquire) : ICodeGenerationWorkspaceLockBackend
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(string resource, CancellationToken cancellationToken) => acquire();
    }

    private sealed class AsyncLease(Func<ValueTask> release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => release();
    }

    [TestMethod]
    public void Workspace_lock_resource_is_stable_for_persistent_identity()
    {
        var identity = Guid.CreateVersion7();
        var first = CodeGenerationWorkspaceLockResource.Create(identity);
        var second = CodeGenerationWorkspaceLockResource.Create(identity);
        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, "fn:codegeneration:workspace:");
    }

    [TestMethod]
    public async Task Local_gate_rejects_second_concurrent_enter()
    {
        var gate = CodeGenerationApplyGateTestSupport.CreateLocalGate(_workspaceRoot);
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        Assert.IsFalse(await gate.TryEnterAsync(CancellationToken.None));
        await gate.ReleaseAsync();
    }

    [TestMethod]
    public async Task Independent_instances_share_file_lease_and_keep_identity_after_release()
    {
        var first = CodeGenerationApplyGateTestSupport.CreateLocalGate(_workspaceRoot);
        var second = CodeGenerationApplyGateTestSupport.CreateLocalGate(_workspaceRoot);
        Assert.IsTrue(await first.TryEnterAsync(CancellationToken.None));
        var identity = first.WorkspaceId;
        Assert.IsFalse(await second.TryEnterAsync(CancellationToken.None));
        await first.ReleaseAsync();
        Assert.IsTrue(await second.TryEnterAsync(CancellationToken.None));
        Assert.AreEqual(identity, second.WorkspaceId);
        await second.ReleaseAsync();
    }

    [TestMethod]
    public async Task Losing_database_lease_does_not_grant_a_second_writer_while_file_lease_is_held()
    {
        var lease = new AsyncLease(() => ValueTask.CompletedTask);
        var first = CreateDistributedGate(new DelegateBackend(() => Task.FromResult<IAsyncDisposable?>(lease)));
        var second = CodeGenerationApplyGateTestSupport.CreateLocalGate(_workspaceRoot);
        Assert.IsTrue(await first.TryEnterAsync(CancellationToken.None));
        await lease.DisposeAsync();
        Assert.IsFalse(await second.TryEnterAsync(CancellationToken.None));
        await first.ReleaseAsync();
        Assert.IsTrue(await second.TryEnterAsync(CancellationToken.None));
        await second.ReleaseAsync();
    }

    [TestMethod]
    public async Task Distributed_gate_fails_when_backend_unavailable()
    {
        var gate = new CodeGenerationApplyGate(
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = _workspaceRoot,
                DistributedGateEnabled = true,
            }),
            new RejectingWorkspaceLockBackend());
        Assert.IsFalse(await gate.TryEnterAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Distributed_gate_acquires_and_releases_backend_lease()
    {
        var backend = new CountingWorkspaceLockBackend();
        var gate = new CodeGenerationApplyGate(
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = _workspaceRoot,
                DistributedGateEnabled = true,
            }),
            backend);
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        Assert.AreEqual(1, backend.AcquireCount);
        await gate.ReleaseAsync();
        Assert.AreEqual(1, backend.ReleaseCount);
    }

    private sealed class RejectingWorkspaceLockBackend : ICodeGenerationWorkspaceLockBackend
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string lockResource,
            CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class CountingWorkspaceLockBackend : ICodeGenerationWorkspaceLockBackend
    {
        public int AcquireCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<IAsyncDisposable?> TryAcquireAsync(
            string lockResource,
            CancellationToken cancellationToken)
        {
            AcquireCount++;
            return Task.FromResult<IAsyncDisposable?>(new Lease(() => ReleaseCount++));
        }

        private sealed class Lease(Action onDispose) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                onDispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}

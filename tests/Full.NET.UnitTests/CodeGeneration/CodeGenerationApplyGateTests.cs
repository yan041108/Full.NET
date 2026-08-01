using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class CodeGenerationApplyGateTests
{
    [TestMethod]
    public void Workspace_lock_resource_is_stable_for_normalized_paths()
    {
        var first = CodeGenerationWorkspaceLockResource.Create(@"C:\workspaces\codegen");
        var second = CodeGenerationWorkspaceLockResource.Create(@"C:\workspaces\codegen\");
        Assert.AreEqual(first, second);
        StringAssert.StartsWith(first, "fn:codegeneration:workspace:");
    }

    [TestMethod]
    public async Task Local_gate_rejects_second_concurrent_enter()
    {
        var gate = CodeGenerationApplyGateTestSupport.CreateLocalGate(@"C:\workspaces\codegen");
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        Assert.IsFalse(await gate.TryEnterAsync(CancellationToken.None));
        gate.Release();
    }

    [TestMethod]
    public async Task Distributed_gate_fails_when_backend_unavailable()
    {
        var gate = new CodeGenerationApplyGate(
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = @"C:\workspaces\codegen",
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
                WorkspaceRoot = @"C:\workspaces\codegen",
                DistributedGateEnabled = true,
            }),
            backend);
        Assert.IsTrue(await gate.TryEnterAsync(CancellationToken.None));
        Assert.AreEqual(1, backend.AcquireCount);
        gate.Release();
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
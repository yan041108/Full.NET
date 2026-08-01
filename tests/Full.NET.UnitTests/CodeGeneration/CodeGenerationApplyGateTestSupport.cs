using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

internal static class CodeGenerationApplyGateTestSupport
{
    public static CodeGenerationApplyGate CreateLocalGate(string workspaceRoot) =>
        new(
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = workspaceRoot,
            }),
            new NoOpWorkspaceLockBackend());

    private sealed class NoOpWorkspaceLockBackend : ICodeGenerationWorkspaceLockBackend
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string lockResource,
            CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }
}
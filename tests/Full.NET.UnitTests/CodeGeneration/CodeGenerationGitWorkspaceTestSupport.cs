using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Git;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.CodeGeneration;

internal static class CodeGenerationGitWorkspaceTestSupport
{
    public static CodeGenerationGitWorkspaceService CreateDisabled(string workspaceRoot) =>
        new(
            Options.Create(new CodeGenerationGitOptions()),
            Options.Create(new CodeGenerationApplyOptions
            {
                Enabled = true,
                WorkspaceRoot = workspaceRoot,
            }),
            new CodeGenerationGitWorkspaceServiceTests.RecordingGitCommandRunner(),
            NullLogger<CodeGenerationGitWorkspaceService>.Instance);
}
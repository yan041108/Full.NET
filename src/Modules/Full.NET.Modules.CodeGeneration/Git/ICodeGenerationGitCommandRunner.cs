namespace Full.NET.Modules.CodeGeneration.Git;

internal interface ICodeGenerationGitCommandRunner
{
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? configuration,
        CancellationToken cancellationToken);
}

internal sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
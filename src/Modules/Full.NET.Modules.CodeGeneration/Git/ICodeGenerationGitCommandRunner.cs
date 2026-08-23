namespace Full.NET.Modules.CodeGeneration.Git;

/// <summary>
/// 抽象 Git 命令执行入口，便于在测试中替换为不依赖真实 git 进程的实现；生产实现必须以子进程方式调用本地 git。
/// </summary>
internal interface ICodeGenerationGitCommandRunner
{
    /// <summary>
    /// 在指定工作目录执行 git 子进程，可选注入 -c 配置项（如凭据 Header）；取消令牌触发时终止整个进程树。
    /// </summary>
    /// <param name="workingDirectory">git 工作目录，必须已初始化为仓库。</param>
    /// <param name="arguments">git 子命令参数，由调用方负责白名单，实现不做转义。</param>
    /// <param name="configuration">可选的 git -c 键值对，用于注入临时凭据或作者信息。</param>
    /// <param name="cancellationToken">用于取消并终止进程树的令牌。</param>
    /// <returns>退出码、标准输出与标准错误；ExitCode == 0 视为成功。</returns>
    Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? configuration,
        CancellationToken cancellationToken);
}

/// <summary>
/// 保存 git 子进程的退出码与标准输出/错误；<see cref="IsSuccess"/> 以 ExitCode == 0 判定成功。
/// </summary>
internal sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    /// <summary>退出码为 0 时表示命令成功。</summary>
    public bool IsSuccess => ExitCode == 0;
}
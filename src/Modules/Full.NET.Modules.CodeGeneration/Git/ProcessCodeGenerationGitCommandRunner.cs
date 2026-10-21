using System.Diagnostics;
using System.Text;

namespace Full.NET.Modules.CodeGeneration.Git;

/// <summary>
/// 基于本地 git 可执行文件的命令运行器；输出统一按 UTF-8 解码，取消时终止整个进程树避免遗留子进程。
/// </summary>
internal sealed class ProcessCodeGenerationGitCommandRunner : ICodeGenerationGitCommandRunner
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// 启动 git 进程并等待退出，按顺序注入 -c 配置项与子命令参数；取消令牌触发时 Kill 整个进程树并传播取消异常。
    /// </summary>
    public async Task<GitCommandResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? configuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        // Git 可能等待凭据、网络或钩子；即使调用方处于不可取消的提交区间也必须释放工作区锁。
        var callerToken = cancellationToken;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        cancellationToken = deadline.Token;

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (configuration is not null)
        {
            foreach (var entry in configuration)
            {
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add($"{entry.Key}={entry.Value}");
            }
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start git process.");
        }

        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new GitCommandResult(process.ExitCode,
                await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // 退出和管道收尾先于工作区门释放，禁止超时后仍遗留修改同一工作区的 Git 子进程。
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            callerToken.ThrowIfCancellationRequested();
            return new GitCommandResult(-1, string.Empty, "Git command exceeded its execution deadline.");
        }
    }
}

using System.Diagnostics;

namespace Full.NET.Testing;

/// <summary>为原生用例准备 JIT Migrator 子进程并保留失败输出。</summary>
internal static class NativeMigratorProcess
{
    internal static ProcessStartInfo CreateStartInfo(string repositoryRoot, string outputDirectory, string seedProfile)
    {
        const string assemblyName = "Full.NET.Host.Migrator";
        // DLL、依赖图与运行配置由 Integration 的项目引用一同构建/复制，并参与输出摘要。
        // 不能退回 dotnet run 自动补构建，否则每个用例都会重新进入 MSBuild。
        foreach (var extension in new[] { "dll", "deps.json", "runtimeconfig.json" })
        {
            var artifact = Path.Combine(outputDirectory, $"{assemblyName}.{extension}");
            if (!File.Exists(artifact))
                throw new FileNotFoundException("缺少 JIT Migrator 产物；请先构建 Integration Release。", artifact);
        }
        var info = new ProcessStartInfo("dotnet")
        {
            // dotnet run 默认以项目目录加载 appsettings；直接执行 DLL 必须保留这个配置边界。
            WorkingDirectory = Path.Combine(repositoryRoot, "src", "Hosts", assemblyName),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { Path.GetFullPath(Path.Combine(outputDirectory, $"{assemblyName}.dll")),
            "migrate", "--seed", seedProfile })
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    internal static async Task RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 JIT Migrator。");
        // 两个管道同时泵送，取消时先结束进程树，再读到 EOF，不能遗留迁移或持有管道的后代。
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            throw;
        }
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"JIT Migrator 退出码 {process.ExitCode}。stderr: {stderr}\nstdout: {stdout}");
        }
    }
}

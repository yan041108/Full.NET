using System.Diagnostics;
using Full.NET.CodeGeneration.Cli;

namespace Full.NET.UnitTests.CodeGeneration;

[TestClass]
public sealed class DiagnoseSdkProcessTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Cancellation_stops_owned_probe_before_returning(bool alreadyCanceled)
    {
        using var process = StartProbe("ping 127.0.0.1 -n 30 >nul", "sleep 30");
        using var cancellation = new CancellationTokenSource();
        try
        {
            if (alreadyCanceled)
            {
                cancellation.Cancel();
            }
            else
            {
                cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));
            }

            var exception = await Assert.ThrowsAsync<OperationCanceledException>(
                () => DiagnoseCommand.ReadSdkProbeAsync(process, cancellation.Token));
            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(process.HasExited, "取消返回前必须停止本次启动的 SDK 探测进程。");
        }
        finally
        {
            // 即使旧实现的回归断言失败，也只清理本测试拥有的进程树。
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [TestMethod]
    public async Task Successful_probe_preserves_trimmed_version()
    {
        using var process = StartProbe("echo 10.0.100", "printf '10.0.100\\n'");
        var result = await DiagnoseCommand.ReadSdkProbeAsync(process, CancellationToken.None);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("10.0.100", result.Version);
        Assert.IsTrue(process.HasExited);
    }

    [TestMethod]
    public async Task Failed_probe_preserves_exit_code_and_empty_output()
    {
        using var process = StartProbe("exit /b 3", "exit 3");
        var result = await DiagnoseCommand.ReadSdkProbeAsync(process, CancellationToken.None);
        Assert.AreEqual(3, result.ExitCode);
        Assert.AreEqual(string.Empty, result.Version);
        Assert.IsTrue(process.HasExited);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Probe_deadline_stops_process_even_if_output_has_started(bool writesVersion)
    {
        // 有限延迟确保旧实现能退出并产生断言失败，不让 RED 本身依赖无限等待。
        using var process = StartProbe(writesVersion
            ? "echo 10.0.100 & ping 127.0.0.1 -n 3 >nul" : "ping 127.0.0.1 -n 3 >nul",
            writesVersion ? "printf '10.0.100\\n'; sleep 2" : "sleep 2");
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => DiagnoseCommand.ReadSdkProbeAsync(
                process, CancellationToken.None, TimeSpan.FromMilliseconds(100)));
            Assert.IsTrue(process.HasExited, "超时返回前必须停止本次启动的 SDK 探测进程。");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [TestMethod]
    public async Task Probe_finishes_normally_within_deadline()
    {
        using var process = StartProbe("echo 10.0.100", "printf '10.0.100\\n'");
        var result = await DiagnoseCommand.ReadSdkProbeAsync(process, CancellationToken.None, TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual("10.0.100", result.Version);
        Assert.IsTrue(process.HasExited);
    }

    [TestMethod]
    public async Task Caller_cancellation_takes_precedence_over_expired_deadline()
    {
        using var process = StartProbe("ping 127.0.0.1 -n 30 >nul", "sleep 30");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => DiagnoseCommand.ReadSdkProbeAsync(
                process, cancellation.Token, TimeSpan.Zero));
            Assert.AreEqual(cancellation.Token, exception.CancellationToken);
            Assert.IsTrue(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Timed_out_probe_returns_redacted_diagnostic_instead_of_version(bool sensitiveOutput)
    {
        using var process = StartProbe(sensitiveOutput
            ? "echo credential-probe & echo credential-probe 1>&2 & ping 127.0.0.1 -n 3 >nul"
            : "ping 127.0.0.1 -n 3 >nul",
            sensitiveOutput ? "printf 'credential-probe\\n'; printf 'credential-probe\\n' >&2; sleep 2" : "sleep 2");
        try
        {
            var finding = await DiagnoseCommand.DiagnoseSdkProbeAsync(
                process, CancellationToken.None, TimeSpan.FromMilliseconds(100));
            Assert.AreEqual("code_generation.sdk.probe_timeout", finding.Code);
            Assert.AreEqual("error", finding.Severity);
            Assert.IsFalse((finding.Message + finding.Hint).Contains("credential-probe", StringComparison.Ordinal));
            Assert.IsTrue(process.HasExited);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [TestMethod]
    public async Task Deadline_cancels_pipe_reads_when_parent_has_exited()
    {
        // 子进程只存活四秒；父进程已退出时也不能等到继承的输出管道自然关闭。
        using var process = StartProbe(
            "start /b ping.exe 127.0.0.1 -n 5 & exit /b 0",
            "sleep 4 & exit 0");
        try
        {
            Assert.IsTrue(process.WaitForExit(5000), "夹具父进程须先退出，才能验证遗留管道边界。");
            var elapsed = Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(() => DiagnoseCommand.ReadSdkProbeAsync(
                process, CancellationToken.None, TimeSpan.FromMilliseconds(100)));
            Assert.IsTrue(process.HasExited);
            Assert.IsLessThan(TimeSpan.FromSeconds(2), elapsed.Elapsed,
                "父进程退出后的管道收尾不能继续等待有限延迟子进程结束。");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static Process StartProbe(string windowsCommand, string unixCommand)
    {
        // 固定系统命令模拟探测阻塞和退出，不依赖安装的 SDK 版本或外部测试进程。
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(windowsCommand);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(unixCommand);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("测试进程未启动。");
    }
}

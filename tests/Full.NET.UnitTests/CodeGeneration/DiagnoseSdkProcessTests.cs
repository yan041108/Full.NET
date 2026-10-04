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

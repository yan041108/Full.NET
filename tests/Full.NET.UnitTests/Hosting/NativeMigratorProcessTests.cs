using System.Diagnostics;
using System.Text.Json;
using Full.NET.Testing;

namespace Full.NET.UnitTests.Hosting;

[TestClass]
public sealed class NativeMigratorProcessTests
{
    [TestMethod]
    public void Command_runs_built_assembly_without_build_or_restore()
    {
        using var fixture = new ProbeFixture();
        fixture.CreateArtifacts();
        var info = NativeMigratorProcess.CreateStartInfo(fixture.Directory, fixture.Directory);
        CollectionAssert.AreEqual(new[] { Path.Combine(fixture.Directory, "Full.NET.Host.Migrator.dll"),
            "migrate", "--seed", "development" }, info.ArgumentList.ToArray());
        Assert.AreEqual(Path.Combine(fixture.Directory, "src", "Hosts", "Full.NET.Host.Migrator"), info.WorkingDirectory);
        Assert.IsTrue(info.RedirectStandardOutput && info.RedirectStandardError);
    }

    [TestMethod]
    [DataRow("dll")]
    [DataRow("deps.json")]
    [DataRow("runtimeconfig.json")]
    public void Missing_artifact_fails_before_launch(string extension)
    {
        using var fixture = new ProbeFixture();
        fixture.CreateArtifacts();
        File.Delete(Path.Combine(fixture.Directory, $"Full.NET.Host.Migrator.{extension}"));
        var exception = Assert.ThrowsExactly<FileNotFoundException>(() =>
            NativeMigratorProcess.CreateStartInfo(fixture.Directory, fixture.Directory));
        StringAssert.Contains(exception.Message, "Integration");
    }

    [TestMethod]
    public async Task Large_stderr_does_not_block_stdout_or_completion()
    {
        using var fixture = new ProbeFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await NativeMigratorProcess.RunAsync(fixture.StartInfo("flood"), deadline.Token);
    }

    [TestMethod]
    public async Task Nonzero_exit_reports_both_streams_and_exit_code()
    {
        using var fixture = new ProbeFixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            NativeMigratorProcess.RunAsync(fixture.StartInfo("fail"), deadline.Token));
        StringAssert.Contains(exception.Message, "23");
        StringAssert.Contains(exception.Message, "migration-stdout");
        StringAssert.Contains(exception.Message, "migration-stderr");
    }

    [TestMethod]
    public async Task Cancellation_waits_for_parent_and_descendant_exit()
    {
        using var fixture = new ProbeFixture();
        using var cancel = new CancellationTokenSource();
        var pending = NativeMigratorProcess.RunAsync(fixture.StartInfo("cancel"), cancel.Token);
        var ids = await fixture.WaitForReadyAsync();
        cancel.Cancel();
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual(cancel.Token, exception.CancellationToken);
        Assert.IsFalse(ProbeFixture.IsRunning(ids.Pid), "父进程必须在取消返回前退出。");
        Assert.IsFalse(ProbeFixture.IsRunning(ids.ChildPid), "后代进程不能继续持有输出管道。");
    }

    [TestMethod]
    public async Task Precancelled_request_never_starts_process()
    {
        using var fixture = new ProbeFixture();
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            NativeMigratorProcess.RunAsync(fixture.StartInfo("cancel"), cancelled));
        await Task.Delay(300);
        Assert.IsFalse(File.Exists(fixture.ReadyPath));
    }

    private sealed class ProbeFixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "fullnet-migrator-probe", Guid.NewGuid().ToString("N"));
        public string ReadyPath => Path.Combine(Directory, "ready.json");

        public ProbeFixture() => System.IO.Directory.CreateDirectory(Directory);

        public void CreateArtifacts()
        {
            foreach (var extension in new[] { "dll", "deps.json", "runtimeconfig.json" })
                File.WriteAllText(Path.Combine(Directory, $"Full.NET.Host.Migrator.{extension}"), "fixture");
        }

        public ProcessStartInfo StartInfo(string mode)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Full.NET.slnx"))) root = root.Parent;
            var info = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            info.ArgumentList.Add(Path.Combine(root!.FullName, "tests", "testing", "fixtures", "migrator-process-probe.mjs"));
            info.ArgumentList.Add(mode);
            info.ArgumentList.Add(ReadyPath);
            return info;
        }

        public async Task<ProbeIds> WaitForReadyAsync()
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (File.Exists(ReadyPath))
                {
                    try { return JsonSerializer.Deserialize<ProbeIds>(File.ReadAllText(ReadyPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; }
                    catch (JsonException) { }
                }
                await Task.Delay(25);
            }
            throw new TimeoutException("子进程未就绪。");
        }

        public static bool IsRunning(int pid)
        {
            if (pid <= 0) return false;
            if (OperatingSystem.IsLinux())
            {
                // 非直接子进程退出后可能等待 PID 1 回收；僵尸已停止执行，不等于遗留运行进程。
                try
                {
                    var state = File.ReadAllText($"/proc/{pid}/stat");
                    if (state[state.LastIndexOf(')') + 2] == 'Z') return false;
                }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
            }
            try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }

        public void Dispose()
        {
            // RED 阶段原实现会遗留子进程；仅清理本夹具回执登记的 PID，不碰其他测试资源。
            if (File.Exists(ReadyPath))
            {
                var ids = JsonSerializer.Deserialize<ProbeIds>(File.ReadAllText(ReadyPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                foreach (var pid in new[] { ids.Pid, ids.ChildPid })
                {
                    if (!IsRunning(pid)) continue;
                    try { using var process = Process.GetProcessById(pid); process.Kill(true); process.WaitForExit(5000); }
                    catch (ArgumentException) { }
                }
            }
            System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed record ProbeIds(int Pid, int ChildPid);
}

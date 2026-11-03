using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Full.NET.IntegrationTests.NativeAot;

/// <summary>直接拥有独立角色的 ELF 进程；持续排空日志，取消后结束进程再释放证据。</summary>
internal sealed class NativeIndependentHostProcess : IAsyncDisposable
{
    private readonly Task<string> output;
    private readonly Task<string> errors;
    private readonly string evidence;
    internal Process Child { get; }

    private NativeIndependentHostProcess(Process process, string evidence)
    {
        Child = process;
        this.evidence = evidence;
        output = PumpAsync(process.StandardOutput);
        errors = PumpAsync(process.StandardError);
    }

    internal static string RequireArtifact(string role)
    {
        if (!OperatingSystem.IsLinux()) Assert.Inconclusive("独立角色 Native 验收要求 Linux ELF。");
        var key = role.Replace('-', '_').ToUpperInvariant();
        var path = Environment.GetEnvironmentVariable("FULLNET_NATIVE_" + key + "_PATH");
        var expected = Environment.GetEnvironmentVariable("FULLNET_NATIVE_" + key + "_SHA256");
        Assert.IsFalse(string.IsNullOrWhiteSpace(path), "必须由角色入口注入真实 ELF 路径。");
        Assert.IsTrue(File.Exists(path), "Native ELF 不存在。");
        var bytes = File.ReadAllBytes(path!);
        CollectionAssert.AreEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }, bytes.Take(4).ToArray());
        Assert.AreEqual(expected, Convert.ToHexStringLower(SHA256.HashData(bytes)), "ELF 与本次角色发布摘要不一致。");
        return path!;
    }

    internal static NativeIndependentHostProcess Start(string executable, IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?> environment, string label)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // 原生角色不继承托管诊断 hook，也不回退 dotnet 或触发隐式构建。
        start.Environment.Remove("DOTNET_STARTUP_HOOKS");
        foreach (var setting in environment) start.Environment[setting.Key] = setting.Value;
        var directory = Environment.GetEnvironmentVariable("FULLNET_NATIVE_ROLE_EVIDENCE_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(directory);
        var evidence = Path.Combine(directory, label + "-" + Guid.NewGuid().ToString("N"));
        return new(Process.Start(start) ?? throw new InvalidOperationException("独立 Native 角色未启动。"), evidence);
    }

    internal async Task<(int ExitCode, string Output, string Errors)> WaitAsync(CancellationToken token)
    {
        await Child.WaitForExitAsync(token);
        var stdout = await output;
        var stderr = await errors;
        await File.WriteAllTextAsync(evidence + ".stdout.log", stdout);
        await File.WriteAllTextAsync(evidence + ".stderr.log", stderr);
        return (Child.ExitCode, stdout, stderr);
    }

    internal async Task TerminateAsync(CancellationToken token)
    {
        if (Child.HasExited) return;
        using var signal = System.Diagnostics.Process.Start(new ProcessStartInfo("kill")
        {
            ArgumentList = { "-TERM", Child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            UseShellExecute = false, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("无法发送 Native 停机信号。");
        await signal.WaitForExitAsync(token);
        Assert.AreEqual(0, signal.ExitCode);
        await Child.WaitForExitAsync(token);
    }

    /// <summary>共用既有服务夹具时仍保留每个原生进程的完整输出，失败不能在清理时丢失。</summary>
    internal static async Task PreserveLogsAsync(string label, Task<string>? stdout, Task<string>? stderr)
    {
        var directory = Environment.GetEnvironmentVariable("FULLNET_NATIVE_ROLE_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var prefix = Path.Combine(directory, label + "-" + Guid.NewGuid().ToString("N"));
        if (stdout is not null) await File.WriteAllTextAsync(prefix + ".stdout.log", await stdout);
        if (stderr is not null) await File.WriteAllTextAsync(prefix + ".stderr.log", await stderr);
    }

    private static async Task<string> PumpAsync(StreamReader reader)
    {
        const int limit = 4 * 1024 * 1024;
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0) break;
            var accepted = Math.Min(read, limit - text.Length);
            text.Append(buffer, 0, accepted);
            truncated |= accepted < read;
        }
        if (truncated) text.Append("\nNATIVE_ROLE_LOG_TRUNCATED\n");
        return text.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!Child.HasExited)
            {
                Child.Kill(entireProcessTree: true);
                await Child.WaitForExitAsync();
            }
            await File.WriteAllTextAsync(evidence + ".stdout.log", await output);
            await File.WriteAllTextAsync(evidence + ".stderr.log", await errors);
        }
        finally { Child.Dispose(); }
    }
}

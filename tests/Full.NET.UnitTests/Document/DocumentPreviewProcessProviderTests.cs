using System.Diagnostics;
using System.Text.Json;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Microsoft.Extensions.Options;
using NSubstitute;
using Errors = Full.NET.Modules.Document.Contracts.DocumentErrorCodes;

namespace Full.NET.UnitTests.Document;

/// <summary>夹具只启动本次 Node 转换进程，检查输出背压、退出码、预算和取消回收。</summary>
[TestClass]
public sealed class DocumentPreviewProcessProviderTests
{
    [TestMethod]
    [DataRow("flood", 0)]
    [DataRow("fail", 23)]
    public async Task Both_output_pipes_are_drained_and_exit_code_is_preserved(string mode, int exitCode)
    {
        using var fixture = new Fixture();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.AreEqual(exitCode, await DocumentPreviewProcessLifetime.RunAsync(fixture.StartInfo(mode), deadline.Token));
    }

    [TestMethod]
    public async Task Precancelled_conversion_never_launches_or_copies_input()
    {
        using var fixture = new Fixture();
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Provider("cancel")
            .ConvertAsync(Input(input), fixture.Directory, new(true)));
        Assert.AreEqual(0L, input.Position);
        Assert.IsFalse(File.Exists(fixture.ReadyPath));
    }

    [TestMethod]
    public async Task Invalid_process_stream_configuration_is_rejected_before_launch()
    {
        using var fixture = new Fixture();
        var info = fixture.StartInfo("cancel");
        info.RedirectStandardError = false;
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => DocumentPreviewProcessLifetime.RunAsync(info, default));
        Assert.IsFalse(File.Exists(fixture.ReadyPath));
    }

    [TestMethod]
    public async Task Caller_cancellation_reaps_owned_parent_and_descendant_before_return()
    {
        using var fixture = new Fixture();
        using var input = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Provider("cancel").ConvertAsync(Input(input), fixture.Directory, cancellation.Token);
        try
        {
            var ids = await fixture.WaitForReadyAsync();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.IsFalse(Fixture.IsRunning(ids.Pid));
            Assert.IsFalse(Fixture.IsRunning(ids.ChildPid));
            Assert.IsTrue(input.CanRead);
        }
        finally
        {
            cancellation.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
        }
    }

    [TestMethod]
    [DataRow("success", true, null)]
    [DataRow("oversize", false, "document.office_preview.output_too_large")]
    [DataRow("empty", false, "document.office_preview.conversion_failed")]
    [DataRow("missing", false, "document.office_preview.conversion_failed")]
    [DataRow("fail", false, "document.office_preview.conversion_failed")]
    public async Task Process_output_is_owned_and_budget_failures_use_stable_codes(string mode, bool success, string? error)
    {
        using var fixture = new Fixture();
        using var input = new MemoryStream([1]);
        var result = await fixture.Provider(mode).ConvertAsync(Input(input), fixture.Directory);
        Assert.AreEqual(success, result.IsSuccess);
        Assert.IsTrue(input.CanRead);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Directory, "source.docx")));
        if (success)
        {
            await using var output = result.Value!.PdfContent;
            Assert.AreEqual(3L, result.Value.PdfLength);
            Assert.AreEqual(1, output.ReadByte());
            Assert.AreEqual(2, output.ReadByte());
            Assert.AreEqual(3, output.ReadByte());
        }
        else
        {
            Assert.AreEqual(error, result.Error!.Code);
            var path = Path.Combine(fixture.Directory, "output.pdf");
            if (File.Exists(path))
            {
                // Windows 的独占打开同时证明失败返回前已关闭输出句柄。
                using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
    }

    [TestMethod]
    public async Task Actual_source_budget_is_enforced_before_converter_launch()
    {
        using var fixture = new Fixture();
        using var input = new MemoryStream([1, 2, 3, 4]);
        var result = await fixture.Provider("success").ConvertAsync(Input(input), fixture.Directory);
        Assert.AreEqual(Errors.OfficePreviewInputTooLarge, result.Error!.Code);
        Assert.IsTrue(input.CanRead);
        Assert.IsFalse(File.Exists(fixture.ReadyPath));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Directory, "output.pdf")));
    }

    private static DocumentOfficePreviewConversionInput Input(Stream input) =>
        new("../../name\" --unsafe.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", input, 1);

    private sealed class Fixture : IDisposable
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "fullnet-preview-provider-probe");
        private readonly List<(int Id, DateTime Started)> owned = [];
        public string Directory { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        private string ScriptPath => Path.Combine(Directory, "converter.mjs");
        public string ReadyPath => Path.Combine(Directory, "ready.json");

        internal Fixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(ScriptPath, """
                import { writeFileSync } from 'node:fs';
                import { spawn } from 'node:child_process';
                const [mode, input, output, receipt] = process.argv.slice(2);
                if (mode === 'cancel') {
                  const child = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], { stdio: 'inherit', windowsHide: true });
                  await new Promise((resolve, reject) => { child.once('spawn', resolve); child.once('error', reject); });
                  writeFileSync(receipt, JSON.stringify({ pid: process.pid, childPid: child.pid }));
                  setInterval(() => {}, 1000);
                } else {
                  for (let i = 0; i < 64; i++) {
                    await Promise.all([process.stdout, process.stderr].map(stream => new Promise((resolve, reject) =>
                      stream.write(Buffer.alloc(65536), error => error ? reject(error) : resolve()))));
                  }
                  if (mode !== 'missing' && mode !== 'fail') writeFileSync(output, Buffer.from(mode === 'empty' ? [] : mode === 'oversize' ? [1,2,3,4] : [1,2,3]));
                  process.exitCode = mode === 'fail' ? 23 : 0;
                }
                """);
        }

        public ProcessStartInfo StartInfo(string mode) => new("node")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { ScriptPath, mode, Path.Combine(Directory, "source.docx"), Path.Combine(Directory, "output.pdf"), ReadyPath },
        };

        public ExternalProcessDocumentOfficePreviewConversionProvider Provider(string mode)
        {
            var options = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
            options.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions
            {
                MaxInputBytes = 3, MaxOutputBytes = 3, TimeoutSeconds = 30,
                ExternalProcess = new() { ExecutablePath = "node", ArgumentTemplate = $"\"{ScriptPath}\" {mode} \"{{inputPath}}\" \"{{outputPath}}\" \"{ReadyPath}\"" },
            });
            return new(options);
        }

        public async Task<Ids> WaitForReadyAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (File.Exists(ReadyPath))
                {
                    try
                    {
                        var ids = JsonSerializer.Deserialize<Ids>(File.ReadAllText(ReadyPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                        foreach (var id in new[] { ids.Pid, ids.ChildPid })
                        {
                            using var process = Process.GetProcessById(id);
                            owned.Add((id, process.StartTime.ToUniversalTime()));
                        }
                        return ids;
                    }
                    catch (JsonException) { }
                }
                await Task.Delay(25);
            }
            throw new TimeoutException("转换夹具未就绪。");
        }

        public static bool IsRunning(int id)
        {
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    var stat = File.ReadAllText($"/proc/{id}/stat");
                    if (stat[stat.LastIndexOf(')') + 2] == 'Z') return false;
                }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                catch (IOException) { }
            }
            try { using var process = Process.GetProcessById(id); return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }

        public void Dispose()
        {
            foreach (var (id, started) in owned)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    // 回执和启动时间同时匹配，防止 PID 复用后误停其他窗口的进程。
                    if (!process.HasExited && process.StartTime.ToUniversalTime() == started) { process.Kill(true); process.WaitForExit(5000); }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            var fullPath = Path.GetFullPath(Directory);
            if (!fullPath.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("转换夹具目录越界。");
            System.IO.Directory.Delete(fullPath, true);
        }
    }

    private sealed record Ids(int Pid, int ChildPid);
}

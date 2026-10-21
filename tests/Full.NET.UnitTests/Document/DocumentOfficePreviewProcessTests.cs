using System.Diagnostics;
using Full.NET.Modules.Document.Configuration;
using Full.NET.Modules.Document.Providers.OfficePreview;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.Document;

/// <summary>真实转换子进程覆盖大量输出与宿主取消，防止管道死锁和孤儿进程。</summary>
[TestClass]
public sealed class DocumentOfficePreviewProcessTests
{
    [TestMethod]
    public async Task Large_stdout_and_stderr_are_drained_while_conversion_runs()
    {
        var directory = Directory.CreateTempSubdirectory("fullnet-preview-regression-").FullName;
        try
        {
            var script = Path.Combine(directory, "converter.cjs");
            await File.WriteAllTextAsync(script,
                "const fs=require('fs'); const data=Buffer.alloc(1048576,65); fs.writeSync(1,data); fs.writeSync(2,data); fs.writeFileSync(process.argv[3],'%PDF-test');");
            var provider = Create(script);
            await using var input = new MemoryStream([1, 2, 3]);
            var result = await provider.ConvertAsync(new("source.docx", "application/octet-stream", input, input.Length), directory);
            Assert.IsTrue(result.IsSuccess);
            await using var output = result.Value!.PdfContent;
            Assert.AreEqual(9, result.Value.PdfLength);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task Caller_cancellation_terminates_the_converter_before_returning()
    {
        var directory = Directory.CreateTempSubdirectory("fullnet-preview-regression-").FullName;
        try
        {
            var script = Path.Combine(directory, "converter.cjs");
            var pidPath = Path.Combine(directory, "converter.pid");
            await File.WriteAllTextAsync(script,
                "require('fs').writeFileSync(require('path').join(__dirname,'converter.pid'),String(process.pid));setInterval(()=>{},1000);");
            using var cancellation = new CancellationTokenSource();
            await using var input = new MemoryStream([1]);
            var conversion = Create(script).ConvertAsync(new("source.docx", "application/octet-stream", input, 1), directory, cancellation.Token);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(pidPath)) await Task.Delay(20, deadline.Token);
            var pid = int.Parse(await File.ReadAllTextAsync(pidPath), System.Globalization.CultureInfo.InvariantCulture);
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await conversion);
            try { using var process = Process.GetProcessById(pid); Assert.IsTrue(process.HasExited); }
            catch (ArgumentException) { }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static ExternalProcessDocumentOfficePreviewConversionProvider Create(string script)
    {
        var monitor = Substitute.For<IOptionsMonitor<DocumentOfficePreviewConversionOptions>>();
        monitor.CurrentValue.Returns(new DocumentOfficePreviewConversionOptions
        {
            TimeoutSeconds = 10,
            ExternalProcess = new() { ExecutablePath = OperatingSystem.IsWindows() ? "node.exe" : "node",
                ArgumentTemplate = $"\"{script}\" \"{{inputPath}}\" \"{{outputPath}}\"" },
        });
        return new(monitor);
    }
}

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Agents.Runtime;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>解析观测覆盖实际流与子进程；诊断不能泄漏来源或提前于资源清理结束。</summary>
[TestClass][DoNotParallelize]
public sealed class AiKnowledgeParseStageTelemetryTests
{
    [TestMethod]
    [DataRow("pdf", "ok")][DataRow("pdf", "stderr")][DataRow("pdf", "exit")][DataRow("pdf", "partial")]
    [DataRow("pdf", "trailing")][DataRow("pdf", "hash")][DataRow("pdf", "oversize")][DataRow("pdf", "error-flood")]
    [DataRow("pdf", "source-hash")][DataRow("pdf", "source-size")]
    [DataRow("docx", "ok")][DataRow("docx", "stderr")][DataRow("docx", "exit")][DataRow("docx", "partial")]
    [DataRow("docx", "trailing")][DataRow("docx", "hash")][DataRow("docx", "oversize")][DataRow("docx", "error-flood")]
    [DataRow("docx", "source-hash")][DataRow("docx", "source-size")]
    public async Task Parse_stage_sandbox_complete_or_denied_retains_original_result(string format, string fault)
    {
        using var f = new SandboxFixture(format, fault); using var capture = new AgentTelemetryCapture();
        var result = await f.Parse(); Assert.AreEqual(fault == "ok", result is not null);
        StageTelemetryAssertions.Verify(capture, "knowledge_parse_" + format, fault == "ok" ? "completed" : "denied");
        Assert.AreEqual(fault is not ("source-hash" or "source-size"), File.Exists(f.Started));
    }

    [TestMethod][DataRow("pdf", "transaction")][DataRow("pdf", "start")][DataRow("pdf", "configuration")]
    [DataRow("docx", "transaction")][DataRow("docx", "start")][DataRow("docx", "configuration")]
    public async Task Parse_stage_sandbox_exception_is_failed_without_private_details(string format, string fault)
    {
        using var f = new SandboxFixture(format, fault); using var capture = new AgentTelemetryCapture();
        f.State.HasTransaction = fault == "transaction";
        if (fault == "transaction") await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Parse());
        else if (fault == "configuration") await Assert.ThrowsExactlyAsync<ArgumentException>(() => f.Parse());
        else await Assert.ThrowsExactlyAsync<IOException>(() => f.Parse());
        StageTelemetryAssertions.Verify(capture, "knowledge_parse_" + format, "failed");
        Assert.IsFalse(File.Exists(f.Started));
    }

    [TestMethod][DataRow("pdf", false, "hang")][DataRow("pdf", true, "hang")]
    [DataRow("pdf", false, "complete-hang")][DataRow("pdf", true, "complete-hang")]
    [DataRow("docx", false, "hang")][DataRow("docx", true, "hang")]
    [DataRow("docx", false, "complete-hang")][DataRow("docx", true, "complete-hang")]
    public async Task Parse_stage_sandbox_cancel_stops_observation_after_child_cleanup(string format, bool deadline, string mode)
    {
        using var f = new SandboxFixture(format, mode, deadline ? 1000 : 10000);
        using var stop = new CancellationTokenSource(); using var capture = new AgentTelemetryCapture();
        var cleanedAtStop = false;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AgentExecutionTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = span => { if (Equals(span.GetTagItem("ai.stage"), "knowledge_parse_" + format)) cleanedAtStop = File.Exists(f.Cleaned); }
        };
        ActivitySource.AddActivityListener(listener);
        var work = f.Parse(stop.Token); await WaitForFile(f.Started); if (!deadline) stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        Assert.IsTrue(cleanedAtStop); StageTelemetryAssertions.Verify(capture, "knowledge_parse_" + format, "cancelled");
    }

    [TestMethod][DataRow("pdf")][DataRow("docx")]
    public async Task Parse_stage_sandbox_precancel_emits_nothing(string format)
    {
        using var f = new SandboxFixture(format, "ok"); using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Parse(new CancellationToken(true)));
        Assert.IsEmpty(capture.Spans); Assert.IsEmpty(capture.Measurements); Assert.IsFalse(File.Exists(f.Started));
    }

    [TestMethod]
    [DataRow("pdf", "sample")][DataRow("pdf", "started")][DataRow("pdf", "stopped")]
    [DataRow("pdf", "attempts")][DataRow("pdf", "duration")][DataRow("pdf", "none")]
    [DataRow("docx", "sample")][DataRow("docx", "started")][DataRow("docx", "stopped")]
    [DataRow("docx", "attempts")][DataRow("docx", "duration")][DataRow("docx", "none")]
    public async Task Parse_stage_sandbox_listener_failure_cannot_change_document(string format, string fault)
    {
        using var f = new SandboxFixture(format, "ok"); using var capture = StageTelemetryAssertions.FaultCapture(fault);
        Assert.IsNotNull(await f.Parse()); Assert.AreSame(capture.Root, Activity.Current);
    }

    [TestMethod][DataRow("success")][DataRow("format")][DataRow("hash")][DataRow("utf8")]
    [DataRow("empty")][DataRow("io")][DataRow("cancel")]
    public async Task Parse_stage_text_retains_result_exception_and_stream_ownership(string fault)
    {
        var bytes = fault == "utf8" ? new byte[] { 0xff } : Encoding.UTF8.GetBytes(fault == "empty" ? " " : "secret-source");
        using var stop = new CancellationTokenSource(); using var stream = new ControlledStream(bytes);
        var marker = new IOException("secret-source-and-credentials");
        if (fault == "io") stream.Failure = marker;
        if (fault == "cancel") stream.AfterRead = stop.Cancel;
        using var capture = new AgentTelemetryCapture();
        Task<AiKnowledgeTextParseResult> Parse() => AiKnowledgeTextParser.ParseAsync(stream,
            fault == "format" ? "secret.pdf" : "secret.txt", "text/plain", fault == "hash" ? new string('b', 64) : Hash(bytes), stop.Token);
        if (fault == "io") Assert.AreSame(marker, await Assert.ThrowsExactlyAsync<IOException>(Parse));
        else if (fault == "cancel") await Assert.ThrowsAsync<OperationCanceledException>(Parse);
        else Assert.AreEqual(fault == "success", (await Parse()).IsSuccess);
        Assert.IsFalse(stream.IsDisposed);
        StageTelemetryAssertions.Verify(capture, "knowledge_parse_text", fault == "success" ? "completed" : fault == "io" ? "failed" : fault == "cancel" ? "cancelled" : "denied");
    }

    [TestMethod]
    public async Task Parse_stage_text_precancel_never_reads_or_emits()
    {
        using var stream = new ControlledStream(Encoding.UTF8.GetBytes("secret-source")); using var capture = new AgentTelemetryCapture();
        await Assert.ThrowsAsync<OperationCanceledException>(() => AiKnowledgeTextParser.ParseAsync(stream, "secret.txt", "text/plain", Hash(stream.Bytes), new CancellationToken(true)));
        Assert.AreEqual(0, stream.Reads); Assert.IsFalse(stream.IsDisposed); Assert.IsEmpty(capture.Spans); Assert.IsEmpty(capture.Measurements);
    }

    [TestMethod][DataRow("sample")][DataRow("started")][DataRow("stopped")][DataRow("attempts")][DataRow("duration")][DataRow("none")]
    public async Task Parse_stage_text_listener_failure_cannot_change_document(string fault)
    {
        using var stream = new ControlledStream(Encoding.UTF8.GetBytes("secret-source")); using var capture = StageTelemetryAssertions.FaultCapture(fault);
        Assert.IsTrue((await AiKnowledgeTextParser.ParseAsync(stream, "secret.txt", "text/plain", Hash(stream.Bytes), default)).IsSuccess);
        Assert.IsFalse(stream.IsDisposed); Assert.AreSame(capture.Root, Activity.Current);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static async Task WaitForFile(string path)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!File.Exists(path)) await Task.Delay(20, timeout.Token);
    }

    /// <summary>流不替解析器检查取消；用于证明原边界仍拥有每次读取和取消判断。</summary>
    private sealed class ControlledStream : MemoryStream
    {
        internal byte[] Bytes { get; }
        internal ControlledStream(byte[] bytes) : base(bytes, false) { Bytes = bytes; }
        internal int Reads { get; private set; }
        internal bool IsDisposed { get; private set; }
        internal IOException? Failure { get; set; }
        internal Action? AfterRead { get; set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reads++; if (Failure is { } error) throw error;
            var count = Read(buffer.Span); AfterRead?.Invoke(); return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
    }

    private sealed class TransactionState : IDataTransactionState { public bool HasTransaction { get; set; } }

    /// <summary>真实 Node 子进程遵守生产管道协议；仅输出合成内容，不访问 Docker 或模型。</summary>
    private sealed class SandboxFixture : IDisposable
    {
        private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private readonly string format, fault, node, script;
        private readonly int timeout;
        private readonly byte[] source = Encoding.UTF8.GetBytes("synthetic-private-source");
        private readonly string directory = Path.Combine(Path.GetTempPath(), "fullnet-parse-stage-" + Guid.NewGuid().ToString("N"));
        internal string Started => Path.Combine(directory, "started");
        internal string Cleaned => Path.Combine(directory, "cleaned");
        internal TransactionState State { get; } = new();
        internal SandboxFixture(string format, string fault, int timeout = 10000)
        {
            this.format = format; this.fault = fault; this.timeout = timeout; Directory.CreateDirectory(directory);
            var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
            node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(folder => Path.GetFullPath(Path.Combine(folder, executable))).FirstOrDefault(File.Exists)
                ?? throw new AssertFailedException("需要真实本地 Node，不允许跳过。");
            script = Path.Combine(directory, "parse.mjs");
            File.WriteAllText(script, Script.Replace("FORMAT", format, StringComparison.Ordinal).Replace("MODE", fault, StringComparison.Ordinal), new UTF8Encoding(false));
            if (fault == "start") node = script;
            if (fault == "configuration") node = Path.Combine(directory, "missing-node");
        }
        internal async Task<object?> Parse(CancellationToken token = default)
        {
            var hash = fault == "source-hash" ? new string('b', 64) : Hash(source); var size = source.Length + (fault == "source-size" ? 1 : 0);
            return format == "pdf" ? await new AiKnowledgePdfSandboxParser(State, new(node, script, Image, timeout)).ParseAsync(source, hash, size, token)
                : await new AiKnowledgeDocxSandboxParser(State, new(node, script, Image, timeout)).ParseAsync(source, hash, size, token);
        }
        public void Dispose() => Directory.Delete(directory, true);
        private const string Script = """
            import {writeFileSync} from 'node:fs'; import {fileURLToPath} from 'node:url'; import path from 'node:path';
            import {createHash} from 'node:crypto';
            const folder=path.dirname(fileURLToPath(import.meta.url)), mode='MODE', format='FORMAT', args=process.argv.slice(2);
            const value=key=>args[args.indexOf(key)+1]; const chunks=[]; let count=0, started=false;
            if(value('--input-mode')!=='framed-v1')process.exit(7);
            process.stdin.on('data',bytes=>{
              chunks.push(bytes); count+=bytes.length; if(started||count!==Number(value('--size-bytes')))return; started=true;
              if(createHash('sha256').update(Buffer.concat(chunks)).digest('hex')!==value('--content-hash'))process.exit(9);
              if(mode!=='complete-hang')writeFileSync(path.join(folder,'started'),'1'); if(mode==='hang')return;
              const hash=mode==='hash'?'b'.repeat(64):value('--content-hash');
              let wire=JSON.stringify(format==='pdf'?{protocolVersion:1,parserVersion:'pdfpig-0.1.16-content-order-v1',contentHash:hash,pages:[{pageNumber:1,text:'secret-source'}]}:
                {protocolVersion:1,parserVersion:'docx-ooxml-body-v1',sizeBytes:count,contentHash:hash,text:'hello DOCX\n',blocks:[{sequence:0,kind:'paragraph',offset:0,length:10,paragraphNumber:1,tableNumber:null,rowNumber:null,cellNumber:null,cellParagraphCount:0,isHeader:false}]});
              if(mode==='partial')wire=wire.slice(0,-1); if(mode==='trailing')wire+='{}'; if(mode==='oversize')wire='x'.repeat(17*1024*1024);
              if(mode==='stderr')process.stderr.write('secret-source'); if(mode==='error-flood')process.stderr.write('x'.repeat(5000));
              if(mode==='complete-hang'){process.stdout.end(wire,()=>writeFileSync(path.join(folder,'started'),'1'));return;}
              process.stdout.end(wire,()=>{process.stdin.destroy();process.exitCode=mode==='exit'?2:0;});
            });
            process.stdin.on('end',()=>{if(mode==='hang'||mode==='complete-hang')setTimeout(()=>{writeFileSync(path.join(folder,'cleaned'),'1');process.exitCode=2;},120);else process.exit(2);});
            """;
    }
}

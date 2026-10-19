using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Ai.Knowledge;

namespace Full.NET.UnitTests.Ai;

/// <summary>单元测试 AI：Knowledge Docx Sandbox Parser。</summary>
[TestClass]
public sealed class AiKnowledgeDocxSandboxParserTests
{
    private static readonly byte[] Source = Encoding.UTF8.GetBytes("%DOCX-1.7\nsynthetic");
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData(Source));
    private const string Image = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [TestMethod]
    public async Task Complete_process_exit_and_protocol_are_required_before_accepting_pages()
    {
        using var fixture = new ChildFixture("ok"); var source = Source.ToArray();
        var result = await fixture.Parser.ParseAsync(source, Hash.ToUpperInvariant(), Source.Length, CancellationToken.None);
        Assert.IsNotNull(result); Assert.AreEqual(Hash, result.ContentHash);
        Assert.AreEqual(Source.Length, result.SizeBytes); Assert.AreEqual("hello DOCX\n", result.Text);

    }

    [TestMethod]
    [DataRow("stderr")]
    [DataRow("exit")]
    [DataRow("partial")]
    [DataRow("trailing")]
    [DataRow("hash")]
    [DataRow("oversize")]
    [DataRow("error-flood")]
    public async Task Invalid_transport_or_output_never_returns_pages(string mode)
    {
        using var fixture = new ChildFixture(mode); var source = Source.ToArray();
        Assert.IsNull(await fixture.Parser.ParseAsync(source, Hash, Source.Length, CancellationToken.None));
        Assert.IsTrue(File.Exists(fixture.Started));
    }

    [TestMethod]
    [DataRow("hash")]
    [DataRow("short")]
    [DataRow("long")]
    [DataRow("zero")]
    [DataRow("large")]
    public async Task Actual_source_must_match_trusted_identity_before_starting_child(string mode)
    {
        using var fixture = new ChildFixture("ok"); var source = Source.ToArray();
        var size = mode switch { "short" => Source.Length - 1, "long" => Source.Length + 1, "zero" => 0,
            "large" => AiKnowledgeDocxOutputReader.MaxSourceBytes + 1, _ => Source.Length };
        Assert.IsNull(await fixture.Parser.ParseAsync(source, mode == "hash" ? new string('b', 64) : Hash, size, CancellationToken.None));
        Assert.IsFalse(File.Exists(fixture.Started));
    }

    [TestMethod]
    public async Task Transaction_and_precancellation_prevent_child_start()
    {
        using var fixture = new ChildFixture("ok"); var source = Source.ToArray();
        fixture.State.HasTransaction = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Parser.ParseAsync(source, Hash, Source.Length, CancellationToken.None));
        fixture.State.HasTransaction = false;
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Parser.ParseAsync(source, Hash, Source.Length, new CancellationToken(true)));
        Assert.IsFalse(File.Exists(fixture.Started));
    }

    [TestMethod]
    [DataRow(false, "hang")]
    [DataRow(true, "hang")]
    [DataRow(false, "complete-hang")]
    [DataRow(true, "complete-hang")]
    public async Task Cancellation_or_deadline_closes_stdin_and_waits_for_child_cleanup(bool deadline, string mode)
    {
        using var fixture = new ChildFixture(mode, deadline ? 1000 : 10000); var source = Source.ToArray();
        using var cancellation = new CancellationTokenSource();
        var work = fixture.Parser.ParseAsync(source, Hash, Source.Length, cancellation.Token);
        await WaitForFile(fixture.Started); if (!deadline) cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => work);
        Assert.IsTrue(File.Exists(fixture.Cleaned));
    }

    [TestMethod]
    public async Task Source_is_frozen_before_first_wait_and_transaction_is_checked_after_process_wait()
    {
        using var fixture = new ChildFixture("hold-success"); var source = Source.ToArray();
        var work = fixture.Parser.ParseAsync(source, Hash, Source.Length, CancellationToken.None);
        await WaitForFile(fixture.Started); Array.Fill(source, (byte)'X'); fixture.State.HasTransaction = true;
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "release"), "1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => work);
    }

    [TestMethod]
    public async Task Caller_mutation_cannot_change_the_verified_source_sent_to_child()
    {
        using var fixture = new ChildFixture("hold-success"); var source = Source.ToArray();
        var work = fixture.Parser.ParseAsync(source, Hash, Source.Length, CancellationToken.None);
        await WaitForFile(fixture.Started); Array.Fill(source, (byte)'X');
        File.WriteAllText(Path.Combine(fixture.DirectoryPath, "release"), "1");
        Assert.IsNotNull(await work); Assert.IsTrue(source.All(value => value == (byte)'X'));
    }

    [TestMethod]
    public async Task Host_credentials_and_node_injection_are_not_inherited()
    {
        using var fixture = new ChildFixture("environment"); var source = Source.ToArray();
        // 合成哨兵只在当前进程设置；测试结束恢复原值，不打印环境或真实凭据。
        const string key = "FULLNET_DOCX_SECRET_CANARY"; var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "synthetic");
            Assert.IsNotNull(await fixture.Parser.ParseAsync(source, Hash, Source.Length, CancellationToken.None));
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [TestMethod]
    [DataRow("mutable")]
    [DataRow("relative-node")]
    [DataRow("relative-cli")]
    [DataRow("missing")]
    [DataRow("timeout")]
    public async Task Invalid_trusted_configuration_fails_before_child_start(string mode)
    {
        using var fixture = new ChildFixture("ok"); var source = Source.ToArray();
        var options = fixture.Options;
        options = mode switch { "mutable" => options with { ImageId = "parser:latest" },
            "relative-node" => options with { NodeExecutablePath = "node" }, "relative-cli" => options with { CliScriptPath = "cli.mjs" },
            "missing" => options with { CliScriptPath = Path.Combine(fixture.DirectoryPath, "absent.mjs") },
            _ => options with { TimeoutMs = 30001 } };
        var parser = new AiKnowledgeDocxSandboxParser(fixture.State, options);
        await Assert.ThrowsAsync<ArgumentException>(() => parser.ParseAsync(source, Hash, Source.Length, CancellationToken.None));
        Assert.IsFalse(File.Exists(fixture.Started));
    }

    private static async Task WaitForFile(string path)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!File.Exists(path)) await Task.Delay(20, deadline.Token);
    }

    private sealed class TransactionState : IDataTransactionState { public bool HasTransaction { get; set; } }

    private sealed class ChildFixture : IDisposable
    {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "fullnet-docx-bridge-" + Guid.NewGuid().ToString("N"));
        internal string Started => Path.Combine(DirectoryPath, "started");
        internal string Cleaned => Path.Combine(DirectoryPath, "cleaned");
        internal TransactionState State { get; } = new();
        internal AiKnowledgeDocxSandboxOptions Options { get; }
        internal AiKnowledgeDocxSandboxParser Parser => new(State, Options);

        internal ChildFixture(string mode, int timeout = 10000)
        {
            Directory.CreateDirectory(DirectoryPath);
            var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
            var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(folder => Path.GetFullPath(Path.Combine(folder, executable))).FirstOrDefault(File.Exists);
            Assert.IsNotNull(node, "测试依赖本地 Node.js 可执行文件，不允许跳过桥接验证");
            var script = Path.Combine(DirectoryPath, "docx-sandbox-cli.mjs");
            File.WriteAllText(script, Script.Replace("MODE", mode, StringComparison.Ordinal), new UTF8Encoding(false));
            Options = new(node, script, Image, timeout);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, true);

        private const string Script = """
            import { existsSync, writeFileSync } from 'node:fs';
            import { createHash } from 'node:crypto';
            import { fileURLToPath } from 'node:url';
            import path from 'node:path';
            const folder = path.dirname(fileURLToPath(import.meta.url));
            const mode = 'MODE'; const args = process.argv.slice(2);
            const value = key => args[args.indexOf(key) + 1];
            if (value('--input-mode') !== 'framed-v1') process.exit(7);
            let count = 0, started = false; const chunks = [];
            process.stdin.on('data', bytes => {
              count += bytes.length;
              chunks.push(bytes);
              if (started || count !== Number(value('--size-bytes'))) return;
              started = true;
              if (mode !== 'complete-hang') writeFileSync(path.join(folder, 'started'), '1');
              if (createHash('sha256').update(Buffer.concat(chunks)).digest('hex') !== value('--content-hash')) process.exit(9);
              if (mode === 'hang') return;
              let wire = JSON.stringify({ protocolVersion: 1, parserVersion: 'docx-ooxml-body-v1', sizeBytes: Number(value('--size-bytes')),
                contentHash: mode === 'hash' ? 'b'.repeat(64) : value('--content-hash'), text: 'hello DOCX\n', blocks: [{sequence:0,kind:'paragraph',offset:0,length:10,paragraphNumber:1,tableNumber:null,rowNumber:null,cellNumber:null,cellParagraphCount:0,isHeader:false}] });
              if (mode === 'environment' && Object.keys(process.env).some(key => !['PATH', 'SystemRoot', 'SYSTEMROOT', 'COMSPEC',
                'USERPROFILE', 'HOME', 'DOCKER_HOST', 'DOCKER_TLS_VERIFY', 'DOCKER_CERT_PATH', 'DOCKER_CONFIG'].includes(key))) process.exit(8);
              if (mode === 'partial') wire = wire.slice(0, -1);
              if (mode === 'trailing') wire += '{}';
              if (mode === 'oversize') wire = 'x'.repeat(17 * 1024 * 1024);
              if (mode === 'stderr') process.stderr.write('synthetic');
              if (mode === 'error-flood') process.stderr.write('x'.repeat(5000));
              // 此场景先完成输出再挂起；提前宣告就绪会让取消关闭管道，引发 EPIPE 而跳过 EOF 清理。
              if (mode === 'complete-hang') { process.stdout.end(wire, () => writeFileSync(path.join(folder, 'started'), '1')); return; }
              const publish = () => process.stdout.end(wire, () => { process.stdin.destroy(); process.exitCode = mode === 'exit' ? 2 : 0; });
              if (mode === 'hold-success') {
                const timer = setInterval(() => { if (existsSync(path.join(folder, 'release'))) { clearInterval(timer); publish(); } }, 10);
              } else publish();
            });
            process.stdin.on('end', () => {
              if (mode === 'hang' || mode === 'complete-hang') setTimeout(() => { writeFileSync(path.join(folder, 'cleaned'), '1'); process.exitCode = 2; }, 120);
              else process.exit(2);
            });
            """;
    }
}

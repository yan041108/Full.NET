using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Full.NET.ArchitectureTests;

/// <summary>按 BCL 符号识别同步等待，避免把业务 Result 属性误判为 Task.Result。</summary>
[TestClass]
public sealed class AsyncBoundaryTests
{
    // 与启用 ImplicitUsings 的 SDK/Web SDK 保持一致，避免 File/HttpClient 推断出的 Task 丢失符号。
    private static readonly string GlobalUsings = """
        global using System;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using System.Collections.Generic;
        global using System.Linq;
        global using System.IO;
        global using System.Net.Http;
        global using System.Net.Http.Json;
        global using Microsoft.AspNetCore.Builder;
        global using Microsoft.AspNetCore.Hosting;
        global using Microsoft.AspNetCore.Http;
        global using Microsoft.AspNetCore.Routing;
        global using Microsoft.Extensions.Configuration;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Hosting;
        global using Microsoft.Extensions.Logging;
        """;

    [TestMethod]
    public void Production_sources_do_not_block_tasks_or_sleep_or_offload_to_thread_pool()
    {
        var root = ArchitectureRepositoryRoot.Find();
        var sources = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or ".worktrees"))
            .Select(path => (Path: path, Source: File.ReadAllText(path))).ToArray();
        var violations = FindProductionViolations(sources);
        Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Checker_resolves_implicit_io_task_types()
    {
        Assert.HasCount(1, FindProductionViolations([("Sample.cs", "class Sample { string Read() => File.ReadAllTextAsync(\"input\").Result; }")]));
    }

    [TestMethod]
    public void Checker_covers_static_generic_run_and_unresolved_run_boundary()
    {
        Assert.HasCount(1, FindViolations([CSharpSyntaxTree.ParseText("using static System.Threading.Tasks.Task; class Sample { void M() { _ = Run<int>(() => 1); } }")]));
        Assert.IsEmpty(FindViolations([CSharpSyntaxTree.ParseText("class Sample { void M(UnknownBuilder builder) { builder.Build().Run(); } }")]));
        Assert.HasCount(1, FindViolations([CSharpSyntaxTree.ParseText("class Sample { void M() { Task.Run(unknownArgument); } }")]));
    }

    [TestMethod]
    public void Checker_scans_aot_and_jit_conditional_branches()
    {
        Assert.HasCount(2, FindProductionViolations([("Sample.cs", """
            class Sample {
                void Run(Task task) {
            #if FULLNET_AOT_COMPILE
                    task.Wait();
            #else
                    task.GetAwaiter().GetResult();
            #endif
                }
            }
            """)]));
    }

    private static List<string> FindProductionViolations((string Path, string Source)[] sources)
    {
        // 分别绑定 JIT 和 AOT 分支，不能让预处理器将生产路径静默排除。
        string[][] configurations = [[], ["FULLNET_AOT_COMPILE", "FULLNET_API_NATIVE_AOT"]];
        return configurations.SelectMany(symbols => FindViolations(sources.Select(source =>
            CSharpSyntaxTree.ParseText(source.Source, new CSharpParseOptions(preprocessorSymbols: symbols), source.Path)).ToArray()))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    [TestMethod]
    public void Checker_rejects_real_task_waits_including_multiline_and_task_run()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            class Sample {
                void Run(Task<int> task, ValueTask<int> value) {
                    _ = task.Result;
                    task.Wait();
                    task.ConfigureAwait(false).GetAwaiter()
                        .GetResult();
                    _ = value.Result;
                    Task.WaitAll(task);
                    Task.WaitAny(task);
                    Thread.Sleep(1);
                    _ = Task.Run(() => task.GetAwaiter().GetResult());
                    _ = task?.Result;
                    _ = Task.Factory.StartNew(() => 1);
                }
            }
            """);
        Assert.HasCount(11, FindViolations([tree]));
    }

    [TestMethod]
    public void Checker_allows_business_result_and_zero_timeout_probe_and_await()
    {
        var tree = CSharpSyntaxTree.ParseText("""
            class Sample {
                int Result => 1;
                async Task<int> Run(Task<int> task, SemaphoreSlim gate) {
                    _ = this.Result;
                    _ = gate.Wait(0);
                    return await task;
                }
            }
            """);
        Assert.IsEmpty(FindViolations([tree]));
    }

    private static List<string> FindViolations(SyntaxTree[] trees)
    {
        // 使用运行时与当前构建产物解析第三方返回类型；不执行被扫描的业务代码。
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create("AsyncBoundaryScan",
            trees.Append(CSharpSyntaxTree.ParseText(GlobalUsings)),
            paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var violations = new List<string>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                var candidate = node switch
                {
                    InvocationExpressionSyntax invocation => invocation.Expression switch
                    {
                        MemberAccessExpressionSyntax member => IsCandidate(member.Name.Identifier.ValueText),
                        MemberBindingExpressionSyntax member => IsCandidate(member.Name.Identifier.ValueText),
                        SimpleNameSyntax identifier => IsCandidate(identifier.Identifier.ValueText),
                        _ => false,
                    },
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == "Result",
                    MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText == "Result",
                    _ => false,
                };
                if (!candidate) continue;
                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol is null)
                {
                    // Run 不是等待原语；AppHost 的 Aspire 入口不在测试引用闭包中。
                    // Task.Run 是 BCL 静态方法，必须先确认接收者为 Task，不能把未知业务 Run 当作阻塞。
                    if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Run" } run }
                        && model.GetSymbolInfo(run.Expression).Symbol is not INamedTypeSymbol { Name: "Task" }) continue;
                    violations.Add($"{tree.FilePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: 无法解析边界候选 {node}");
                    continue;
                }
                var type = symbol.ContainingType?.OriginalDefinition;
                var ns = type?.ContainingNamespace.ToDisplayString();
                var blocked = ns == "System.Threading.Tasks" && type?.Name is "Task" or "ValueTask"
                    && symbol.Name is "Result" or "Wait" or "WaitAll" or "WaitAny" or "Run";
                blocked |= ns == "System.Runtime.CompilerServices" && symbol.Name == "GetResult"
                    && type!.Name.Contains("Awaiter", StringComparison.Ordinal);
                blocked |= ns == "System.Threading" && type?.Name == "Thread" && symbol.Name == "Sleep";
                blocked |= ns == "System.Threading.Tasks" && type?.Name == "TaskFactory" && symbol.Name == "StartNew";
                if (blocked) violations.Add($"{tree.FilePath}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: {symbol.ToDisplayString()}");
            }
        }
        return violations;
    }

    private static bool IsCandidate(string name) => name is "Wait" or "WaitAll" or "WaitAny" or "Run" or "GetResult" or "Sleep" or "StartNew";
}

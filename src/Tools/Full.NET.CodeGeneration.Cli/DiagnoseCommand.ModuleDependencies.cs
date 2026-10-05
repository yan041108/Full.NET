using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private sealed record StaticModuleDependencies(string Name, IReadOnlyList<string> Required, IReadOnlyList<string> Optional);
    private sealed class ModuleDeclarationUnreadableException : Exception { }

    private static void CheckStandaloneModuleDependencies(
        string workspacePath, IReadOnlySet<string> selected, List<DiagnoseFinding> findings)
    {
        try
        {
            // 只检查最终启用的官方节点；禁用模块的声明不参与当前图，也不执行源码或模块注册。
            var graph = selected.ToDictionary(name => name, name => ReadStaticModuleDependencies(workspacePath, name), StringComparer.Ordinal);
            var invalid = graph.Any(entry => entry.Value.Name != entry.Key
                || entry.Value.Required.Distinct(StringComparer.Ordinal).Count() != entry.Value.Required.Count
                || entry.Value.Required.Any(dependency => !selected.Contains(dependency))
                || entry.Value.Optional.Any(dependency => !DiagnosticModuleNames.Contains(dependency, StringComparer.Ordinal)
                    || entry.Value.Required.Contains(dependency, StringComparer.Ordinal)));
            if (!invalid)
            {
                var permanent = new HashSet<string>(StringComparer.Ordinal);
                var temporary = new HashSet<string>(StringComparer.Ordinal);
                bool Visit(string name)
                {
                    if (permanent.Contains(name)) return true;
                    if (!temporary.Add(name)) return false;
                    foreach (var dependency in graph[name].Required)
                        if (!Visit(dependency)) return false;
                    temporary.Remove(name);
                    permanent.Add(name);
                    return true;
                }
                invalid = selected.Any(name => !Visit(name));
            }

            findings.Add(invalid
                ? DiagnoseFinding.Error("DIAG_RUNTIME_MODULE_DEPENDENCIES_INVALID",
                    "最终启用的官方模块静态依赖声明无效、缺少必需依赖或存在循环。",
                    "核对启用集和所选官方模块源码的 Name、Dependencies、OptionalContractDependencies；必需依赖须启用且不得重复或成环，可选契约须为已知官方来源且不得与必需依赖重叠。诊断不会回显声明值或异常文本。")
                : DiagnoseFinding.Ok("DIAG_RUNTIME_MODULE_DEPENDENCIES_CLOSED",
                    "所选官方模块的固定源码声明形成闭合、无循环的静态依赖图；可选契约生产者允许未启用。未认证应用自有模块、编译、DI 或宿主启动。"));
        }
        catch (Exception exception) when (exception is ModuleDeclarationUnreadableException or IOException or UnauthorizedAccessException)
        {
            findings.Add(DiagnoseFinding.Warn("DIAG_RUNTIME_MODULE_DEPENDENCIES_UNVERIFIED",
                "所选官方模块源码不可读取或依赖声明不能静态判定；依赖图未认证。",
                "恢复所选官方模块源码；当前只支持直接模块类中的固定字符串与集合表达式，不执行动态表达式或条件声明。警告不能作为依赖图验收通过证据。"));
        }
    }

    private static StaticModuleDependencies ReadStaticModuleDependencies(string workspacePath, string name)
    {
        // 模块键已通过官方名单和安装范围准入，固定路径不接受配置提供的任意文件位置。
        var relative = name == "EnterpriseRequest"
            ? "samples/enterprise-request/src/Full.NET.Modules.EnterpriseRequest/EnterpriseRequestModule.cs"
            : "src/Modules/Full.NET.Modules." + name + "/" + name + "Module.cs";
        // 只读取文件语法根节点；不调用编译或程序集加载入口。
        var root = SyntaxFactory.ParseCompilationUnit(File.ReadAllText(Path.Combine(workspacePath, "framework/fullnet", relative)));
        if (root.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new ModuleDeclarationUnreadableException();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(declaration => declaration.Identifier.ValueText == name + "Module").ToArray();
        if (classes.Length != 1) throw new ModuleDeclarationUnreadableException();
        var module = classes[0];
        if (module.TypeParameterList is not null || module.Modifiers.Any(SyntaxKind.PartialKeyword)
            || module.Parent is not (BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
            || module.BaseList?.Types.Any(type => type.Type.ToString() == "IFullNetModule"
                || type.Type.ToString() == "Full.NET.Modularity.Modules.IFullNetModule") != true)
            throw new ModuleDeclarationUnreadableException();
        var properties = module.Members.OfType<PropertyDeclarationSyntax>()
            .Where(property => property.Identifier.ValueText is "Name" or "Dependencies" or "OptionalContractDependencies").ToArray();
        if (properties.Length == 0) throw new ModuleDeclarationUnreadableException();
        var metadataEnd = properties.Max(property => property.Span.End);
        // 方法体内的 AOT 条件不影响元数据；元数据之前或其中的条件分支不能只按默认符号认证。
        if (root.DescendantTrivia(descendIntoTrivia: true).Any(trivia => trivia.SpanStart < metadataEnd
            && trivia.Kind() is SyntaxKind.IfDirectiveTrivia or SyntaxKind.ElifDirectiveTrivia
                or SyntaxKind.ElseDirectiveTrivia or SyntaxKind.EndIfDirectiveTrivia))
            throw new ModuleDeclarationUnreadableException();

        ExpressionSyntax? ReadExpression(string propertyName, bool optional = false)
        {
            var matches = properties.Where(property => property.Identifier.ValueText == propertyName).ToArray();
            if (matches.Length == 0 && optional) return null;
            if (matches.Length != 1 || !matches[0].Modifiers.Any(SyntaxKind.PublicKeyword)
                || matches[0].Modifiers.Any(SyntaxKind.StaticKeyword)
                || matches[0].ExpressionBody is null)
                throw new ModuleDeclarationUnreadableException();
            return matches[0].ExpressionBody!.Expression;
        }
        return new StaticModuleDependencies(
            ReadLiteral(ReadExpression("Name")!),
            ReadLiteralCollection(ReadExpression("Dependencies")!),
            ReadExpression("OptionalContractDependencies", optional: true) is { } optionalExpression
                ? ReadLiteralCollection(optionalExpression) : []);
    }

    private static string ReadLiteral(ExpressionSyntax expression)
    {
        // null 是可判定的非法稳定键，归入声明错误；方法调用与其他表达式仅标为未认证。
        if (expression.IsKind(SyntaxKind.NullLiteralExpression)) return string.Empty;
        if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            throw new ModuleDeclarationUnreadableException();
        return literal.Token.ValueText;
    }

    private static IReadOnlyList<string> ReadLiteralCollection(ExpressionSyntax expression)
    {
        if (expression is not CollectionExpressionSyntax collection) throw new ModuleDeclarationUnreadableException();
        return collection.Elements.Select(element => element is ExpressionElementSyntax item
            ? ReadLiteral(item.Expression) : throw new ModuleDeclarationUnreadableException()).ToArray();
    }
}

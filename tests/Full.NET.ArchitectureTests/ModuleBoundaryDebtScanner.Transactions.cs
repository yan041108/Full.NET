using System.Text.RegularExpressions;

namespace Full.NET.ArchitectureTests;

internal static partial class ModuleBoundaryDebtScanner
{
    public static CrossModuleTransactionUsage[] ScanCrossModuleTransactionUsages(string root)
    {
        var portImplementations = ScanPortImplementations(root);
        var contractDefinitions = ScanContractDefinitions(root);
        var modulesRoot = Path.Combine(root, "src", "Modules");
        var usages = new List<CrossModuleTransactionUsage>();
        foreach (var path in Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutputPath(path)
                || path.Contains(".Contracts", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            var moduleMatch = ModuleDirectoryRegex().Match(relativePath);
            if (!moduleMatch.Success)
            {
                continue;
            }

            var consumerModule = NormalizeModuleName(moduleMatch.Groups["module"].Value);
            var content = File.ReadAllText(path);
            if (!content.Contains("ICommandTransaction", StringComparison.Ordinal)
                || (!content.Contains("ExecuteAsync", StringComparison.Ordinal)
                    && !content.Contains("ExecuteResultAsync", StringComparison.Ordinal)))
            {
                continue;
            }

            var className = ClassNameRegex().Match(content).Groups["name"].Value;
            if (string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            var crossModuleContracts = ParseCrossModuleContracts(
                content,
                consumerModule,
                contractDefinitions,
                portImplementations);
            if (crossModuleContracts.Count == 0)
            {
                continue;
            }

            foreach (var targetMethod in ExtractTransactionTargetMethods(content))
            {
                var methodBody = ExtractMethodBody(content, targetMethod);
                if (string.IsNullOrWhiteSpace(methodBody))
                {
                    continue;
                }

                foreach (var contract in crossModuleContracts.Values)
                {
                    if (!MethodBodyUsesField(methodBody, contract.FieldName))
                    {
                        continue;
                    }

                    usages.Add(new CrossModuleTransactionUsage(
                        consumerModule,
                        contract.OwnerModule,
                        relativePath,
                        string.Concat(className, ".", targetMethod),
                        contract.ContractType));
                }
            }
        }

        return usages
            .Distinct()
            .OrderBy(usage => usage.ConsumerModule, StringComparer.Ordinal)
            .ThenBy(usage => usage.EntryPoint, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>扫描各模块真正实现的 Contract Port，忽略仅在构造函数中消费接口的类型。</summary>
    /// <param name="root">仓库根目录。</param>
    /// <returns>以接口类型名为键、实现模块名为值的只读映射。</returns>
    public static IReadOnlyDictionary<string, string> ScanPortImplementations(string root)
    {
        var implementations = new Dictionary<string, string>(StringComparer.Ordinal);
        var modulesRoot = Path.Combine(root, "src", "Modules");
        foreach (var path in Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains(".Contracts", StringComparison.OrdinalIgnoreCase)
                || IsBuildOutputPath(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            var moduleMatch = ModuleDirectoryRegex().Match(relativePath);
            if (!moduleMatch.Success)
            {
                continue;
            }

            var implementationModule = NormalizeModuleName(moduleMatch.Groups["module"].Value);
            var content = File.ReadAllText(path);
            foreach (Match declaration in TypeImplementationRegex().Matches(content))
            {
                foreach (Match match in InterfaceTypeRegex().Matches(
                    declaration.Groups["contracts"].Value))
                {
                    var contractType = match.Groups["contract"].Value;
                    if (!contractType.StartsWith('I'))
                    {
                        continue;
                    }

                    implementations[contractType] = implementationModule;
                }
            }
        }

        return implementations;
    }

    public static IReadOnlyDictionary<string, string> ScanContractDefinitions(string root)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        var modulesRoot = Path.Combine(root, "src", "Modules");
        foreach (var path in Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (!path.Contains(".Contracts", StringComparison.OrdinalIgnoreCase)
                || IsBuildOutputPath(path))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
            var moduleMatch = ModuleDirectoryRegex().Match(relativePath);
            if (!moduleMatch.Success)
            {
                continue;
            }

            var contractModule = NormalizeModuleName(moduleMatch.Groups["module"].Value);
            foreach (Match match in ContractInterfaceRegex().Matches(File.ReadAllText(path)))
            {
                definitions[match.Groups["name"].Value] = contractModule;
            }
        }

        return definitions;
    }

    private static Dictionary<string, CrossModuleContractBinding> ParseCrossModuleContracts(
        string content,
        string consumerModule,
        IReadOnlyDictionary<string, string> contractDefinitions,
        IReadOnlyDictionary<string, string> portImplementations)
    {
        var bindings = new Dictionary<string, CrossModuleContractBinding>(StringComparer.Ordinal);
        foreach (Match match in ConstructorParameterRegex().Matches(content))
        {
            var contractType = match.Groups["type"].Value;
            var fieldName = match.Groups["name"].Value;
            var ownerModule = ResolveContractOwnerModule(
                consumerModule,
                contractType,
                contractDefinitions,
                portImplementations);
            if (ownerModule is null)
            {
                continue;
            }

            bindings[fieldName] = new CrossModuleContractBinding(ownerModule, contractType, fieldName);
        }

        return bindings;
    }

    private static string? ResolveContractOwnerModule(
        string consumerModule,
        string contractType,
        IReadOnlyDictionary<string, string> contractDefinitions,
        IReadOnlyDictionary<string, string> portImplementations)
    {
        if (!contractDefinitions.TryGetValue(contractType, out var contractModule))
        {
            return null;
        }

        if (!string.Equals(contractModule, consumerModule, StringComparison.OrdinalIgnoreCase))
        {
            return contractModule;
        }

        if (portImplementations.TryGetValue(contractType, out var implementationModule)
            && !string.Equals(implementationModule, consumerModule, StringComparison.OrdinalIgnoreCase))
        {
            return implementationModule;
        }

        return null;
    }

    private static IEnumerable<string> ExtractTransactionTargetMethods(string content)
    {
        foreach (Match match in TransactionTargetRegex().Matches(content))
        {
            var method = match.Groups["method"].Value;
            if (!string.IsNullOrWhiteSpace(method))
            {
                yield return method;
            }
        }
    }

    private static string ExtractMethodBody(string content, string methodName)
    {
        var match = MethodDeclarationRegex(methodName).Match(content);
        if (!match.Success)
        {
            return string.Empty;
        }

        var braceIndex = content.IndexOf('{', match.Index + match.Length);
        if (braceIndex < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var i = braceIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                depth++;
            }
            else if (content[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return content[braceIndex..(i + 1)];
                }
            }
        }

        return string.Empty;
    }

    private static bool MethodBodyUsesField(string methodBody, string fieldName) =>
        Regex.IsMatch(
            methodBody,
            "\\b" + Regex.Escape(fieldName) + "\\s*\\.",
            RegexOptions.CultureInvariant);

    private static string NormalizeModuleName(string directoryModuleName)
    {
        const string contractsSuffix = ".Contracts";
        var moduleName = directoryModuleName.EndsWith(
            contractsSuffix,
            StringComparison.OrdinalIgnoreCase)
            ? directoryModuleName[..^contractsSuffix.Length]
            : directoryModuleName;
        return moduleName.ToLowerInvariant();
    }

    private static string TransactionKey(CrossModuleTransactionUsage usage) =>
        string.Join('|', usage.File, usage.EntryPoint, usage.ContractType);

    private static string TransactionKey(CrossModuleTransactionDebt entry) =>
        string.Join('|', entry.File, entry.EntryPoint, entry.ContractType);

    private static bool IsBuildOutputPath(string path) =>
        path.Contains(
            string.Concat(Path.DirectorySeparatorChar, "bin", Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            string.Concat(Path.DirectorySeparatorChar, "obj", Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?:^|/)Full\.NET\.Modules\.(?<module>[^/]+)(?:/|$)")]
    private static partial Regex ModuleDirectoryRegex();

    [GeneratedRegex(@"internal\s+sealed\s+class\s+(?<name>[A-Za-z0-9_]+)")]
    private static partial Regex ClassNameRegex();

    [GeneratedRegex(@"(?<type>I[A-Za-z0-9_]+)\s+(?<name>[a-z][A-Za-z0-9_]*)")]
    private static partial Regex ConstructorParameterRegex();

    [GeneratedRegex(@"public\s+interface\s+(?<name>I[A-Za-z0-9_]+)")]
    private static partial Regex ContractInterfaceRegex();

    [GeneratedRegex(
        @"transaction\.Execute(?:Result)?Async\(\s*(?:\w+\s*=>\s*)?(?<method>[A-Za-z0-9_]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TransactionTargetRegex();

    /// <summary>匹配类型声明的实现列表，排除主构造函数中的接口参数。</summary>
    [GeneratedRegex(
        @"(?:class|record|struct)\s+[A-Za-z0-9_]+(?:\s*\([^{}]*\))?\s*:\s*(?<contracts>[^{]+)\{",
        RegexOptions.CultureInvariant)]
    private static partial Regex TypeImplementationRegex();

    /// <summary>从类型实现列表中提取接口类型名。</summary>
    [GeneratedRegex(@"(?:^|[\s,])(?<contract>I[A-Za-z0-9_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex InterfaceTypeRegex();

    private static Regex MethodDeclarationRegex(string methodName) =>
        new(
            "(?:^|[\\r\\n])\\s*(?:private|public|internal|protected)\\s+(?:async\\s+)?[\\w<>,\\.\\[\\]\\?\\s]+\\b"
            + Regex.Escape(methodName)
            + "\\s*\\(",
            RegexOptions.CultureInvariant);
}

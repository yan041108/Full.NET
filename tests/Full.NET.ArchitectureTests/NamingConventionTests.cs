using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class NamingConventionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Lazy<NamingDebtDocument> NamingDebt = new(ReadDebt);

    [TestMethod]
    public void CSharp_symbols_follow_project_conventions()
    {
        var typePattern = LoadPattern("dotnet", "typePattern");
        var interfacePattern = LoadPattern("dotnet", "interfacePattern");
        var parameterPattern = LoadPattern("dotnet", "parameterPattern");
        var prohibitedAbbreviations = LoadProfileArray("dotnet", "abbreviations")
            .Where(value => value.Length > 1)
            .Select(value => value.ToUpperInvariant())
            .ToArray();
        var offenders = new List<string>();
        foreach (var type in ProductionAssemblies.All.SelectMany(GetLoadableTypes))
        {
            if (IsGenerated(type))
            {
                continue;
            }

            var typeName = RemoveGenericArity(type.Name);
            if (!typePattern.IsMatch(typeName))
            {
                offenders.Add($"类型：{type.FullName}");
            }

            if (type.IsInterface && !interfacePattern.IsMatch(typeName))
            {
                offenders.Add($"接口：{type.FullName}");
            }

            var abbreviationTarget = type.IsInterface ? typeName[1..] : typeName;
            if (prohibitedAbbreviations.Any(abbreviationTarget.Contains))
            {
                offenders.Add($"缩写：{type.FullName}");
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName
                    || IsGenerated(method)
                    || type.IsSubclassOf(typeof(MulticastDelegate))
                    || method.GetBaseDefinition() != method
                    || (IsRecord(type) && method.Name == "Deconstruct"))
                {
                    continue;
                }

                if (IsAsyncReturnType(method.ReturnType)
                    && !method.Name.EndsWith("Async", StringComparison.Ordinal))
                {
                    offenders.Add($"异步方法：{type.FullName}.{method.Name}");
                }

                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name is not null
                        && !parameterPattern.IsMatch(parameter.Name))
                    {
                        offenders.Add(
                            $"方法参数：{type.FullName}.{method.Name}({parameter.Name})");
                    }
                }
            }
        }

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Permission_codes_follow_stable_contract_pattern()
    {
        var pattern = LoadPattern("contracts", "permission", "pattern");
        var offenders = ProductionAssemblies.All
            .SelectMany(GetLoadableTypes)
            .Where(type => !type.IsAbstract
                && typeof(IAuthorizationCatalogContributor).IsAssignableFrom(type))
            .SelectMany(type =>
            {
                var contributor = (IAuthorizationCatalogContributor)
                    Activator.CreateInstance(type, nonPublic: true)!;
                var file = ResolveSourceFile(type);
                return contributor.Permissions
                    .Where(item => !pattern.IsMatch(item.Code))
                    .Where(item => !IsDebt("permission", item.Code, file))
                    .Select(item => $"{file}: {item.Code}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Error_codes_follow_stable_contract_pattern()
    {
        var pattern = LoadPattern("contracts", "error", "pattern");
        var offenders = ProductionAssemblies.All
            .SelectMany(GetLoadableTypes)
            .Where(type => type.Name.EndsWith("ErrorCodes", StringComparison.Ordinal))
            .SelectMany(type => ReadStaticStringCatalog(type, "All")
                .Select(value => (Value: value, File: ResolveSourceFile(type))))
            .Where(item => !pattern.IsMatch(item.Value))
            .Where(item => !IsDebt("error_code", item.Value, item.File))
            .Select(item => $"{item.File}: {item.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Message_types_and_statement_ids_follow_stable_contract_patterns()
    {
        var messagePattern = LoadPattern("contracts", "message", "pattern");
        var statementPattern = LoadPattern("contracts", "statement", "pattern");
        var offenders = new List<string>();
        foreach (var type in ProductionAssemblies.All
            .SelectMany(GetLoadableTypes)
            .Where(type => !type.IsAbstract
                && typeof(IIntegrationEventHandler).IsAssignableFrom(type)))
        {
            var handler = (IIntegrationEventHandler)RuntimeHelpers.GetUninitializedObject(type);
            var file = ResolveSourceFile(type);
            if (!messagePattern.IsMatch(handler.EventType)
                && !IsDebt("message_type", handler.EventType, file))
            {
                offenders.Add($"消息类型 {file}: {handler.EventType}");
            }
        }

        foreach (var type in ProductionAssemblies.All.SelectMany(GetLoadableTypes))
        {
            var statements = ReadSqlStatements(type).ToArray();
            if (statements.Length == 0)
            {
                continue;
            }

            var file = ResolveSourceFile(type);
            foreach (var statement in statements)
            {
                if (!statementPattern.IsMatch(statement.Name)
                    && !IsDebt("statement_id", statement.Name, file))
                {
                    offenders.Add($"Statement ID {file}: {statement.Name}");
                }
            }
        }

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<SqlStatement> ReadSqlStatements(Type type)
    {
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        return type.GetFields(flags)
            .Where(field => field.FieldType == typeof(SqlStatement))
            .Select(field => (SqlStatement?)field.GetValue(null))
            .Concat(type.GetProperties(flags)
                .Where(property => property.PropertyType == typeof(SqlStatement)
                    && property.GetIndexParameters().Length == 0)
                .Select(property => (SqlStatement?)property.GetValue(null)))
            .OfType<SqlStatement>();
    }

    private static IEnumerable<string> ReadStaticStringCatalog(Type type, string propertyName)
    {
        var property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return property?.GetValue(null) as IEnumerable<string> ?? [];
    }

    private static bool IsDebt(string kind, string value, string file)
    {
        return NamingDebt.Value.Items.Any(item => item.Kind == kind
            && item.Value == value
            && NormalizePath(item.File) == file);
    }

    private static NamingDebtDocument ReadDebt()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "contracts/naming/naming-debt.json");
        return JsonSerializer.Deserialize<NamingDebtDocument>(
            File.ReadAllText(path),
            JsonOptions) ?? throw new InvalidDataException("NamingDebtV1 结构无效。");
    }

    private static Regex LoadPattern(params string[] path)
    {
        using var profile = LoadProfile();
        var element = WalkProfile(profile.RootElement, path);
        return new Regex(
            element.GetString() ?? throw new InvalidDataException("Naming Profile 正则不能为空。"),
            RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<string> LoadProfileArray(params string[] path)
    {
        using var profile = LoadProfile();
        return WalkProfile(profile.RootElement, path)
            .EnumerateArray()
            .Select(item => item.GetString()
                ?? throw new InvalidDataException("Naming Profile 数组项不能为空。"))
            .ToArray();
    }

    private static JsonDocument LoadProfile()
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "contracts/naming/fullnet-naming-profile.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonElement WalkProfile(JsonElement element, IEnumerable<string> path)
    {
        foreach (var segment in path)
        {
            element = element.GetProperty(segment);
        }

        return element;
    }

    private static string ResolveSourceFile(Type type, bool required = true)
    {
        if (type.IsNested && type.DeclaringType is not null)
        {
            return ResolveSourceFile(type.DeclaringType, required);
        }

        var root = FindRepositoryRoot();
        var matches = Directory
            .EnumerateFiles(Path.Combine(root, "src"), $"{type.Name}.cs", SearchOption.AllDirectories)
            .ToArray();
        if (matches.Length == 1)
        {
            return NormalizePath(Path.GetRelativePath(root, matches[0]));
        }

        if (matches.Length == 0)
        {
            var declarationPattern = new Regex(
                $@"\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+{Regex.Escape(type.Name)}\b",
                RegexOptions.CultureInvariant);
            matches = Directory
                .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .Where(path => declarationPattern.IsMatch(File.ReadAllText(path)))
                .ToArray();
            if (matches.Length == 1)
            {
                return NormalizePath(Path.GetRelativePath(root, matches[0]));
            }
        }

        if (!required && matches.Length == 0)
        {
            return string.Empty;
        }

        throw new InvalidOperationException(
            $"类型 {type.FullName} 必须能唯一映射到源文件，实际匹配 {matches.Length} 个。");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static bool IsGenerated(MemberInfo member) =>
        member.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || member.Name.Contains('<', StringComparison.Ordinal)
        || (member.DeclaringType is not null && IsGenerated(member.DeclaringType));

    private static bool IsRecord(Type type) =>
        type.GetProperty(
            "EqualityContract",
            BindingFlags.Instance | BindingFlags.NonPublic)?.DeclaringType == type;

    private static bool IsAsyncReturnType(Type type) =>
        type == typeof(Task)
        || type == typeof(ValueTask)
        || (type.IsGenericType
            && type.GetGenericTypeDefinition() is { } definition
            && (definition == typeof(Task<>) || definition == typeof(ValueTask<>)));

    private static string RemoveGenericArity(string value) =>
        value.Split('`', 2)[0];

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 Full.NET 仓库根目录。");
    }

    private sealed record NamingDebtDocument(
        int SchemaVersion,
        IReadOnlyList<NamingDebtItem> Items);

    private sealed record NamingDebtItem(
        string Kind,
        string Value,
        string File,
        string Reason,
        string RemovalMilestone);

}

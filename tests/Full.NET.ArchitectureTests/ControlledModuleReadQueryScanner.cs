using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Full.NET.ArchitectureTests;

/// <summary>只放行精确登记的后台静态查询；登记不豁免同文件的其他表访问。</summary>
internal static class ControlledModuleReadQueryScanner
{
    internal sealed record Dependency(string Module, string Table, string[] Columns);

    internal sealed record Entry(
        string SourceModule, string File, string Member, string StatementName,
        string SqlSha256, string Purpose, Dependency[] Dependencies, string SecurityNotes);

    internal sealed record Document(int SchemaVersion, Entry[] Entries);

    internal sealed record AllowedRead(string File, TextSpan Span);

    internal sealed record Result(AllowedRead[] Allowed, string[] Violations);

    internal static Document Load(string root) => JsonSerializer.Deserialize<Document>(
        File.ReadAllText(Path.Combine(root, "contracts/architecture/module-read-queries.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Missing module read query catalog.");

    internal static string Hash(string sql) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n", StringComparison.Ordinal))));

    /// <summary>先校验目录、源声明和依赖，再返回允许通过所有权扫描的 SQL 字面量位置。</summary>
    internal static Result Analyze(
        Document document,
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyDictionary<string, string[]> moduleDependencies)
    {
        var errors = new List<string>();
        var allowed = new List<AllowedRead>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (document.SchemaVersion != 1 || document.Entries is null)
        {
            return new([], ["Unsupported or incomplete module read query catalog."]);
        }

        foreach (var entry in document.Entries)
        {
            var before = errors.Count;
            var identity = $"{entry.File}:{entry.Member}";
            if (new[] { entry.SourceModule, entry.File, entry.Member, entry.StatementName,
                    entry.SqlSha256, entry.Purpose, entry.SecurityNotes }.Any(string.IsNullOrWhiteSpace)
                || entry.Dependencies is not { Length: > 0 })
            {
                errors.Add($"Incomplete query registration: {identity}");
                continue;
            }

            if (!keys.Add(identity))
            {
                errors.Add($"Duplicate query registration: {identity}");
            }

            if (new[] { entry.SourceModule, entry.File, entry.Member, entry.StatementName }
                .Any(value => value.IndexOfAny(['*', '?', '\\']) >= 0 || value.Contains("..", StringComparison.Ordinal))
                || !Regex.IsMatch(entry.File,
                    @"^src/Modules/Full\.NET\.Modules\.[A-Za-z0-9]+/(?:Features/[A-Za-z0-9]+/)?Queries/[A-Za-z0-9]+Sql\.cs$")
                || !entry.File.StartsWith($"src/Modules/Full.NET.Modules.{entry.SourceModule}/", StringComparison.OrdinalIgnoreCase)
                || entry.Purpose is not ("admin-list" or "admin-search" or "admin-report"))
            {
                errors.Add($"Query identity, location or purpose is not allowed: {identity}");
                continue;
            }

            if (!sources.TryGetValue(entry.File, out var source))
            {
                errors.Add($"Stale query file: {identity}");
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(source);
            var syntax = tree.GetRoot();
            var fields = syntax.DescendantNodes().OfType<FieldDeclarationSyntax>().ToArray();
            // 专用文件只保存静态 SqlStatement；方法、构造器、属性和事务代码均不能混入豁免范围。
            var invalidMembers = syntax.DescendantNodes().OfType<MemberDeclarationSyntax>()
                .Any(member => member is not (BaseNamespaceDeclarationSyntax or ClassDeclarationSyntax or FieldDeclarationSyntax));
            if (tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                || invalidMembers || syntax.DescendantNodes().OfType<GlobalStatementSyntax>().Any()
                || syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(type =>
                    !type.Modifiers.Any(SyntaxKind.StaticKeyword) || type.Modifiers.Any(SyntaxKind.PartialKeyword))
                || fields.Any(field => !field.Modifiers.Any(SyntaxKind.StaticKeyword)
                    || !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                    || field.Declaration.Type.ToString() != "SqlStatement"))
            {
                errors.Add($"Query file must contain only static readonly SqlStatement declarations: {identity}");
                continue;
            }

            var variables = fields.SelectMany(field => field.Declaration.Variables)
                .Where(variable => $"{variable.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText}.{variable.Identifier.ValueText}" == entry.Member)
                .ToArray();
            if (variables.Length != 1 || variables[0].Initializer?.Value is not BaseObjectCreationExpressionSyntax creation
                || creation.ArgumentList?.Arguments.Count is not (3 or 4)
                || creation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax name
                || !name.IsKind(SyntaxKind.StringLiteralExpression)
                || creation.ArgumentList.Arguments[1].Expression is not LiteralExpressionSyntax sql
                || !sql.IsKind(SyntaxKind.StringLiteralExpression))
            {
                errors.Add($"Query must be a named static SQL literal: {identity}");
                continue;
            }

            var text = sql.Token.ValueText;
            if (name.Token.ValueText != entry.StatementName || Hash(text) != entry.SqlSha256)
            {
                errors.Add($"Query statement or SQL digest drift: {identity}");
            }

            var tokens = ReadOnlySql(text);
            if (tokens is null)
            {
                errors.Add($"Query is not a supported read-only SELECT: {identity}");
                continue;
            }

            var foreignTables = Regex.Matches(tokens, @"\bfn_[a-z0-9]+_[a-z0-9_]+\b", RegexOptions.IgnoreCase)
                .Select(match => match.Value.ToLowerInvariant())
                .Where(table => !ModuleTableOwnershipTests.OwnsTable(entry.SourceModule, table))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!foreignTables.SetEquals(entry.Dependencies.Select(item => item.Table))
                || entry.Dependencies.Select(item => item.Table).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entry.Dependencies.Length)
            {
                errors.Add($"Query table dependencies are incomplete or duplicated: {identity}");
            }

            foreach (var dependency in entry.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.Module) || string.IsNullOrWhiteSpace(dependency.Table)
                    || !ModuleTableOwnershipTests.OwnsTable(dependency.Module, dependency.Table)
                    || !HasDependency(entry.SourceModule, dependency.Module, moduleDependencies))
                {
                    errors.Add($"Query owner must be a required runtime dependency: {identity}");
                }

                var aliases = Regex.Matches(tokens,
                    @"\b(?:FROM|JOIN)\s+" + Regex.Escape(dependency.Table ?? "") + @"\s+(?:AS\s+)?(?<alias>[a-z_][a-z0-9_]*)\b",
                    RegexOptions.IgnoreCase).Select(match => match.Groups["alias"].Value).ToArray();
                var columns = aliases.SelectMany(alias => Regex.Matches(tokens,
                    @"\b" + Regex.Escape(alias) + @"\s*\.\s*(?<column>[a-z_][a-z0-9_]*|\*)", RegexOptions.IgnoreCase)
                    .Select(match => match.Groups["column"].Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (aliases.Length == 0 || columns.Count == 0 || columns.Contains("*")
                    || dependency.Columns is not { Length: > 0 }
                    || !columns.SetEquals(dependency.Columns)
                    || dependency.Columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != dependency.Columns.Length)
                {
                    errors.Add($"Query columns must be qualified and exactly registered: {identity} -> {dependency.Table}");
                }
            }

            var typeName = entry.Member.Split('.')[0];
            var consumers = sources.Where(pair => pair.Key != entry.File
                && Regex.IsMatch(pair.Value, @"\b" + Regex.Escape(typeName) + @"\b")).ToArray();
            if (consumers.Length == 0 || consumers.Any(pair =>
                !pair.Key.StartsWith($"src/Modules/Full.NET.Modules.{entry.SourceModule}/", StringComparison.OrdinalIgnoreCase)
                || !pair.Key.Contains("/Queries/", StringComparison.Ordinal)
                || Regex.IsMatch(pair.Value, @"\b(ICommandTransaction|DbSession|DbTransaction|TransactionScope|BeginTransaction|ExecuteNonQueryAsync|ExecuteAsync)\b")
                || Regex.IsMatch(pair.Value, @"\busing\s+(?:static\s+[^;]*\b" + Regex.Escape(typeName) + @"\b|\w+\s*=)")))
            {
                errors.Add($"Query consumers must stay in transaction-free module Queries: {identity}");
            }

            if (errors.Count == before)
            {
                allowed.Add(new(entry.File, sql.Span));
            }
        }

        return new(allowed.ToArray(), errors.ToArray());
    }

    /// <summary>历史债务也不能将跨模块写语句伪装成可接受的只读访问。</summary>
    internal static string[] FindForbiddenSql(string sourceModule, string file, string source) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(literal => literal.Token.ValueText)
            .Where(sql => Regex.Matches(sql, @"\bfn_[a-z0-9]+_[a-z0-9_]+\b", RegexOptions.IgnoreCase)
                .Any(match => !ModuleTableOwnershipTests.OwnsTable(sourceModule, match.Value)))
            .Where(sql => Regex.IsMatch(sql, @"\b(SELECT|WITH|INSERT|UPDATE|DELETE|MERGE|REPLACE|TRUNCATE|CREATE|ALTER|DROP|CALL|EXEC)\b", RegexOptions.IgnoreCase)
                && ReadOnlySql(sql) is null)
            .Select(_ => $"Unsupported or mutating cross-module SQL cannot use debt: {file}")
            .ToArray();

    /// <summary>受限词法检查只接受静态 SELECT；不支持的语法失败关闭，不能作为 SQL 权限证明。</summary>
    internal static string? ReadOnlySql(string sql)
    {
        // MySQL 可执行注释不能当普通注释剥离；反斜杠转义在双库中存在语义差异。
        if (sql.Contains("/*!", StringComparison.Ordinal) || sql.Contains("/*M!", StringComparison.OrdinalIgnoreCase)
            || sql.Contains('\\') || sql.Contains('"') || sql.Contains('#'))
        {
            return null;
        }

        var result = new StringBuilder();
        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                var closed = false;
                while (++i < sql.Length)
                {
                    if (sql[i] != '\'') { continue; }
                    if (i + 1 < sql.Length && sql[i + 1] == '\'') { i++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) { return null; }
                result.Append(" '' ");
            }
            else if (i + 1 < sql.Length && sql[i] == '-' && sql[i + 1] == '-')
            {
                // SQL Server 与 MySQL 对 -- 的起始条件不同，只接受两者都视为注释的形式。
                if (i + 2 < sql.Length && !char.IsWhiteSpace(sql[i + 2])) { return null; }
                while (i < sql.Length && sql[i] is not ('\r' or '\n')) { i++; }
                result.Append(' ');
            }
            else if (i + 1 < sql.Length && sql[i] == '/' && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0 || sql.AsSpan(i + 2, end - i - 2).Contains("/*", StringComparison.Ordinal)) { return null; }
                i = end + 1;
                result.Append(' ');
            }
            else if (sql[i] is '[' or '`')
            {
                var end = sql.IndexOf(sql[i] == '[' ? ']' : '`', i + 1);
                if (end < 0 || !Regex.IsMatch(sql[(i + 1)..end], @"^[A-Za-z_][A-Za-z0-9_]*$")) { return null; }
                result.Append(sql[(i + 1)..end]);
                i = end;
            }
            else { result.Append(sql[i]); }
        }

        var tokens = result.ToString().Trim().TrimEnd(';').TrimEnd();
        // 分页 NEXT 与纯字符串 REPLACE 是 SELECT 的只读语法，不能与序列取号或 REPLACE 写入混同。
        var reviewed = Regex.Replace(tokens, @"\bFETCH\s+NEXT\s+@[A-Za-z_]\w*\s+ROWS\s+ONLY\b", "FETCH_PAGE", RegexOptions.IgnoreCase);
        reviewed = Regex.Replace(reviewed, @"\bREPLACE\s*\(", "STRING_REPLACE(", RegexOptions.IgnoreCase);
        // 仅开放以参数为输入、固定路径的内建 JSON 行集；仍拒绝任意表值函数与跨库对象。
        reviewed = Regex.Replace(reviewed,
            @"\b(FROM|JOIN)\s+(?:OPENJSON(?=\s*\(\s*@[A-Za-z_]\w*\s*\))|JSON_TABLE(?=\s*\(\s*@[A-Za-z_]\w*\s*,\s*''\s+COLUMNS\s*\())",
            "$1 fn_parameter_rows", RegexOptions.IgnoreCase);
        if (!Regex.IsMatch(tokens, @"^SELECT\b", RegexOptions.IgnoreCase)
            || tokens.Contains(';') || tokens.Contains(":=", StringComparison.Ordinal)
            || Regex.IsMatch(reviewed, @"\b(INSERT|UPDATE|DELETE|MERGE|REPLACE|UPSERT|INTO|CREATE|ALTER|DROP|TRUNCATE|EXEC|EXECUTE|CALL|GRANT|REVOKE|BEGIN|COMMIT|ROLLBACK|TRANSACTION|LOCK|UPDLOCK|XLOCK|HOLDLOCK|TABLOCK|TABLOCKX|OPENROWSET|OPENQUERY|OPENDATASOURCE|NEXT|GET_LOCK|RELEASE_LOCK|SLEEP|BENCHMARK|LOAD_FILE)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(reviewed, @"\bSELECT\s+@|\b(FROM|JOIN)\s+(?!fn_\w+\b)[a-z_]|\b(FROM|JOIN)\s+fn_\w+\s*\.|\b[a-z_]\w*\s*\.\s*[a-z_]\w*\s*\(", RegexOptions.IgnoreCase))
        {
            return null;
        }

        return tokens;
    }

    private static bool HasDependency(string source, string target, IReadOnlyDictionary<string, string[]> dependencies)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source };
        var pending = new Queue<string>();
        pending.Enqueue(source);
        while (pending.TryDequeue(out var current))
        {
            if (!dependencies.TryGetValue(current, out var next)) { continue; }
            foreach (var dependency in next)
            {
                if (string.Equals(dependency, target, StringComparison.OrdinalIgnoreCase)) { return true; }
                if (visited.Add(dependency)) { pending.Enqueue(dependency); }
            }
        }
        return false;
    }
}

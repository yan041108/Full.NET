using System.Reflection;
using System.Text.Json;
using Full.NET.Data.Abstractions;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class GlobalSqlStatementCatalogTests
{
    private static readonly Assembly[] SqlStatementAssemblies =
    [
        .. ProductionAssemblies.All,
        typeof(Full.NET.Modules.Jobs.JobsModule).Assembly,
        typeof(Full.NET.Modules.Messaging.MessagingModule).Assembly,
        typeof(Full.NET.Modules.Notifications.NotificationsModule).Assembly,
    ];

    private static readonly HashSet<string> AllowedCategories =
    [
        "cross_context_audit_write",
        "reliable_event_sink",
        "host_catalog",
        "verified_identity",
        "tenant_resolution",
        "host_tenant_catalog",
        "explicit_tenant_anchor",
    ];

    private static readonly string[] AllowedRuntimeCloneMethods =
    [
        "Full.NET.Modules.Organization.Persistence.TenantScopedSqlComposer.ApplyDataScopeFilter",
        "Full.NET.Modules.Auditing.Persistence.AuditingSqlServerPageStatementBuilder.CreateVariants",
        "Full.NET.Modules.Auditing.Persistence.AuditingSqlServerPageStatementBuilder.CreateListVariants",
        "Full.NET.Modules.Auditing.Features.WriteAuditBatch.AuditWriteBatchSql.BuildOperations",
        "Full.NET.Modules.Auditing.Features.WriteAuditBatch.AuditWriteBatchSql.BuildExceptions",
        "Full.NET.Modules.Auditing.Features.WriteAuditBatch.AuditWriteBatchSql.BuildOutbounds",
        "Full.NET.Modules.Identity.Persistence.IdentitySql.BuildProjectedHostUserProfilesByIds",
        "Full.NET.Modules.SerialNumbers.Persistence.SerialNumberSql.CreatePageRulesMySql",
        "Full.NET.Modules.SerialNumbers.Persistence.SerialNumberSql.CreatePageRulesSqlServer",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [TestMethod]
    public void Production_global_sql_statements_are_exactly_cataloged()
    {
        var root = FindRepositoryRoot();
        var declarations = ReadProductionGlobalStatements(root);
        var catalogPath = Path.Combine(
            root,
            "contracts",
            "architecture",
            "global-sql-statements.json");
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(
            File.ReadAllText(catalogPath),
            JsonOptions);

        Assert.IsNotNull(document);
        Assert.AreEqual(1, document.SchemaVersion, "Unsupported Global SQL catalog schema.");

        var violations = Analyze(declarations, document.Entries ?? [])
            .Concat(SqlStatementConstructionScanner.FindViolations(
                SqlStatementAssemblies,
                AllowedRuntimeCloneMethods))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Global_sql_catalog_analyzer_rejects_unregistered_stale_wildcard_duplicate_and_drifted_entries()
    {
        var declaration = new SqlStatementDeclaration(
            "Full.NET.Modules.Alpha.Persistence.AlphaSql.Safe",
            "src/Modules/Full.NET.Modules.Alpha/Persistence/AlphaSql.cs",
            new SqlStatement(
                "alpha.safe",
                "SELECT Id FROM fn_alpha_widget WHERE TenantId IS NULL",
                SqlDataScope.Global));
        var entry = new GlobalSqlCatalogEntry(
            declaration.Statement.Name,
            declaration.Declaration,
            declaration.File,
            "host_catalog",
            "该语句只读取 Host 行。",
            ["TenantId IS NULL"]);

        Assert.HasCount(0, Analyze([declaration], [entry]));
        Assert.IsTrue(
            Analyze([declaration], [])
                .Any(value => value.Contains("Unregistered global statement", StringComparison.Ordinal)));
        Assert.IsTrue(
            Analyze([], [entry])
                .Any(value => value.Contains("Stale global statement catalog entry", StringComparison.Ordinal)));
        Assert.IsTrue(
            Analyze([declaration], [entry with { File = "**/AlphaSql.cs" }])
                .Any(value => value.Contains("Wildcard is not allowed", StringComparison.Ordinal)));
        Assert.IsTrue(
            Analyze([declaration], [entry, entry])
                .Any(value => value.Contains("Duplicate global statement catalog entry", StringComparison.Ordinal)));
        Assert.IsTrue(
            Analyze([declaration], [entry with { RequiredSqlFragments = ["TenantId = @TenantId"] }])
                .Any(value => value.Contains("Required SQL fragment", StringComparison.Ordinal)));
        Assert.IsTrue(
            Analyze(
                    [declaration],
                    [entry with { Category = "unreviewed", Reason = " " }])
                .Any(value => value.Contains("Unknown global statement category", StringComparison.Ordinal)));
        Assert.IsTrue(
            Analyze(
                    [declaration],
                    [entry with { Category = "unreviewed", Reason = " " }])
                .Any(value => value.Contains("Security reason is required", StringComparison.Ordinal)));

        var invalidBinding = declaration with
        {
            Statement = declaration.Statement with
            {
                TenantBinding = SqlTenantBinding.CurrentTenantId,
            },
        };
        Assert.IsTrue(
            Analyze([invalidBinding], [entry])
                .Any(value => value.Contains("must use tenant binding None", StringComparison.Ordinal)));
        Assert.IsTrue(
            SqlStatementConstructionScanner
                .FindViolations([typeof(InlineSqlStatementConstructionFixture)])
                .Any(value => value.Contains(
                    nameof(InlineSqlStatementConstructionFixture),
                    StringComparison.Ordinal)));
        Assert.HasCount(
            0,
            SqlStatementConstructionScanner.FindViolations(
                [typeof(StaticSqlStatementDeclarationFixture)]));
        Assert.HasCount(
            0,
            SqlStatementConstructionScanner.FindViolations(
                [typeof(SafeSqlStatementCloneFixture)],
                [$"{typeof(SafeSqlStatementCloneFixture).FullName}.Clone"]));
        Assert.IsTrue(
            SqlStatementConstructionScanner
                .FindViolations(
                    [typeof(ScopeMutatingSqlStatementCloneFixture)],
                    [$"{typeof(ScopeMutatingSqlStatementCloneFixture).FullName}.Clone"])
                .Any(value => value.Contains(
                    nameof(ScopeMutatingSqlStatementCloneFixture),
                    StringComparison.Ordinal)));
    }

    private static string[] Analyze(
        IReadOnlyCollection<SqlStatementDeclaration> declarations,
        IReadOnlyCollection<GlobalSqlCatalogEntry> entries)
    {
        var violations = new List<string>();
        var acceptedEntries = new List<GlobalSqlCatalogEntry>();
        var catalogKeys = new HashSet<string>(StringComparer.Ordinal);
        var statementNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.StatementName)
                || string.IsNullOrWhiteSpace(entry.Declaration)
                || string.IsNullOrWhiteSpace(entry.File))
            {
                violations.Add($"Exact identity fields are required: {Format(entry)}");
                continue;
            }

            if (ContainsWildcard(entry.StatementName)
                || ContainsWildcard(entry.Declaration)
                || ContainsWildcard(entry.File))
            {
                violations.Add($"Wildcard is not allowed: {Format(entry)}");
                continue;
            }

            var key = Key(entry.StatementName, entry.Declaration, entry.File);
            if (!catalogKeys.Add(key))
            {
                violations.Add($"Duplicate global statement catalog entry: {Format(entry)}");
                continue;
            }

            if (!statementNames.Add(entry.StatementName))
            {
                violations.Add($"Duplicate global statement name: {entry.StatementName}");
            }

            if (string.IsNullOrWhiteSpace(entry.Category)
                || !AllowedCategories.Contains(entry.Category))
            {
                violations.Add($"Unknown global statement category: {Format(entry)}");
            }

            if (string.IsNullOrWhiteSpace(entry.Reason))
            {
                violations.Add($"Security reason is required: {Format(entry)}");
            }

            if (entry.RequiredSqlFragments is not { Length: > 0 }
                || entry.RequiredSqlFragments.Any(string.IsNullOrWhiteSpace))
            {
                violations.Add($"Required SQL fragments are missing: {Format(entry)}");
            }

            acceptedEntries.Add(entry);
        }

        var actualKeys = declarations
            .Select(item => Key(
                item.Statement.Name,
                item.Declaration,
                item.File))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var declaration in declarations)
        {
            var key = Key(
                declaration.Statement.Name,
                declaration.Declaration,
                declaration.File);
            var entry = acceptedEntries.FirstOrDefault(candidate =>
                string.Equals(
                    Key(candidate.StatementName!, candidate.Declaration!, candidate.File!),
                    key,
                    StringComparison.Ordinal));
            if (entry is null)
            {
                violations.Add($"Unregistered global statement: {Format(declaration)}");
                continue;
            }

            if (declaration.Statement.TenantBinding != SqlTenantBinding.None)
            {
                violations.Add(
                    $"Global statement must use tenant binding None: {Format(declaration)}");
            }

            foreach (var fragment in entry.RequiredSqlFragments ?? [])
            {
                if (!declaration.Statement.Text.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"Required SQL fragment '{fragment}' is absent: {Format(declaration)}");
                }
            }
        }

        foreach (var entry in acceptedEntries.Where(entry =>
                     !actualKeys.Contains(Key(
                         entry.StatementName!,
                         entry.Declaration!,
                         entry.File!))))
        {
            violations.Add($"Stale global statement catalog entry: {Format(entry)}");
        }

        return violations
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
    }

    private static SqlStatementDeclaration[] ReadProductionGlobalStatements(string root) =>
        SqlStatementAssemblies
            .Distinct()
            .SelectMany(GetLoadableTypes)
            .SelectMany(type => ReadSqlStatements(root, type))
            .Where(item => item.Statement.Scope == SqlDataScope.Global)
            .OrderBy(item => item.Statement.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Declaration, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<SqlStatementDeclaration> ReadSqlStatements(
        string root,
        Type type)
    {
        const BindingFlags Flags = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(Flags)
                     .Where(field => field.FieldType == typeof(SqlStatement)))
        {
            if (field.GetValue(null) is SqlStatement statement)
            {
                yield return new SqlStatementDeclaration(
                    $"{FormatDeclarationPrefix(type)}.{field.Name}",
                    ResolveSourceFile(root, type),
                    statement);
            }
        }

        foreach (var property in type.GetProperties(Flags)
                     .Where(property => property.PropertyType == typeof(SqlStatement)
                         && property.GetIndexParameters().Length == 0))
        {
            if (property.GetValue(null) is SqlStatement statement)
            {
                yield return new SqlStatementDeclaration(
                    $"{FormatDeclarationPrefix(type)}.{property.Name}",
                    ResolveSourceFile(root, type),
                    statement);
            }
        }
    }

    private static string FormatDeclarationPrefix(Type type) =>
        type.IsNested && type.DeclaringType is not null
            ? $"{type.DeclaringType.FullName}.{type.Name}"
            : type.FullName!;

    private static string ResolveSourceFile(string root, Type type)
    {
        if (type.IsNested && type.DeclaringType is not null)
        {
            return ResolveSourceFile(root, type.DeclaringType);
        }

        var expectedFileName = $"{type.Name}.cs";
        var candidates = Directory
            .EnumerateFiles(Path.Combine(root, "src"), expectedFileName, SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"Could not resolve the source file for {type.FullName}."),
            _ => throw new InvalidOperationException(
                $"Source file for {type.FullName} is ambiguous: {string.Join(", ", candidates)}"),
        };
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaderMessages = exception.LoaderExceptions
                .Where(loaderException => loaderException is not null)
                .Select(loaderException => loaderException!.Message)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(message => message, StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"Could not load every type from {assembly.FullName}: "
                + string.Join(" | ", loaderMessages),
                exception);
        }
    }

    private static bool ContainsWildcard(string value) =>
        value.Contains('*', StringComparison.Ordinal)
        || value.Contains('?', StringComparison.Ordinal);

    private static bool IsBuildOutputPath(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
        || path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static string Key(string statementName, string declaration, string file) =>
        $"{statementName}|{declaration}|{file.Replace('\\', '/')}";

    private static string Format(SqlStatementDeclaration declaration) =>
        $"{declaration.Statement.Name} @ {declaration.Declaration} ({declaration.File})";

    private static string Format(GlobalSqlCatalogEntry entry) =>
        $"{entry.StatementName ?? "<missing>"} @ {entry.Declaration ?? "<missing>"}"
        + $" ({entry.File ?? "<missing>"})";

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

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed record GlobalSqlCatalogDocument(
        int SchemaVersion,
        GlobalSqlCatalogEntry[]? Entries);

    private sealed record GlobalSqlCatalogEntry(
        string? StatementName,
        string? Declaration,
        string? File,
        string? Category,
        string? Reason,
        string[]? RequiredSqlFragments);

    private sealed record SqlStatementDeclaration(
        string Declaration,
        string File,
        SqlStatement Statement);
}

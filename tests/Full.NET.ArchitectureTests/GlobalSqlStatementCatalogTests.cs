using System.Text.RegularExpressions;
using System.Reflection;
using System.Text.Json;
using Full.NET.Data.Abstractions;

namespace Full.NET.ArchitectureTests;

/// <summary>架构测试：全局 SQL 语句目录与模块内 SQL 登记的一致性门禁。</summary>
[TestClass]
public sealed class GlobalSqlStatementCatalogTests
{
    private static readonly Assembly[] SqlStatementAssemblies =
    [
        .. ProductionAssemblies.All,
        // 应用样例不进入官方发布闭包，其后台全局扫描仍必须接受同一精确目录守卫。
        typeof(Full.NET.Modules.EnterpriseRequest.EnterpriseRequestModule).Assembly,
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
        // 访问日志批量 INSERT 只展开固定列和参数名，不允许由日志内容改变 SQL 结构。
        "Full.NET.Modules.Auditing.Features.WriteAccessLogs.AccessLogBatchSql.Build",
        "Full.NET.Modules.Identity.Persistence.IdentitySql.BuildProjectedHostUserProfilesByIds",
        "Full.NET.Modules.SerialNumbers.Persistence.SerialNumberSql.CreatePageRulesMySql",
        "Full.NET.Modules.SerialNumbers.Persistence.SerialNumberSql.CreatePageRulesSqlServer",
        "Full.NET.Modules.Document.Persistence.DocumentShareSql.BuildPageMySql",
        "Full.NET.Modules.Document.Persistence.DocumentShareSql.BuildPageSqlServer",
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
    public void Workflow_collaboration_grant_global_sql_is_exactly_cataloged()
    {
        // 独立检查共享授权控制面；保留全局门禁，其他模块失败不能掩盖本次目录漂移。
        const string file = "src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowCollaborationGrantSql.cs";
        var root = FindRepositoryRoot();
        var declarations = ReadProductionGlobalStatements(root).Where(item => item.File == file).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(
            File.ReadAllText(Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions);
        Assert.IsNotNull(document);
        Assert.AreEqual(1, document.SchemaVersion);
        var entries = (document.Entries ?? []).Where(item => item.File == file).ToArray();
        Assert.HasCount(8, declarations);
        Assert.HasCount(8, entries);
        var violations = Analyze(declarations, entries).ToArray();
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Workflow_operations_global_sql_is_exactly_cataloged()
    {
        const string file = "src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowOperationsSql.cs";
        var root = FindRepositoryRoot();
        var declarations = ReadProductionGlobalStatements(root).Where(item => item.File == file).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(File.ReadAllText(Path.Combine(root,
            "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(item => item.File == file).ToArray();
        Assert.HasCount(8, declarations); Assert.HasCount(8, entries);
        var violations = Analyze(declarations, entries).ToArray();
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Workflow_collaboration_request_global_sql_is_exactly_cataloged()
    {
        const string file = "src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowCollaborationCallSql.cs";
        var root = FindRepositoryRoot();
        var declarations = ReadProductionGlobalStatements(root).Where(item => item.File == file).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(
            File.ReadAllText(Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(item => item.File == file).ToArray();
        Assert.HasCount(6, declarations); Assert.HasCount(6, entries);
        var violations = Analyze(declarations, entries).ToArray();
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Workflow_collaboration_target_global_sql_is_exactly_cataloged()
    {
        const string file = "src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowCollaborationRelaySql.cs";
        var root = FindRepositoryRoot();
        var type = typeof(global::Full.NET.Modules.Workflow.WorkflowModule).Assembly.GetType("Full.NET.Modules.Workflow.Persistence.WorkflowCollaborationRelaySql", throwOnError: true)!;
        var declarations = ReadSqlStatements(root, type).Where(item => item.Statement.Scope == SqlDataScope.Global).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(File.ReadAllText(Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(item => item.File == file).ToArray();
        Assert.HasCount(15, declarations); Assert.HasCount(15, entries);
        var violations = Analyze(declarations, entries).ToArray();
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Workflow_collaboration_control_global_sql_is_exactly_cataloged()
    {
        const string file = "src/Modules/Full.NET.Modules.Workflow/Persistence/WorkflowCollaborationControlSql.cs";
        var root = FindRepositoryRoot();
        var type = typeof(global::Full.NET.Modules.Workflow.WorkflowModule).Assembly.GetType("Full.NET.Modules.Workflow.Persistence.WorkflowCollaborationControlSql", throwOnError: true)!;
        var declarations = ReadSqlStatements(root, type).Where(item => item.Statement.Scope == SqlDataScope.Global).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(File.ReadAllText(Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(item => item.File == file).ToArray();
        Assert.HasCount(21, declarations); Assert.HasCount(21, entries);
        var violations = Analyze(declarations, entries).ToArray();
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    [DataRow("Workflow", "WorkflowCollaborationFileSql", 12)]
    [DataRow("Files", "WorkflowFileReadCapabilitySql", 8)]
    public void Workflow_file_capability_global_sql_is_exactly_cataloged(string module, string name, int count)
    {
        var root = FindRepositoryRoot();
        var assembly = module == "Workflow" ? typeof(global::Full.NET.Modules.Workflow.WorkflowModule).Assembly : typeof(global::Full.NET.Modules.Files.FilesModule).Assembly;
        var type = assembly.GetType($"Full.NET.Modules.{module}.Persistence.{name}", throwOnError: true)!;
        var file = $"src/Modules/Full.NET.Modules.{module}/Persistence/{name}.cs";
        var declarations = ReadSqlStatements(root, type).Where(item => item.Statement.Scope == SqlDataScope.Global).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(File.ReadAllText(Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(item => item.File == file).ToArray();
        Assert.HasCount(count, declarations); Assert.HasCount(count, entries);
        var violations = Analyze(declarations, entries).ToArray();
        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    [DataRow("Workflow", "WorkflowCollaborationListSql", "", 3)]
    [DataRow("Workflow", "WorkflowSql", "filtered", 12)]
    [DataRow("Workflow", "WorkflowClaimSql", "mine", 4)]
    [DataRow("Workflow", "WorkflowRecoverySql", "for_scope", 4)]
    [DataRow("Tenancy", "TenantSql", "find_active_by_explicit_ids", 1)]
    public void Collaboration_pagination_and_tenant_batch_sql_is_exactly_cataloged(string module, string name, string part, int count)
    {
        var root = FindRepositoryRoot();
        var assembly = module == "Workflow" ? typeof(global::Full.NET.Modules.Workflow.WorkflowModule).Assembly : typeof(global::Full.NET.Modules.Tenancy.TenancyModule).Assembly;
        var type = assembly.GetType($"Full.NET.Modules.{module}.Persistence.{name}", throwOnError: true)!;
        var file = $"src/Modules/Full.NET.Modules.{module}/Persistence/{name}.cs";
        var declarations = ReadSqlStatements(root, type).Where(item => item.Statement.Scope == SqlDataScope.Global && item.Statement.Name.Contains(part, StringComparison.Ordinal)).ToArray();
        var names = declarations.Select(item => item.Declaration).ToHashSet(StringComparer.Ordinal);
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(File.ReadAllText(Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(item => item.File == file && names.Contains(item.Declaration!)).ToArray();
        Assert.HasCount(count, declarations); Assert.HasCount(count, entries);
        var violations = Analyze(declarations, entries).ToArray(); Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    [DataRow("system_audit", 1)][DataRow("has_durable_wait", 2)]
    public void Workflow_recovery_worker_sql_is_exactly_cataloged(string part, int count) =>
        Collaboration_pagination_and_tenant_batch_sql_is_exactly_cataloged("Workflow", "WorkflowRecoverySql", part, count);

    [TestMethod]
    public void Workflow_visible_attachment_names_sql_is_exactly_cataloged() =>
        Collaboration_pagination_and_tenant_batch_sql_is_exactly_cataloged("Workflow", "WorkflowCollaborationFileSql", "visible_names", 1);

    [TestMethod]
    public void Application_sample_global_scans_are_exactly_cataloged()
    {
        var root = FindRepositoryRoot();
        var assembly = typeof(Full.NET.Modules.EnterpriseRequest.EnterpriseRequestModule).Assembly;
        var declarations = GetLoadableTypes(assembly).SelectMany(type => ReadSqlStatements(root, type))
            .Where(item => item.Statement.Scope == SqlDataScope.Global).ToArray();
        var document = JsonSerializer.Deserialize<GlobalSqlCatalogDocument>(File.ReadAllText(
            Path.Combine(root, "contracts", "architecture", "global-sql-statements.json")), JsonOptions)!;
        var entries = document.Entries!.Where(entry => entry.File?.StartsWith("samples/enterprise-request/", StringComparison.Ordinal) == true).ToArray();
        var violations = Analyze(declarations, entries).Concat(SqlStatementConstructionScanner.FindViolations([assembly], [])).ToArray();
        Assert.HasCount(2, declarations);
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

    // 同一 SQL 类型的各字段共享来源；按仓库和类型缓存，避免每条声明重复遍历大型输出树。
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Root, Type Type), string> SourceFiles = new();

    /// <summary>解析真实类型声明所在文件，支持切片文件内的多个 SQL 类型并拒绝歧义。</summary>
    /// <param name="root">仓库根目录。</param>
    /// <param name="type">需要定位的声明类型。</param>
    private static string ResolveSourceFile(string root, Type type) =>
        SourceFiles.GetOrAdd((root, type), key => ResolveSourceFileCore(key.Root, key.Type));

    private static string ResolveSourceFileCore(string root, Type type)
    {
        if (type.IsNested && type.DeclaringType is not null)
        {
            return ResolveSourceFile(root, type.DeclaringType);
        }

        var expectedFileName = $"{type.Name}.cs";
        var sourceRoots = GetProductionSourceRoots(root);
        var candidates = sourceRoots
            .SelectMany(sourceRoot => Directory.EnumerateFiles(
                sourceRoot,
                expectedFileName,
                SearchOption.AllDirectories))
            .Where(path => !IsBuildOutputPath(path))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();

        if (candidates.Length == 0)
        {
            // 同一切片可把多个 SQL 类型放入同一源文件；按声明解析，不能假定文件名等于类型名。
            var namespacePattern = new Regex(@"(?m)^\s*namespace\s+" + Regex.Escape(type.Namespace!) + @"\s*[;{]");
            var declarationPattern = new Regex(@"(?m)^\s*(?:(?:internal|public|static|sealed|partial|abstract)\s+)*class\s+"
                + Regex.Escape(type.Name) + @"\b");
            candidates = sourceRoots
                .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
                .Where(path => !IsBuildOutputPath(path))
                .Where(path =>
                {
                    var source = File.ReadAllText(path);
                    return namespacePattern.IsMatch(source) && declarationPattern.IsMatch(source);
                })
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .ToArray();
        }

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new InvalidOperationException(
                $"Could not resolve the source file for {type.FullName}."),
            _ => throw new InvalidOperationException(
                $"Source file for {type.FullName} is ambiguous: {string.Join(", ", candidates)}"),
        };
    }

    private static string[] GetProductionSourceRoots(string root) =>
    [
        Path.Combine(root, "src"),
        Path.Combine(root, "samples"),
    ];

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
        // MTP可能切换到隔离输出；显式根仍由解决方案标记验证，不把输出路径当源码目录。
        foreach (var origin in new[] { Environment.GetEnvironmentVariable("FULLNET_TEST_REPOSITORY_ROOT"), AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (string.IsNullOrWhiteSpace(origin)) continue;
            var directory = new DirectoryInfo(origin);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx"))) return directory.FullName;
                directory = directory.Parent;
            }
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

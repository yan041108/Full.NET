using System.Text;
using System.Text.Json;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using Full.NET.Data.CodeGeneration.Schema;

namespace Full.NET.CodeGeneration.Cli;

/// <summary>
/// 为确定性 CRUD 生成器提供安全默认预览和显式写盘命令。
/// </summary>
internal static class CodeGenerationCli
{
    private const int SuccessExitCode = 0;
    private const int RuntimeFailureExitCode = 1;
    private const int ConflictExitCode = 2;
    private const int UsageExitCode = 64;

    private const string Usage =
        """
        Usage:
          fullnet-codegen --schema <json-file> --workspace <existing-directory> [--apply]
          fullnet-codegen import-database --provider <sqlserver|mysql>
            --connection-env <environment-variable>
            --owner-key <value> --module-key <value> --entity-key <value>
            --root-namespace <value> --clr-type <value>
            --api-resource <value> --permission-resource <value>
            (--data-scope <tenant|host|global> | --tenant-scoped <true|false>)
            --has-version <true|false>
            --workspace <existing-directory> [--apply]
          fullnet-codegen list-database-tables --provider <sqlserver|mysql>
            --connection-env <environment-variable>
          fullnet-codegen preview-database-batch --provider <sqlserver|mysql>
            --connection-env <environment-variable>
            --mapping <json-file>
            --workspace <existing-directory>
          fullnet-codegen apply-database-batch --provider <sqlserver|mysql>
            --connection-env <environment-variable>
            --mapping <json-file>
            --workspace <existing-directory>
          fullnet-codegen plan-module-integration
            --schema <json-file>
            --repository <existing-directory>
            --target <json-file>
          fullnet-codegen validate-module-integration
            --schema <json-file>
            --repository <existing-directory>
            --target <json-file>
          fullnet-codegen apply-module-integration
            --schema <json-file>
            --repository <existing-directory>
            --target <json-file>
          fullnet-codegen apply-module-entry-integration
            --schema <json-file>
            --repository <existing-directory>
            --target <json-file>
          fullnet-codegen apply-composition-integration
            --schema <json-file>
            --repository <existing-directory>
            --target <json-file>
          fullnet-codegen apply-client-route-integration
            --schema <json-file>
            --repository <existing-directory>
            --target <json-file>
        """;

    /// <summary>
    /// 解析命令行参数并执行对应的代码生成、数据库导入或模块接入命令；所有用户可见输出通过 <paramref name="output"/> 与 <paramref name="error"/> 写入，便于测试捕获。
    /// </summary>
    /// <remarks>
    /// 该方法是 CLI 唯一入口，承担三重安全语义：默认预览不写盘、<c>--apply</c> 显式触发写盘、工作区或候选编译冲突时以非零退出码失败关闭。
    /// </remarks>
    /// <param name="args">命令行参数；第一个非选项 token 决定子命令（import-database、list-database-tables 等）。</param>
    /// <param name="output">标准输出流，写入已执行的动作、计划项和成功路径信息。</param>
    /// <param name="error">标准错误流，写入使用错误、冲突诊断与编译失败信息；数据库驱动原始异常不会直接外泄。</param>
    /// <param name="cancellationToken">用于取消数据库连接、文件 IO 和编译进程的令牌。</param>
    /// <returns>
    /// 退出码：0 表示成功；1 表示未受控运行时失败；2 表示工作区冲突或候选编译失败（可重试或修正后重试）；64 表示参数使用错误。
    /// </returns>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            var options = Parse(args);
            if (options.ShowHelp)
            {
                await output.WriteLineAsync(Usage);
                return SuccessExitCode;
            }

            if (options.DatabaseCatalog is not null)
            {
                var tableNames = await DatabaseTableCatalogCommand.ListAsync(
                    options.DatabaseCatalog,
                    ReadConnectionString(
                        options.DatabaseCatalog.ConnectionEnvironmentVariable),
                    cancellationToken);
                foreach (var tableName in tableNames)
                {
                    await output.WriteLineAsync($"Table {tableName}");
                }

                return SuccessExitCode;
            }

            if (options.ModuleIntegration is not null)
            {
                var integrationSchema = await CrudSchemaDocument.LoadAsync(
                    options.ModuleIntegration.SchemaPath,
                    cancellationToken);
                var target = await ModuleIntegrationTargetDocument.LoadAsync(
                    options.ModuleIntegration.TargetPath,
                    cancellationToken);
                if (options.ModuleIntegration.Mode
                    == ModuleIntegrationCliMode.ApplyClientRoutes)
                {
                    var routeResult =
                        await ClientRouteIntegrationApplyCommand.ApplyAsync(
                            options.ModuleIntegration.RepositoryPath,
                            integrationSchema,
                            target,
                            cancellationToken);
                    foreach (var diagnostic in routeResult.Diagnostics)
                    {
                        await error.WriteLineAsync(diagnostic);
                    }

                    if (!routeResult.Applied)
                    {
                        return ConflictExitCode;
                    }

                    await output.WriteLineAsync(
                        $"{(routeResult.VueChanged ? "Update" : "Unchanged")} "
                        + target.VueRouterPath);
                    await output.WriteLineAsync(
                        $"{(routeResult.LayuiChanged ? "Update" : "Unchanged")} "
                        + target.LayuiRouterPath);
                    await output.WriteLineAsync(
                        "Validated ClientRouteStructure "
                        + target.ClientRoute!.RoutePath);
                    return SuccessExitCode;
                }

                if (options.ModuleIntegration.Mode
                    == ModuleIntegrationCliMode.ApplyComposition)
                {
                    var compositionResult =
                        await CompositionIntegrationApplyCommand.ApplyAsync(
                            options.ModuleIntegration.RepositoryPath,
                            integrationSchema,
                            target,
                            cancellationToken);
                    foreach (var diagnostic in
                             compositionResult.Diagnostics)
                    {
                        await error.WriteLineAsync(diagnostic);
                    }

                    if (compositionResult.Compilation is
                        { Succeeded: false } compositionFailure)
                    {
                        foreach (var diagnostic in
                                 compositionFailure.Diagnostics)
                        {
                            await error.WriteLineAsync(diagnostic);
                        }
                    }

                    if (!compositionResult.Applied)
                    {
                        return ConflictExitCode;
                    }

                    await output.WriteLineAsync(
                        $"{(compositionResult.ProjectChanged ? "Update" : "Unchanged")} "
                        + target.CompositionProjectPath);
                    await output.WriteLineAsync(
                        $"{(compositionResult.CatalogChanged ? "Update" : "Unchanged")} "
                        + target.CompositionCatalogPath);
                    if (compositionResult.Compilation is not null)
                    {
                        await output.WriteLineAsync(
                            "Validated CompositionCompilation "
                            + target.CompositionProjectPath);
                    }

                    return SuccessExitCode;
                }

                if (options.ModuleIntegration.Mode
                    == ModuleIntegrationCliMode.ApplyModuleEntry)
                {
                    var entryResult =
                        await ModuleEntryIntegrationApplyCommand.ApplyAsync(
                            options.ModuleIntegration.RepositoryPath,
                            integrationSchema,
                            target,
                            cancellationToken);
                    foreach (var diagnostic in entryResult.Diagnostics)
                    {
                        await error.WriteLineAsync(diagnostic);
                    }

                    if (entryResult.Compilation is
                        { Succeeded: false } entryCompilationFailure)
                    {
                        foreach (var diagnostic in
                                 entryCompilationFailure.Diagnostics)
                        {
                            await error.WriteLineAsync(diagnostic);
                        }
                    }

                    if (!entryResult.Applied)
                    {
                        return ConflictExitCode;
                    }

                    await output.WriteLineAsync(
                        $"{(entryResult.Changed ? "Update" : "Unchanged")} "
                        + target.ModuleEntryPointPath);
                    if (entryResult.Compilation is not null)
                    {
                        await output.WriteLineAsync(
                            "Validated ModuleCompilation "
                            + target.ModuleProjectPath);
                    }

                    return SuccessExitCode;
                }

                if (options.ModuleIntegration.Mode
                    == ModuleIntegrationCliMode.ApplyBackend)
                {
                    var applyResult =
                        await ModuleIntegrationBackendApplyCommand.ApplyAsync(
                            options.ModuleIntegration.RepositoryPath,
                            integrationSchema,
                            target,
                            cancellationToken);
                    foreach (var action in applyResult.Actions)
                    {
                        await output.WriteLineAsync(
                            $"{action.Kind} {action.RelativePath}");
                    }

                    if (applyResult.Compilation is
                        { Succeeded: false } compilationFailure)
                    {
                        foreach (var diagnostic in
                                 compilationFailure.Diagnostics)
                        {
                            await error.WriteLineAsync(diagnostic);
                        }
                    }

                    if (!applyResult.Applied)
                    {
                        return ConflictExitCode;
                    }

                    await output.WriteLineAsync(
                        "Validated ModuleCompilation "
                        + target.ModuleProjectPath);
                    return SuccessExitCode;
                }

                if (options.ModuleIntegration.Mode
                    == ModuleIntegrationCliMode.ValidateCompilation)
                {
                    var compilation =
                        await ModuleIntegrationCompilationCommand.ValidateAsync(
                            options.ModuleIntegration.RepositoryPath,
                            integrationSchema,
                            target,
                            cancellationToken);
                    if (compilation.Succeeded)
                    {
                        await output.WriteLineAsync(
                            "Validated ModuleCompilation "
                            + target.ModuleProjectPath);
                        return SuccessExitCode;
                    }

                    foreach (var diagnostic in compilation.Diagnostics)
                    {
                        await error.WriteLineAsync(diagnostic);
                    }

                    return ConflictExitCode;
                }

                var integrationPlan =
                    await ModuleIntegrationPlanCommand.PlanAsync(
                        options.ModuleIntegration.RepositoryPath,
                        integrationSchema,
                        target,
                        cancellationToken);
                foreach (var item in integrationPlan.Items)
                {
                    await output.WriteLineAsync(
                        $"{item.Status} {item.Area} "
                        + $"{item.RelativePath} {item.Instruction}");
                }

                return SuccessExitCode;
            }

            if (options.DatabaseBatch is not null)
            {
                var mappings = await DatabaseBatchMappingDocument.LoadAsync(
                    options.DatabaseBatch.MappingPath,
                    cancellationToken);
                var schemas = await DatabaseBatchImportCommand.ImportAsync(
                    options.DatabaseBatch,
                    mappings,
                    ReadConnectionString(
                        options.DatabaseBatch
                            .ConnectionEnvironmentVariable),
                    cancellationToken);

                GenerationWritePlan batchPlan;
                try
                {
                    batchPlan = options.Apply
                        ? await CrudGenerationWorkspace.ApplyAsync(
                            options.WorkspacePath!,
                            schemas,
                            cancellationToken)
                        : await CrudGenerationWorkspace.PlanAsync(
                            options.WorkspacePath!,
                            schemas,
                            cancellationToken);
                }
                catch (DecoderFallbackException)
                {
                    await error.WriteLineAsync(
                        "工作区冲突：生成工作区文本不是严格 UTF-8。");
                    return ConflictExitCode;
                }

                foreach (var action in batchPlan.Actions)
                {
                    await output.WriteLineAsync(
                        $"{action.Kind} {action.RelativePath}");
                }

                return batchPlan.CanApply
                    ? SuccessExitCode
                    : ConflictExitCode;
            }

            FullNetCrudSchema schema;
            if (options.DatabaseImport is null)
            {
                schema = await CrudSchemaDocument.LoadAsync(
                    options.SchemaPath!,
                    cancellationToken);
            }
            else
            {
                schema = await DatabaseCrudImportCommand.ImportAsync(
                    options.DatabaseImport,
                    ReadConnectionString(
                        options.DatabaseImport.ConnectionEnvironmentVariable),
                    cancellationToken);
            }

            GenerationWritePlan plan;
            try
            {
                plan = options.Apply
                    ? await CrudGenerationWorkspace.ApplyAsync(
                        options.WorkspacePath!,
                        schema,
                        cancellationToken)
                    : await CrudGenerationWorkspace.PlanAsync(
                        options.WorkspacePath!,
                        schema,
                        cancellationToken);
            }
            catch (DecoderFallbackException)
            {
                await error.WriteLineAsync(
                    "工作区冲突：生成工作区文本不是严格 UTF-8。");
                return ConflictExitCode;
            }

            foreach (var action in plan.Actions)
            {
                await output.WriteLineAsync(
                    $"{action.Kind} {action.RelativePath}");
            }

            return plan.CanApply
                ? SuccessExitCode
                : ConflictExitCode;
        }
        catch (CliUsageException exception)
        {
            await error.WriteLineAsync(exception.Message);
            await error.WriteLineAsync(Usage);
            return UsageExitCode;
        }
        catch (GenerationWorkspaceConflictException exception)
        {
            await error.WriteLineAsync(
                $"工作区冲突：{exception.Message}");
            return ConflictExitCode;
        }
        catch (FileNotFoundException)
        {
            await error.WriteLineAsync("CRUD Schema 文件不存在。");
            return UsageExitCode;
        }
        catch (DirectoryNotFoundException)
        {
            await error.WriteLineAsync(
                "CRUD Schema 文件或生成工作区目录不存在。");
            return UsageExitCode;
        }
        catch (Exception exception)
            when (exception is JsonException
                or DecoderFallbackException
                or ArgumentException)
        {
            await error.WriteLineAsync(
                $"CRUD Schema 输入无效：{exception.Message}");
            return UsageExitCode;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync(
                $"生成运行失败：{exception.GetType().Name}");
            return RuntimeFailureExitCode;
        }
    }

    private static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count > 0
            && string.Equals(
                args[0],
                "import-database",
                StringComparison.Ordinal))
        {
            return ParseDatabaseImport(args);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "list-database-tables",
                StringComparison.Ordinal))
        {
            return ParseDatabaseCatalog(args);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "preview-database-batch",
                StringComparison.Ordinal))
        {
            return ParseDatabaseBatch(args, apply: false);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "apply-database-batch",
                StringComparison.Ordinal))
        {
            return ParseDatabaseBatch(args, apply: true);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "plan-module-integration",
                StringComparison.Ordinal))
        {
            return ParseModuleIntegration(
                args,
                ModuleIntegrationCliMode.Plan);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "validate-module-integration",
                StringComparison.Ordinal))
        {
            return ParseModuleIntegration(
                args,
                ModuleIntegrationCliMode.ValidateCompilation);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "apply-module-integration",
                StringComparison.Ordinal))
        {
            return ParseModuleIntegration(
                args,
                ModuleIntegrationCliMode.ApplyBackend);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "apply-module-entry-integration",
                StringComparison.Ordinal))
        {
            return ParseModuleIntegration(
                args,
                ModuleIntegrationCliMode.ApplyModuleEntry);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "apply-composition-integration",
                StringComparison.Ordinal))
        {
            return ParseModuleIntegration(
                args,
                ModuleIntegrationCliMode.ApplyComposition);
        }

        if (args.Count > 0
            && string.Equals(
                args[0],
                "apply-client-route-integration",
                StringComparison.Ordinal))
        {
            return ParseModuleIntegration(
                args,
                ModuleIntegrationCliMode.ApplyClientRoutes);
        }

        return ParseSchema(args);
    }

    private static CliOptions ParseModuleIntegration(
        IReadOnlyList<string> args,
        ModuleIntegrationCliMode mode)
    {
        string? schemaPath = null;
        string? repositoryPath = null;
        string? targetPath = null;
        var showHelp = false;
        for (var index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--schema":
                    schemaPath = ReadValue(
                        args,
                        ref index,
                        "--schema",
                        schemaPath);
                    break;

                case "--repository":
                    repositoryPath = ReadValue(
                        args,
                        ref index,
                        "--repository",
                        repositoryPath);
                    break;

                case "--target":
                    targetPath = ReadValue(
                        args,
                        ref index,
                        "--target",
                        targetPath);
                    break;

                default:
                    throw new CliUsageException(
                        $"未知参数：{args[index]}");
            }
        }

        if (showHelp)
        {
            return new CliOptions(
                SchemaPath: null,
                WorkspacePath: null,
                Apply: false,
                ShowHelp: true,
                DatabaseImport: null);
        }

        if (schemaPath is null
            || repositoryPath is null
            || targetPath is null)
        {
            throw new CliUsageException(
                $"{args[0]} 的 Schema、仓库目录和接入目标均为必填。");
        }

        return new CliOptions(
            SchemaPath: null,
            WorkspacePath: null,
            Apply: false,
            ShowHelp: false,
            DatabaseImport: null,
            DatabaseCatalog: null,
            DatabaseBatch: null,
            ModuleIntegration: new ModuleIntegrationCliOptions(
                schemaPath,
                repositoryPath,
                targetPath,
                mode));
    }

    private static CliOptions ParseSchema(IReadOnlyList<string> args)
    {
        string? schemaPath = null;
        string? workspacePath = null;
        var apply = false;
        var showHelp = false;
        for (var index = 0; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--apply":
                    if (apply)
                    {
                        throw new CliUsageException(
                            "--apply 不得重复。");
                    }

                    apply = true;
                    break;

                case "--schema":
                    schemaPath = ReadValue(
                        args,
                        ref index,
                        "--schema",
                        schemaPath);
                    break;

                case "--workspace":
                    workspacePath = ReadValue(
                        args,
                        ref index,
                        "--workspace",
                        workspacePath);
                    break;

                default:
                    throw new CliUsageException(
                        $"未知参数：{args[index]}");
            }
        }

        if (showHelp)
        {
            return new CliOptions(
                SchemaPath: null,
                WorkspacePath: null,
                Apply: false,
                ShowHelp: true,
                DatabaseImport: null);
        }

        if (schemaPath is null || workspacePath is null)
        {
            throw new CliUsageException(
                "--schema 与 --workspace 均为必填参数。");
        }

        return new CliOptions(
            schemaPath,
            workspacePath,
            apply,
            ShowHelp: false,
            DatabaseImport: null);
    }

    private static CliOptions ParseDatabaseCatalog(
        IReadOnlyList<string> args)
    {
        string? provider = null;
        string? connectionEnvironmentVariable = null;
        var showHelp = false;
        for (var index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--provider":
                    provider = ReadValue(
                        args,
                        ref index,
                        "--provider",
                        provider);
                    break;

                case "--connection-env":
                    connectionEnvironmentVariable = ReadValue(
                        args,
                        ref index,
                        "--connection-env",
                        connectionEnvironmentVariable);
                    break;

                default:
                    throw new CliUsageException(
                        $"未知参数：{args[index]}");
            }
        }

        if (showHelp)
        {
            return new CliOptions(
                SchemaPath: null,
                WorkspacePath: null,
                Apply: false,
                ShowHelp: true,
                DatabaseImport: null);
        }

        if (provider is null || connectionEnvironmentVariable is null)
        {
            throw new CliUsageException(
                "list-database-tables 的 Provider 与连接环境变量均为必填。");
        }

        return new CliOptions(
            SchemaPath: null,
            WorkspacePath: null,
            Apply: false,
            ShowHelp: false,
            DatabaseImport: null,
            new DatabaseCatalogCliOptions(
                ParseProvider(provider),
                connectionEnvironmentVariable));
    }

    private static CliOptions ParseDatabaseImport(
        IReadOnlyList<string> args)
    {
        string? provider = null;
        string? connectionEnvironmentVariable = null;
        string? ownerKey = null;
        string? moduleKey = null;
        string? entityKey = null;
        string? rootNamespace = null;
        string? clrTypeName = null;
        string? apiResourceName = null;
        string? permissionResourceName = null;
        string? tenantScoped = null;
        string? dataScope = null;
        string? hasVersion = null;
        string? workspacePath = null;
        var apply = false;
        var showHelp = false;
        for (var index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--apply":
                    if (apply)
                    {
                        throw new CliUsageException(
                            "--apply 不得重复。");
                    }

                    apply = true;
                    break;

                case "--provider":
                    provider = ReadValue(
                        args,
                        ref index,
                        "--provider",
                        provider);
                    break;

                case "--connection-env":
                    connectionEnvironmentVariable = ReadValue(
                        args,
                        ref index,
                        "--connection-env",
                        connectionEnvironmentVariable);
                    break;

                case "--owner-key":
                    ownerKey = ReadValue(
                        args,
                        ref index,
                        "--owner-key",
                        ownerKey);
                    break;

                case "--module-key":
                    moduleKey = ReadValue(
                        args,
                        ref index,
                        "--module-key",
                        moduleKey);
                    break;

                case "--entity-key":
                    entityKey = ReadValue(
                        args,
                        ref index,
                        "--entity-key",
                        entityKey);
                    break;

                case "--root-namespace":
                    rootNamespace = ReadValue(
                        args,
                        ref index,
                        "--root-namespace",
                        rootNamespace);
                    break;

                case "--clr-type":
                    clrTypeName = ReadValue(
                        args,
                        ref index,
                        "--clr-type",
                        clrTypeName);
                    break;

                case "--api-resource":
                    apiResourceName = ReadValue(
                        args,
                        ref index,
                        "--api-resource",
                        apiResourceName);
                    break;

                case "--permission-resource":
                    permissionResourceName = ReadValue(
                        args,
                        ref index,
                        "--permission-resource",
                        permissionResourceName);
                    break;

                case "--tenant-scoped":
                    tenantScoped = ReadValue(
                        args,
                        ref index,
                        "--tenant-scoped",
                        tenantScoped);
                    break;

                case "--data-scope":
                    dataScope = ReadValue(
                        args,
                        ref index,
                        "--data-scope",
                        dataScope);
                    break;

                case "--has-version":
                    hasVersion = ReadValue(
                        args,
                        ref index,
                        "--has-version",
                        hasVersion);
                    break;

                case "--workspace":
                    workspacePath = ReadValue(
                        args,
                        ref index,
                        "--workspace",
                        workspacePath);
                    break;

                default:
                    throw new CliUsageException(
                        $"未知参数：{args[index]}");
            }
        }

        if (showHelp)
        {
            return new CliOptions(
                SchemaPath: null,
                WorkspacePath: null,
                Apply: false,
                ShowHelp: true,
                DatabaseImport: null);
        }

        if (provider is null
            || connectionEnvironmentVariable is null
            || ownerKey is null
            || moduleKey is null
            || entityKey is null
            || rootNamespace is null
            || clrTypeName is null
            || apiResourceName is null
            || permissionResourceName is null
            || (tenantScoped is null && dataScope is null)
            || hasVersion is null
            || workspacePath is null)
        {
            throw new CliUsageException(
                "import-database 的所有命名、语义、连接环境变量和工作区参数均为必填。");
        }

        if (tenantScoped is not null && dataScope is not null)
        {
            throw new CliUsageException(
                "--data-scope 与兼容参数 --tenant-scoped 只能提供一个作用域。");
        }

        var resolvedDataScope = dataScope is not null
            ? ParseDataScope(dataScope)
            : ParseBoolean("--tenant-scoped", tenantScoped!)
                ? FullNetCrudDataScope.TenantRequired
                : FullNetCrudDataScope.Unspecified;
        var databaseImport = new DatabaseImportCliOptions(
            ParseProvider(provider),
            connectionEnvironmentVariable,
            ownerKey,
            moduleKey,
            entityKey,
            rootNamespace,
            clrTypeName,
            apiResourceName,
            permissionResourceName,
            resolvedDataScope,
            ParseBoolean("--has-version", hasVersion));
        return new CliOptions(
            SchemaPath: null,
            workspacePath,
            apply,
            ShowHelp: false,
            databaseImport);
    }

    private static CliOptions ParseDatabaseBatch(
        IReadOnlyList<string> args,
        bool apply)
    {
        string? provider = null;
        string? connectionEnvironmentVariable = null;
        string? mappingPath = null;
        string? workspacePath = null;
        var showHelp = false;
        for (var index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;

                case "--provider":
                    provider = ReadValue(
                        args,
                        ref index,
                        "--provider",
                        provider);
                    break;

                case "--connection-env":
                    connectionEnvironmentVariable = ReadValue(
                        args,
                        ref index,
                        "--connection-env",
                        connectionEnvironmentVariable);
                    break;

                case "--mapping":
                    mappingPath = ReadValue(
                        args,
                        ref index,
                        "--mapping",
                        mappingPath);
                    break;

                case "--workspace":
                    workspacePath = ReadValue(
                        args,
                        ref index,
                        "--workspace",
                        workspacePath);
                    break;

                default:
                    throw new CliUsageException(
                        $"未知参数：{args[index]}");
            }
        }

        if (showHelp)
        {
            return new CliOptions(
                SchemaPath: null,
                WorkspacePath: null,
                Apply: false,
                ShowHelp: true,
                DatabaseImport: null);
        }

        if (provider is null
            || connectionEnvironmentVariable is null
            || mappingPath is null
            || workspacePath is null)
        {
            throw new CliUsageException(
                $"{args[0]} 的 Provider、连接环境变量、映射文件和工作区参数均为必填。");
        }

        return new CliOptions(
            SchemaPath: null,
            workspacePath,
            Apply: apply,
            ShowHelp: false,
            DatabaseImport: null,
            DatabaseCatalog: null,
            DatabaseBatch: new DatabaseBatchCliOptions(
                ParseProvider(provider),
                connectionEnvironmentVariable,
                mappingPath));
    }

    private static DatabaseMetadataProvider ParseProvider(string value) =>
        value switch
        {
            "sqlserver" => DatabaseMetadataProvider.SqlServer,
            "mysql" => DatabaseMetadataProvider.MySql,
            _ => throw new CliUsageException(
                "--provider 只接受 sqlserver 或 mysql。"),
        };

    private static FullNetCrudDataScope ParseDataScope(string value) =>
        value switch
        {
            "tenant" => FullNetCrudDataScope.TenantRequired,
            "host" => FullNetCrudDataScope.HostOnly,
            "global" => FullNetCrudDataScope.Global,
            _ => throw new CliUsageException(
                "--data-scope 只接受 tenant、host 或 global。"),
        };

    private static bool ParseBoolean(
        string optionName,
        string value) =>
        value switch
        {
            "true" => true,
            "false" => false,
            _ => throw new CliUsageException(
                $"{optionName} 只接受 true 或 false。"),
        };

    private static string ReadConnectionString(
        string environmentVariable)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            environmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new CliUsageException(
                "--connection-env 指向的环境变量不存在或为空。");
        }

        return connectionString;
    }

    private static string ReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string optionName,
        string? existingValue)
    {
        if (existingValue is not null)
        {
            throw new CliUsageException(
                $"{optionName} 不得重复。");
        }

        if (index + 1 >= args.Count
            || args[index + 1].StartsWith(
                "--",
                StringComparison.Ordinal))
        {
            throw new CliUsageException(
                $"{optionName} 缺少参数值。");
        }

        index++;
        return args[index];
    }

    private sealed record CliOptions(
        string? SchemaPath,
        string? WorkspacePath,
        bool Apply,
        bool ShowHelp,
        DatabaseImportCliOptions? DatabaseImport,
        DatabaseCatalogCliOptions? DatabaseCatalog = null,
        DatabaseBatchCliOptions? DatabaseBatch = null,
        ModuleIntegrationCliOptions? ModuleIntegration = null);

    private sealed class CliUsageException(string message)
        : Exception(message);
}

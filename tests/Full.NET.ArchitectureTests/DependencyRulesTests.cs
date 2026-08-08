using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Settings;
using Full.NET.Modules.SerialNumbers;
using Full.NET.Modules.Auditing;
using Full.NET.Modules.Files;
using Full.NET.Modules.Document;
using Full.NET.Modules.Tenancy;
using NetArchTest.Rules;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class DependencyRulesTests
{
    private static readonly ReverseContractDependencyDebt[] AllowedReverseContractDependencies = [];

    private static readonly string[] ForbiddenRuntimeDynamicTokens =
    [
        "CSharpCompilation",
        "CSharpSyntaxTree",
        "CSharpScript",
        "Microsoft.CodeAnalysis.CSharp.Scripting",
        "ApplicationPartManager",
        "ApplicationParts.Add",
        "Assembly.LoadFrom",
        "Assembly.LoadFile",
    ];

    private static readonly Assembly[] BuildingBlockAssemblies =
    [
        typeof(Full.NET.Abstractions.Results.Result<>).Assembly,
        typeof(Full.NET.Caching.Fusion.CacheOptions).Assembly,
        typeof(Full.NET.Data.Abstractions.SqlStatement).Assembly,
        typeof(Full.NET.Data.CodeGeneration.Naming.NamingProfile).Assembly,
        typeof(Full.NET.Data.Dapper.ServiceCollectionExtensions).Assembly,
        typeof(Full.NET.Data.MySql.MySqlConnectionStringPolicy).Assembly,
        typeof(Full.NET.Hosting.Api.IApiResultMapper).Assembly,
        typeof(Full.NET.Localization.ILocaleNormalizer).Assembly,
        typeof(Full.NET.Migrations.DbUp.IDatabaseMigrationRunner).Assembly,
        typeof(Full.NET.Modularity.Modules.IFullNetModule).Assembly,
        typeof(Full.NET.Realtime.IRealtimePublisher).Assembly,
        typeof(Full.NET.Realtime.SignalR.RealtimeOptions).Assembly,
        typeof(Full.NET.Serialization.MessagePack.MessagePackIntegrationEventSerializer).Assembly,
        typeof(Full.NET.Seeding.Abstractions.SeedProfile).Assembly,
        typeof(Full.NET.Seeding.Dapper.SeedCommandLine).Assembly,
        typeof(Full.NET.Validation.FluentValidation.ServiceCollectionExtensions).Assembly,
    ];

    // 业务模块的全部程序集：Core（含跨模块契约）与 .Http 承载面都必须遵守数据与基础设施边界。
    private static readonly Assembly[] BusinessModuleAssemblies =
    [
        typeof(IdentityModule).Assembly,
        typeof(Full.NET.Modules.Identity.Contracts.VerifiedTenantContext).Assembly,
        typeof(TenancyModule).Assembly,
        typeof(Full.NET.Modules.Tenancy.Contracts.TenantSummary).Assembly,
        typeof(Full.NET.Modules.Organization.OrganizationModule).Assembly,
        typeof(Full.NET.Modules.Organization.Contracts.OrganizationErrorCodes).Assembly,
        typeof(SettingsModule).Assembly,
        typeof(Full.NET.Modules.Settings.Contracts.SettingsErrorCodes).Assembly,
        typeof(AuditingModule).Assembly,
        typeof(FilesModule).Assembly,
        typeof(Full.NET.Modules.Files.Contracts.FilesErrorCodes).Assembly,
        typeof(DocumentModule).Assembly,
        typeof(SerialNumbersModule).Assembly,
    ];

    [TestMethod]
    public void BuildingBlocks_DoNotDependOnModules()
    {
        var result = Types.InAssemblies(BuildingBlockAssemblies)
            .ShouldNot()
            .HaveDependencyOn("Full.NET.Modules")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"BuildingBlocks depending on Modules: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void Production_source_rejects_runtime_dynamic_csharp_and_application_part_mutation()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .Where(path => !IsApprovedCompatibilityPath(path))
            .Select(path => new
            {
                Path = Path.GetRelativePath(root, path).Replace('\\', '/'),
                Content = File.ReadAllText(path),
            })
            .Where(file => ForbiddenRuntimeDynamicTokens.Any(token =>
                file.Content.Contains(token, StringComparison.Ordinal)))
            .Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(
            0,
            offenders,
            "生产代码禁止运行时动态 C# 编译或 ApplicationPart 变更："
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Production_modules_do_not_reference_other_module_implementations_or_grant_friend_access()
    {
        var root = FindRepositoryRoot();
        var modulesRoot = Path.Combine(root, "src", "Modules");
        var offenders = Directory
            .EnumerateFiles(modulesRoot, "*.csproj", SearchOption.AllDirectories)
            .SelectMany(FindCrossModuleImplementationReferences)
            .Concat(Directory
                .EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsBuildOutputPath(path))
                .SelectMany(FindCrossModuleFriendAssemblies))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Production_module_contract_references_are_declared_dependencies()
    {
        Full.NET.Modularity.Modules.IFullNetModule[] modules =
        [
            new IdentityModule(),
            new AuditingModule(),
            new FilesModule(),
            new Full.NET.Modules.Notifications.NotificationsModule(),
            new Full.NET.Modules.Jobs.JobsModule(),
            new TenancyModule(),
            new Full.NET.Modules.Organization.OrganizationModule(),
            new SettingsModule(),
            new SerialNumbersModule(),
        ];
        var root = FindRepositoryRoot();
        var moduleByName = modules.ToDictionary(
            module => module.Name,
            StringComparer.Ordinal);
        var violations = modules
            .SelectMany(module =>
            {
                var projectPath = Path.Combine(
                    root,
                    "src",
                    "Modules",
                    $"Full.NET.Modules.{module.Name}",
                    $"Full.NET.Modules.{module.Name}.csproj");
                return XDocument.Load(projectPath)
                    .Descendants("ProjectReference")
                    .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                    .Select(GetProjectNameFromReference)
                    .Where(project => project.StartsWith(
                        "Full.NET.Modules.",
                        StringComparison.Ordinal)
                        && project.EndsWith(
                            ".Contracts",
                            StringComparison.Ordinal))
                    .Select(project => project[
                        "Full.NET.Modules.".Length
                        ..^".Contracts".Length])
                    .Where(dependency =>
                        !string.Equals(
                            dependency,
                            module.Name,
                            StringComparison.Ordinal)
                        && !module.Dependencies.Contains(
                            dependency,
                            StringComparer.Ordinal)
                        && !HasReverseModuleDependency(
                            module.Name,
                            dependency,
                            moduleByName))
                    .Select(dependency =>
                        $"{module.Name} references {dependency}.Contracts without declaring {dependency}");
            })
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void Reverse_module_dependency_does_not_implicitly_authorize_a_contract_cycle()
    {
        Full.NET.Modularity.Modules.IFullNetModule[] modules =
        [
            new TenancyModule(),
            new Full.NET.Modules.Organization.OrganizationModule(),
        ];
        var moduleByName = modules.ToDictionary(module => module.Name, StringComparer.Ordinal);

        Assert.IsFalse(HasReverseModuleDependency("Tenancy", "Organization", moduleByName));
        Assert.HasCount(0, AllowedReverseContractDependencies);
    }

    [TestMethod]
    public void Module_dependency_scanner_recognizes_negative_fixtures_and_allowed_boundaries()
    {
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-module-boundary-{Guid.NewGuid():N}");
        var projectDirectory = Path.Combine(fixtureRoot, "Full.NET.Modules.Alpha");
        var attributeDirectory = Path.Combine(projectDirectory, "Generated", "Attributes");
        Directory.CreateDirectory(attributeDirectory);
        var projectPath = Path.Combine(
            projectDirectory,
            "Full.NET.Modules.Alpha.csproj");
        var attributePath = Path.Combine(attributeDirectory, "Friends.cs");

        try
        {
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <ProjectReference Include="..\Full.NET.Modules.Alpha.Http\Full.NET.Modules.Alpha.Http.csproj" />
                    <ProjectReference Include="../Full.NET.Modules.Alpha.Worker/Full.NET.Modules.Alpha.Worker.csproj" />
                    <ProjectReference Include="..\Full.NET.Modules.Beta.Contracts\Full.NET.Modules.Beta.Contracts.csproj" />
                    <ProjectReference Include="../Full.NET.Modules.Gamma.Contracts/Full.NET.Modules.Gamma.Contracts.csproj" />
                    <ProjectReference Include="..\Full.NET.Modules.Beta\Full.NET.Modules.Beta.csproj" />
                    <ProjectReference Include="../Full.NET.Modules.Gamma/Full.NET.Modules.Gamma.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                attributePath,
                """
                [assembly: InternalsVisibleTo ( "Full.NET.Modules.Beta" )]
                [assembly: System.Runtime.CompilerServices.InternalsVisibleToAttribute("Full.NET.Modules.Gamma")]
                [assembly: global::System.Runtime.CompilerServices.InternalsVisibleTo ("Full.NET.Modules.Delta")]
                [assembly: InternalsVisibleTo("Full.NET.Modules.Alpha.Http")]
                """);

            CollectionAssert.AreEqual(
                new[]
                {
                    "Full.NET.Modules.Beta",
                    "Full.NET.Modules.Gamma",
                },
                new[]
                {
                    GetProjectNameFromReference(
                        @"..\Full.NET.Modules.Beta\Full.NET.Modules.Beta.csproj"),
                    GetProjectNameFromReference(
                        "../Full.NET.Modules.Gamma/Full.NET.Modules.Gamma.csproj"),
                });

            var offenders = FindCrossModuleImplementationReferences(projectPath)
                .Concat(FindCrossModuleFriendAssemblies(attributePath))
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    "Full.NET.Modules.Alpha -> Full.NET.Modules.Beta",
                    "Full.NET.Modules.Alpha -> Full.NET.Modules.Beta (InternalsVisibleTo)",
                    "Full.NET.Modules.Alpha -> Full.NET.Modules.Delta (InternalsVisibleTo)",
                    "Full.NET.Modules.Alpha -> Full.NET.Modules.Gamma",
                    "Full.NET.Modules.Alpha -> Full.NET.Modules.Gamma (InternalsVisibleTo)",
                },
                offenders);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Standalone_contract_projects_do_not_depend_on_aspnetcore()
    {
        // 仅独立 Contracts 项目必须保持 web-free；当模块没有真实消费者支撑单独 .Http 时，主项目可以直接承载 Web 面。
        var coreAssemblies = new[]
        {
            typeof(Full.NET.Modules.Identity.Contracts.VerifiedTenantContext).Assembly,
            typeof(Full.NET.Modules.Organization.Contracts.OrganizationErrorCodes).Assembly,
            typeof(Full.NET.Modules.Settings.Contracts.SettingsErrorCodes).Assembly,
        };

        var offenders = coreAssemblies
            .SelectMany(assembly => assembly
                .GetReferencedAssemblies()
                .Where(reference => reference.Name is not null
                    && reference.Name.StartsWith(
                        "Microsoft.AspNetCore",
                        StringComparison.Ordinal))
                .Select(reference => $"{assembly.GetName().Name} -> {reference.Name}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Tenancy_ExportsOnlyContractsAndModuleEntryPoint_FromSingleModuleAssembly()
    {
        var exportedTypes = typeof(TenancyModule).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(Full.NET.Modules.Tenancy.Contracts.AssignHostTenantPackageRequest).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.ChangeTenantContextRequest).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.CreateHostTenantPackageRequest).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.ITenantProvisioningService).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.ProvisionTenantRequest).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenancyErrorCodes).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenancyTenantManagementPermissions).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenancyTenantPackagePermissions).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenantChangedIntegrationEvent).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenantContextSummary).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenantPackageSummary).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenantProvisionedIntegrationEvent).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.TenantSummary).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.UpdateHostTenantPackageRequest).FullName,
                typeof(Full.NET.Modules.Tenancy.Contracts.UpdateHostTenantRequest).FullName,
                typeof(TenancyModule).FullName,
            },
            exportedTypes);
    }

    [TestMethod]
    public void Composition_uses_tenancy_core_project_instead_of_http_split_project()
    {
        var root = FindRepositoryRoot();
        var compositionProjectPath = Path.Combine(
            root,
            "src",
            "Composition",
            "Full.NET.Composition",
            "Full.NET.Composition.csproj");
        var projectReferences = XDocument.Load(compositionProjectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        CollectionAssert.Contains(
            projectReferences,
            @"..\..\Modules\Full.NET.Modules.Tenancy\Full.NET.Modules.Tenancy.csproj");
        CollectionAssert.DoesNotContain(
            projectReferences,
            @"..\..\Modules\Full.NET.Modules.Tenancy.Http\Full.NET.Modules.Tenancy.Http.csproj");
    }

    [TestMethod]
    public void Identity_DoesNotDependOnTenancyOrHosts()
    {
        var result = Types.InAssembly(typeof(IdentityModule).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Full.NET.Modules.Tenancy",
                "Full.NET.Host.Api",
                "Full.NET.Host.Migrator",
                "Full.NET.Host.Worker")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Identity dependency violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void BusinessModules_DoNotDependOnSignalRHubContext()
    {
        var result = Types.InAssemblies(BusinessModuleAssemblies)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.AspNetCore.SignalR",
                "Microsoft.AspNetCore.SignalR.Core")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Business modules must use IRealtimePublisher instead of SignalR: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void BusinessModules_DoNotDependOnConfluentKafka()
    {
        var result = Types.InAssemblies(BusinessModuleAssemblies)
            .ShouldNot()
            .HaveDependencyOn("Confluent.Kafka")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Business modules must not reference Confluent.Kafka: "
            + $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void BusinessModules_DoNotDependOnDapperOrAdoNetProviders()
    {
        var result = Types.InAssemblies(BusinessModuleAssemblies)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Dapper",
                "System.Data",
                "Microsoft.Data.SqlClient",
                "MySqlConnector")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"Business module data dependency violations: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void MySql_provider_has_only_approved_dependencies_and_consumers()
    {
        var root = FindRepositoryRoot();
        var providerProjectPath = Path.Combine(
            root,
            "src",
            "BuildingBlocks",
            "Full.NET.Data.MySql",
            "Full.NET.Data.MySql.csproj");
        Assert.IsTrue(File.Exists(providerProjectPath), "MySQL Provider 项目必须存在。");

        var providerProject = XDocument.Load(providerProjectPath);
        var dependencies = providerProject
            .Descendants()
            .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(
            new[]
            {
                "..\\Full.NET.Data.Abstractions\\Full.NET.Data.Abstractions.csproj",
                "MySqlConnector",
            },
            dependencies);

        var moduleProjectOffenders = Directory
            .EnumerateFiles(
                Path.Combine(root, "src", "Modules"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Where(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName is "ProjectReference" or "PackageReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(value => value?.Contains("Full.NET.Data.MySql", StringComparison.Ordinal) == true
                    || string.Equals(value, "MySqlConnector", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();
        Assert.HasCount(0, moduleProjectOffenders, string.Join(Environment.NewLine, moduleProjectOffenders));

        var approvedConsumers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("benchmarks", "Full.NET.Benchmarks", "Full.NET.Benchmarks.csproj"),
            Path.Combine("src", "BuildingBlocks", "Full.NET.Data.Dapper", "Full.NET.Data.Dapper.csproj"),
            Path.Combine("src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Full.NET.Migrations.DbUp.csproj"),
            Path.Combine("src", "BuildingBlocks", "Full.NET.Seeding.Dapper", "Full.NET.Seeding.Dapper.csproj"),
            Path.Combine("src", "Hosts", "Full.NET.Host.Migrator", "Full.NET.Host.Migrator.csproj"),
            Path.Combine("tests", "Full.NET.UnitTests", "Full.NET.UnitTests.csproj"),
            Path.Combine("tests", "Full.NET.IntegrationTests", "Full.NET.IntegrationTests.csproj"),
            Path.Combine("tests", "Full.NET.ArchitectureTests", "Full.NET.ArchitectureTests.csproj"),
        };
        var consumers = EnumerateRepositoryFiles(root, "*.csproj")
            .Where(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(value => value?.Contains("Full.NET.Data.MySql", StringComparison.Ordinal) == true))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var unapprovedConsumers = consumers
            .Where(path => !approvedConsumers.Contains(path))
            .ToArray();
        Assert.HasCount(0, unapprovedConsumers, string.Join(Environment.NewLine, unapprovedConsumers));
        CollectionAssert.Contains(
            consumers,
            Path.Combine("src", "BuildingBlocks", "Full.NET.Data.Dapper", "Full.NET.Data.Dapper.csproj"));
        CollectionAssert.Contains(
            consumers,
            Path.Combine("src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Full.NET.Migrations.DbUp.csproj"));
        CollectionAssert.Contains(
            consumers,
            Path.Combine("src", "BuildingBlocks", "Full.NET.Seeding.Dapper", "Full.NET.Seeding.Dapper.csproj"));

        AssertPolicyConsumer(
            root,
            Path.Combine("src", "BuildingBlocks", "Full.NET.Data.Dapper", "DbConnectionFactory.cs"),
            "allowUserVariables: false");
        AssertPolicyConsumer(
            root,
            Path.Combine("src", "BuildingBlocks", "Full.NET.Seeding.Dapper", "SeedDbConnectionFactory.cs"),
            "allowUserVariables: false");
        AssertPolicyConsumer(
            root,
            Path.Combine("src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "DbUpMigrationRunner.cs"),
            "allowUserVariables: true");
    }

    [TestMethod]
    public void Guid_storage_unsafe_conversions_exist_only_in_negative_fixtures()
    {
        var root = FindRepositoryRoot();
        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine("tests", "Full.NET.UnitTests", "Data", "MySqlConnectionStringPolicyTests.cs"),
            Path.Combine("tests", "Full.NET.IntegrationTests", "Data", "GuidBinaryRoundTripTests.cs"),
        };
        var sourceFiles = EnumerateRepositoryFiles(root, "*.*")
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(path => new GuidStorageSourceFile(
                Path.GetRelativePath(root, path),
                File.ReadAllText(path)))
            .ToArray();
        var sourceOffenders = GuidStorageArchitectureScanner
            .FindUnsafeSqlConversions(sourceFiles, allowedFiles);
        var compiledOffenders = GuidStorageArchitectureScanner
            .FindGuidToByteArrayCalls(ProductionAssemblies.All);
        var offenders = sourceOffenders
            .Concat(compiledOffenders)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Guid_storage_scanners_recognize_compiled_and_multiline_negative_fixtures()
    {
        var compiledOffenders = GuidStorageArchitectureScanner.FindGuidToByteArrayCalls(
            [typeof(GuidToByteArrayNegativeFixture).Assembly]);
        CollectionAssert.Contains(
            compiledOffenders,
            typeof(GuidToByteArrayNegativeFixture).FullName
                + ".Convert");

        var multilineSql = "SELECT "
            + "UUID_TO_BIN"
            + "(\n    @Id,\n    1\n)";
        var sourceOffenders = GuidStorageArchitectureScanner.FindUnsafeSqlConversions(
            [
                new GuidStorageSourceFile("negative-fixture.sql", multilineSql),
                new GuidStorageSourceFile(
                    "nested-negative-fixture.sql",
                    "SELECT " + "UUID_TO_BIN" + "(TRIM(Id), 1)"),
                new GuidStorageSourceFile(
                    "safe-fixture.sql",
                    "SELECT UUID_TO_BIN(Id, 0); INSERT INTO sample(Value) VALUES (1)"),
            ],
            []);

        CollectionAssert.Contains(sourceOffenders, "negative-fixture.sql");
        CollectionAssert.Contains(sourceOffenders, "nested-negative-fixture.sql");
        CollectionAssert.DoesNotContain(sourceOffenders, "safe-fixture.sql");
    }

    [TestMethod]
    public void Guid_round_trip_fixture_uses_uuid_storage_contract_as_single_source()
    {
        var root = FindRepositoryRoot();
        var contractPath = Path.Combine(
            root,
            "contracts",
            "database",
            "uuid-storage-v1.json");
        using var contract = JsonDocument.Parse(File.ReadAllText(contractPath));
        var vector = contract.RootElement
            .GetProperty("vectors")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("name").GetString(),
                "readable-boundaries",
                StringComparison.Ordinal));
        var guid = vector.GetProperty("uuid").GetString()
            ?? throw new InvalidDataException("UUID 契约向量缺少 uuid。");
        var hex = vector.GetProperty("hex").GetString()
            ?? throw new InvalidDataException("UUID 契约向量缺少 hex。");
        var testSource = File.ReadAllText(Path.Combine(
            root,
            "tests",
            "Full.NET.IntegrationTests",
            "Data",
            "GuidBinaryRoundTripTests.cs"));

        Assert.Contains("uuid-storage-v1.json", testSource, StringComparison.Ordinal);
        Assert.DoesNotContain(guid, testSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(hex, testSource, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Business_modules_do_not_depend_on_seeding_dapper()
    {
        var result = Types.InAssemblies(BusinessModuleAssemblies)
            .ShouldNot()
            .HaveDependencyOn("Full.NET.Seeding.Dapper")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            $"业务模块 Seed 基础设施依赖违规: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void Only_migrator_host_depends_on_seeding_dapper()
    {
        var runtimeHostResult = Types.InAssemblies(
                [ProductionAssemblies.HostApi, ProductionAssemblies.HostWorker])
            .ShouldNot()
            .HaveDependencyOn("Full.NET.Seeding.Dapper")
            .GetResult();
        var migratorReferencesSeeding = ProductionAssemblies.HostMigrator
            .GetReferencedAssemblies()
            .Any(reference => string.Equals(
                reference.Name,
                "Full.NET.Seeding.Dapper",
                StringComparison.Ordinal));

        Assert.IsTrue(
            runtimeHostResult.IsSuccessful,
            $"运行时 Host Seed 基础设施依赖违规: {string.Join(", ", runtimeHostResult.FailingTypeNames ?? [])}");
        Assert.IsTrue(migratorReferencesSeeding, "Migrator 必须显式引用 Seed Dapper 基础设施。");
    }

    [TestMethod]
    public void Migration_execution_is_owned_by_migrator_and_excluded_from_api_host()
    {
        var root = FindRepositoryRoot();
        const string migratorProject =
            "src/Hosts/Full.NET.Host.Migrator/Full.NET.Host.Migrator.csproj";
        const string benchmarkProject =
            "benchmarks/Full.NET.Benchmarks/Full.NET.Benchmarks.csproj";
        var migrationConsumers = FindDirectMigrationConsumers(root);
        var unapprovedProductionConsumers = migrationConsumers
            .Where(path => !path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(
                path,
                migratorProject,
                StringComparison.OrdinalIgnoreCase))
            // 基准工具在隔离容器中复用正式迁移，不能因此把迁移能力带入任何运行时 Host。
            .Where(path => !string.Equals(
                path,
                benchmarkProject,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.HasCount(
            0,
            unapprovedProductionConsumers,
            string.Join(Environment.NewLine, unapprovedProductionConsumers));
        CollectionAssert.Contains(migrationConsumers, migratorProject);

        var apiDependencyClosure = GetProjectDependencyClosure(
            root,
            "src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj");
        var apiMigrationDependencies = apiDependencyClosure
            .Where(path => string.Equals(
                GetProjectNameFromReference(path),
                "Full.NET.Migrations.DbUp",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.HasCount(
            0,
            apiMigrationDependencies,
            string.Join(Environment.NewLine, apiMigrationDependencies));

        var apiSourceOffenders = Directory
            .EnumerateFiles(
                Path.Combine(root, "src", "Hosts", "Full.NET.Host.Api"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("AddFullNetMigrations", StringComparison.Ordinal)
                    || source.Contains("IDatabaseMigrationRunner", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, apiSourceOffenders, string.Join(Environment.NewLine, apiSourceOffenders));
    }

    [TestMethod]
    public void Migration_project_reference_scanner_handles_both_separator_styles()
    {
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-migration-separators-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Migration"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Consumers"));

        try
        {
            WriteProject(
                Path.Combine(fixtureRoot, "Migration", "Full.NET.Migrations.DbUp.csproj"));
            WriteProject(
                Path.Combine(fixtureRoot, "Consumers", "Backslash.Consumer.csproj"),
                @"..\Migration\Full.NET.Migrations.DbUp.csproj");
            WriteProject(
                Path.Combine(fixtureRoot, "Consumers", "ForwardSlash.Consumer.csproj"),
                "../Migration/Full.NET.Migrations.DbUp.csproj");

            CollectionAssert.AreEqual(
                new[]
                {
                    "Consumers/Backslash.Consumer.csproj",
                    "Consumers/ForwardSlash.Consumer.csproj",
                },
                FindDirectMigrationConsumers(fixtureRoot));
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Api_project_dependency_closure_detects_transitive_migration_reference()
    {
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-migration-closure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Api"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Bridge"));
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Migration"));

        try
        {
            WriteProject(
                Path.Combine(fixtureRoot, "Api", "Full.NET.Host.Api.csproj"),
                "../Bridge/Full.NET.Migration.Bridge.csproj");
            WriteProject(
                Path.Combine(fixtureRoot, "Bridge", "Full.NET.Migration.Bridge.csproj"),
                @"..\Migration\Full.NET.Migrations.DbUp.csproj");
            WriteProject(
                Path.Combine(fixtureRoot, "Migration", "Full.NET.Migrations.DbUp.csproj"));

            CollectionAssert.Contains(
                GetProjectDependencyClosure(
                    fixtureRoot,
                    "Api/Full.NET.Host.Api.csproj"),
                "Migration/Full.NET.Migrations.DbUp.csproj");
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Repository_file_scans_exclude_nested_worktrees_and_build_outputs()
    {
        var fixtureRoot = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-repository-scan-{Guid.NewGuid():N}");
        var expectedProject = Path.Combine(
            fixtureRoot,
            "src",
            "Approved",
            "Approved.csproj");
        var excludedProjects = new[]
        {
            Path.Combine(
                fixtureRoot,
                ".worktrees",
                "feature",
                "src",
                "Duplicate.csproj"),
            Path.Combine(fixtureRoot, ".git", "internal", "Ignored.csproj"),
            Path.Combine(fixtureRoot, "src", "Approved", "bin", "Generated.csproj"),
            Path.Combine(fixtureRoot, "src", "Approved", "obj", "Generated.csproj"),
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(expectedProject)!);
            File.WriteAllText(expectedProject, "<Project />");
            foreach (var excludedProject in excludedProjects)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(excludedProject)!);
                File.WriteAllText(excludedProject, "<Project />");
            }

            var projects = EnumerateRepositoryFiles(fixtureRoot, "*.csproj")
                .Select(path => Path.GetRelativePath(fixtureRoot, path).Replace('\\', '/'))
                .ToArray();

            CollectionAssert.AreEqual(
                new[] { "src/Approved/Approved.csproj" },
                projects);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [TestMethod]
    public void RejectedDapperExtensions_AreNotReferencedByProjectsOrCentralVersions()
    {
        var rejectedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Dapper.Contrib",
            "Dapper.FastCrud",
            "Dapper.FluentMap",
            "Dapper.ProviderTools",
            "Dapper.Rainbow",
            "Dapper.SimpleCRUD",
            "Dapper.StrongName",
            "Dapper.Transaction",
            "Dommel",
            "MicroOrm.Dapper.Repositories",
            "Z.Dapper.Plus",
        };
        var root = FindRepositoryRoot();
        var offenders = EnumerateRepositoryFiles(root, "*.csproj")
            .Append(Path.Combine(root, "Directory.Packages.props"))
            .Where(File.Exists)
            .SelectMany(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName is "PackageReference" or "PackageVersion")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(package => package is not null && rejectedPackages.Contains(package))
                .Select(package => $"{Path.GetRelativePath(root, path)}: {package}"))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders, string.Join(Environment.NewLine, offenders));
    }

    [TestMethod]
    public void Tenancy_declares_identity_as_an_explicit_module_dependency()
    {
        var module = new TenancyModule();

        CollectionAssert.Contains(
            module.Dependencies.ToArray(),
            "Identity");
    }

    [TestMethod]
    public void Organization_declares_identity_and_tenancy_as_explicit_module_dependencies()
    {
        var module = new Full.NET.Modules.Organization.OrganizationModule();

        CollectionAssert.AreEquivalent(
            new[] { "Identity", "Tenancy" },
            module.Dependencies.ToArray());
    }

    [TestMethod]
    public void Settings_declares_identity_as_an_explicit_module_dependency()
    {
        var module = new SettingsModule();

        CollectionAssert.Contains(
            module.Dependencies.ToArray(),
            "Identity");
    }

    [TestMethod]
    public void SerialNumbers_declares_identity_as_an_explicit_module_dependency()
    {
        var module = new SerialNumbersModule();

        CollectionAssert.AreEquivalent(
            new[] { "Identity" },
            module.Dependencies.ToArray());
    }

    [TestMethod]
    public void Auditing_declares_identity_as_an_explicit_module_dependency()
    {
        var module = new AuditingModule();

        CollectionAssert.Contains(
            module.Dependencies.ToArray(),
            "Identity");
    }

    [TestMethod]
    public void Files_declares_identity_as_an_explicit_module_dependency()
    {
        var module = new FilesModule();

        CollectionAssert.Contains(
            module.Dependencies.ToArray(),
            "Identity");
    }

    [TestMethod]
    public void Document_declares_identity_and_files_as_explicit_module_dependencies()
    {
        var module = new DocumentModule();

        CollectionAssert.Contains(module.Dependencies.ToArray(), "Identity");
        CollectionAssert.Contains(module.Dependencies.ToArray(), "Files");
    }

    [TestMethod]
    public void ProductionTypes_DoNotExposeServiceLocatorMembers()
    {
        var allowedTypes = new HashSet<Type>
        {
            typeof(Full.NET.Modularity.Messaging.CommandDispatcher),
            typeof(Full.NET.Modularity.Messaging.QueryDispatcher),
        };
        var forbiddenNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "GetService",
            "GetRequiredService",
            "RootServices",
        };
        var offenders = ProductionAssemblies.All
            .SelectMany(GetLoadableTypes)
            .Where(type => !allowedTypes.Contains(type))
            .SelectMany(type => type
                .GetMembers(BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Where(member => member.MemberType is MemberTypes.Method or MemberTypes.Property)
                .Where(member => forbiddenNames.Contains(member.Name))
                .Select(member => $"{type.FullName}.{member.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.HasCount(0, offenders);
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

    private static IEnumerable<string> FindCrossModuleImplementationReferences(
        string projectPath)
    {
        var sourceProject = Path.GetFileNameWithoutExtension(projectPath);
        var sourceModule = GetLogicalModuleName(sourceProject);
        if (sourceModule is null)
        {
            yield break;
        }

        foreach (var reference in XDocument.Load(projectPath)
                     .Descendants()
                     .Where(element => element.Name.LocalName == "ProjectReference")
                     .Select(element => element.Attribute("Include")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Select(value => value!))
        {
            var targetProject = GetProjectNameFromReference(reference);
            var targetModule = GetLogicalModuleName(targetProject);
            if (targetModule is null
                || string.Equals(sourceModule, targetModule, StringComparison.Ordinal)
                || targetProject.EndsWith(".Contracts", StringComparison.Ordinal))
            {
                continue;
            }

            yield return $"{sourceProject} -> {targetProject}";
        }
    }

    private static bool HasReverseModuleDependency(
        string moduleName,
        string contractOwner,
        IReadOnlyDictionary<string, Full.NET.Modularity.Modules.IFullNetModule> moduleByName)
    {
        var reverseDependencyExists =
            moduleByName.TryGetValue(contractOwner, out var owner)
            && owner.Dependencies.Contains(moduleName, StringComparer.Ordinal);
        return reverseDependencyExists
            && AllowedReverseContractDependencies.Any(debt =>
                string.Equals(debt.ConsumerModule, moduleName, StringComparison.Ordinal)
                && string.Equals(debt.ContractOwnerModule, contractOwner, StringComparison.Ordinal));
    }

    private sealed record ReverseContractDependencyDebt(
        string ConsumerModule,
        string ContractOwnerModule,
        string Reason,
        string RemovalTask);

    private static string GetProjectNameFromReference(string reference)
    {
        var normalizedReference = reference.Trim().Replace('\\', '/');
        var fileName = normalizedReference[
            (normalizedReference.LastIndexOf('/') + 1)..];
        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static string[] FindDirectMigrationConsumers(string root)
    {
        return EnumerateRepositoryFiles(root, "*.csproj")
            .Where(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(reference => string.Equals(
                    reference is null ? null : GetProjectNameFromReference(reference),
                    "Full.NET.Migrations.DbUp",
                    StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetProjectDependencyClosure(
        string root,
        string entryProject)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var entryPath = ResolveProjectReference(normalizedRoot, entryProject);
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Push(entryPath);

        while (pending.TryPop(out var projectPath))
        {
            if (!visited.Add(projectPath))
            {
                continue;
            }

            foreach (var reference in XDocument.Load(projectPath)
                         .Descendants()
                         .Where(element => element.Name.LocalName == "ProjectReference")
                         .Select(element => element.Attribute("Include")?.Value)
                         .Where(reference => !string.IsNullOrWhiteSpace(reference)))
            {
                pending.Push(ResolveProjectReference(
                    Path.GetDirectoryName(projectPath)!,
                    reference!));
            }
        }

        return visited
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveProjectReference(string baseDirectory, string reference)
    {
        var platformPath = reference
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(baseDirectory, platformPath));
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(
        string root,
        string searchPattern)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.TryPop(out var directory))
        {
            foreach (var file in Directory
                         .EnumerateFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                yield return file;
            }

            foreach (var childDirectory in Directory
                         .EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly)
                         .Where(path => !IsExcludedRepositoryScanDirectory(path))
                         .OrderByDescending(path => path, StringComparer.Ordinal))
            {
                pendingDirectories.Push(childDirectory);
            }
        }
    }

    private static bool IsExcludedRepositoryScanDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || name.Equals(".worktrees", StringComparison.OrdinalIgnoreCase)
            || name.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || name.Equals("obj", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteProject(string path, params string[] projectReferences)
    {
        var references = string.Join(
            Environment.NewLine,
            projectReferences.Select(reference =>
                $"    <ProjectReference Include=\"{reference}\" />"));
        File.WriteAllText(
            path,
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
            {references}
              </ItemGroup>
            </Project>
            """);
    }

    private static IEnumerable<string> FindCrossModuleFriendAssemblies(
        string sourcePath)
    {
        var sourceProject = FindContainingModuleProject(sourcePath);
        var sourceModule = GetLogicalModuleName(sourceProject);
        if (sourceProject is null || sourceModule is null)
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(
                     File.ReadAllText(sourcePath),
                     "(?:global::)?(?:System\\.Runtime\\.CompilerServices\\.)?"
                     + "InternalsVisibleTo(?:Attribute)?\\s*\\(\\s*\\\""
                     + "(?<assembly>Full\\.NET\\.Modules\\.[^\\\",]+)",
                     RegexOptions.CultureInvariant))
        {
            var targetProject = match.Groups["assembly"].Value;
            var targetModule = GetLogicalModuleName(targetProject);
            if (targetModule is null
                || string.Equals(sourceModule, targetModule, StringComparison.Ordinal))
            {
                continue;
            }

            yield return $"{sourceProject} -> {targetProject} (InternalsVisibleTo)";
        }
    }

    private static string? FindContainingModuleProject(string sourcePath)
    {
        var directory = new FileInfo(sourcePath).Directory;
        while (directory is not null)
        {
            var projects = directory
                .EnumerateFiles("Full.NET.Modules.*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToArray();
            if (projects.Length > 0)
            {
                return Path.GetFileNameWithoutExtension(projects[0].Name);
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsBuildOutputPath(string path)
    {
        var buildOutputSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
        };
        return buildOutputSegments.Any(segment => path.Contains(
            segment,
            StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsApprovedCompatibilityPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/src/Compatibility/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetLogicalModuleName(string? projectName)
    {
        const string prefix = "Full.NET.Modules.";
        if (projectName is null
            || !projectName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var suffix = projectName[prefix.Length..];
        var separatorIndex = suffix.IndexOf('.');
        return separatorIndex < 0 ? suffix : suffix[..separatorIndex];
    }

    private static void AssertPolicyConsumer(
        string root,
        string relativePath,
        string expectedUserVariableSetting)
    {
        var content = File.ReadAllText(Path.Combine(root, relativePath));
        Assert.Contains(
            "MySqlConnectionStringPolicy.Create",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedUserVariableSetting,
            content,
            StringComparison.Ordinal);
    }

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

        throw new DirectoryNotFoundException(
            "Could not locate the Full.NET repository root.");
    }
}

internal static class ProductionAssemblies
{
    public static readonly Assembly DataDapper =
        typeof(Full.NET.Data.Dapper.ServiceCollectionExtensions).Assembly;

    public static readonly Assembly DataMySql =
        typeof(Full.NET.Data.MySql.MySqlConnectionStringPolicy).Assembly;

    public static readonly Assembly SeedingDapper =
        typeof(Full.NET.Seeding.Dapper.SeedCommandLine).Assembly;

    public static readonly Assembly HostApi = Assembly.Load("Full.NET.Host.Api");

    public static readonly Assembly HostMigrator = Assembly.Load("Full.NET.Host.Migrator");

    public static readonly Assembly HostWorker = Assembly.Load("Full.NET.Host.Worker");

    public static readonly Assembly[] All =
    [
        typeof(Full.NET.Abstractions.Results.Result<>).Assembly,
        typeof(Full.NET.Caching.Fusion.CacheOptions).Assembly,
        typeof(Full.NET.Data.Abstractions.SqlStatement).Assembly,
        typeof(Full.NET.Data.CodeGeneration.Naming.NamingProfile).Assembly,
        DataDapper,
        DataMySql,
        typeof(Full.NET.Hosting.Api.IApiResultMapper).Assembly,
        typeof(Full.NET.Localization.ILocaleNormalizer).Assembly,
        typeof(Full.NET.Migrations.DbUp.IDatabaseMigrationRunner).Assembly,
        typeof(Full.NET.Modularity.Modules.IFullNetModule).Assembly,
        typeof(Full.NET.Realtime.IRealtimePublisher).Assembly,
        typeof(Full.NET.Realtime.SignalR.RealtimeOptions).Assembly,
        typeof(Full.NET.Serialization.MessagePack.MessagePackIntegrationEventSerializer).Assembly,
        typeof(Full.NET.Seeding.Abstractions.SeedProfile).Assembly,
        SeedingDapper,
        typeof(Full.NET.Validation.FluentValidation.ServiceCollectionExtensions).Assembly,
        typeof(Full.NET.Compatibility.AdminNet.AdminNetApiResultMapper).Assembly,
        typeof(Full.NET.Composition.FullNetHostProfile).Assembly,
        typeof(IdentityModule).Assembly,
        typeof(Full.NET.Modules.Identity.Contracts.VerifiedTenantContext).Assembly,
        typeof(TenancyModule).Assembly,
        typeof(Full.NET.Modules.Tenancy.Contracts.TenantSummary).Assembly,
        typeof(Full.NET.Modules.Organization.OrganizationModule).Assembly,
        typeof(Full.NET.Modules.Organization.Contracts.OrganizationErrorCodes).Assembly,
        typeof(SettingsModule).Assembly,
        typeof(Full.NET.Modules.Settings.Contracts.SettingsErrorCodes).Assembly,
        typeof(AuditingModule).Assembly,
        typeof(FilesModule).Assembly,
        typeof(Full.NET.Modules.Files.Contracts.FilesErrorCodes).Assembly,
        typeof(DocumentModule).Assembly,
        typeof(SerialNumbersModule).Assembly,
        HostApi,
        HostMigrator,
        HostWorker,
    ];
}

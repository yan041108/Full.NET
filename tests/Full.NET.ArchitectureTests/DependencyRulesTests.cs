using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Full.NET.Modules.Identity;
using Full.NET.Modules.Tenancy;
using NetArchTest.Rules;

namespace Full.NET.ArchitectureTests;

[TestClass]
public sealed class DependencyRulesTests
{
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
    public void BusinessModuleCores_DoNotDependOnAspNetCore()
    {
        // Core（业务逻辑与跨模块契约）必须可脱离 ASP.NET Core 运行，Web 面只允许存在于 .Http 承载程序集。
        var coreAssemblies = new[]
        {
            typeof(Full.NET.Modules.Tenancy.Contracts.TenantSummary).Assembly,
            typeof(Full.NET.Modules.Identity.Contracts.VerifiedTenantContext).Assembly,
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
    public void Tenancy_ExportsOnlyContractsAndCompositionEntryPoints()
    {
        // Core 仅对外暴露跨模块契约命名空间；业务实现（Handler、Resolver、Options 等）保持 internal。
        var coreUnexpectedTypes = typeof(Full.NET.Modules.Tenancy.Contracts.TenantSummary).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace != "Full.NET.Modules.Tenancy.Contracts")
            .Select(type => type.FullName)
            .ToArray();

        Assert.HasCount(
            0,
            coreUnexpectedTypes,
            string.Join(Environment.NewLine, coreUnexpectedTypes));

        // Http 承载程序集仅暴露模块入口 TenancyModule；Endpoint 与中间件保持 internal。
        var httpUnexpectedTypes = typeof(TenancyModule).Assembly
            .GetExportedTypes()
            .Where(type => type != typeof(TenancyModule))
            .Select(type => type.FullName)
            .ToArray();

        Assert.HasCount(
            0,
            httpUnexpectedTypes,
            string.Join(Environment.NewLine, httpUnexpectedTypes));
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
            Path.Combine("src", "BuildingBlocks", "Full.NET.Data.Dapper", "Full.NET.Data.Dapper.csproj"),
            Path.Combine("src", "BuildingBlocks", "Full.NET.Migrations.DbUp", "Full.NET.Migrations.DbUp.csproj"),
            Path.Combine("src", "BuildingBlocks", "Full.NET.Seeding.Dapper", "Full.NET.Seeding.Dapper.csproj"),
            Path.Combine("src", "Hosts", "Full.NET.Host.Migrator", "Full.NET.Host.Migrator.csproj"),
            Path.Combine("tests", "Full.NET.UnitTests", "Full.NET.UnitTests.csproj"),
            Path.Combine("tests", "Full.NET.IntegrationTests", "Full.NET.IntegrationTests.csproj"),
            Path.Combine("tests", "Full.NET.ArchitectureTests", "Full.NET.ArchitectureTests.csproj"),
        };
        var consumers = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
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
        var sourceFiles = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
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
        var migratorProject = Path.Combine(
            "src",
            "Hosts",
            "Full.NET.Host.Migrator",
            "Full.NET.Host.Migrator.csproj");
        var migrationConsumers = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => XDocument.Load(path)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Any(reference => string.Equals(
                    Path.GetFileName(reference),
                    "Full.NET.Migrations.DbUp.csproj",
                    StringComparison.OrdinalIgnoreCase)))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var unapprovedProductionConsumers = migrationConsumers
            .Where(path => !path.StartsWith(
                $"tests{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(
                path,
                migratorProject,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.HasCount(
            0,
            unapprovedProductionConsumers,
            string.Join(Environment.NewLine, unapprovedProductionConsumers));
        CollectionAssert.Contains(migrationConsumers, migratorProject);

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
        var offenders = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
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

    private static string GetProjectNameFromReference(string reference)
    {
        var normalizedReference = reference.Trim().Replace('\\', '/');
        var fileName = normalizedReference[
            (normalizedReference.LastIndexOf('/') + 1)..];
        return Path.GetFileNameWithoutExtension(fileName);
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
        HostApi,
        HostMigrator,
        HostWorker,
    ];
}

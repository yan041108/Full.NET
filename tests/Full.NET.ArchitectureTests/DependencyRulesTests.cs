using System.Reflection;
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
        typeof(Full.NET.Hosting.Api.IApiResultMapper).Assembly,
        typeof(Full.NET.Localization.ILocaleNormalizer).Assembly,
        typeof(Full.NET.Migrations.DbUp.IDatabaseMigrationRunner).Assembly,
        typeof(Full.NET.Modularity.Modules.IFullNetModule).Assembly,
        typeof(Full.NET.Serialization.MessagePack.MessagePackIntegrationEventSerializer).Assembly,
        typeof(Full.NET.Seeding.Abstractions.SeedProfile).Assembly,
        typeof(Full.NET.Seeding.Dapper.SeedCommandLine).Assembly,
        typeof(Full.NET.Validation.FluentValidation.ServiceCollectionExtensions).Assembly,
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
    public void Tenancy_ExportsOnlyContractsAndCompositionEntryPoints()
    {
        var unexpectedPublicTypes = typeof(TenancyModule).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace != "Full.NET.Modules.Tenancy.Contracts")
            .Where(type => type != typeof(TenancyModule)
                && type != typeof(TenancyApplicationBuilderExtensions)
                && type != typeof(TenancyServiceCollectionExtensions))
            .Select(type => type.FullName)
            .ToArray();

        Assert.HasCount(0, unexpectedPublicTypes);
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
        var result = Types.InAssemblies(
                [typeof(IdentityModule).Assembly, typeof(TenancyModule).Assembly])
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
    public void Business_modules_do_not_depend_on_seeding_dapper()
    {
        var result = Types.InAssemblies(
                [typeof(IdentityModule).Assembly, typeof(TenancyModule).Assembly])
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
            typeof(IdentityModule));
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
        typeof(TenancyModule).Assembly,
        HostApi,
        HostMigrator,
        HostWorker,
    ];
}
